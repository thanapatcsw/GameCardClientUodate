-- =================================================================
-- Quiz Patch System Migration
-- GameCardDatabase | 2026-05-21
--
-- สร้างตาราง:
--   quiz_patches    → metadata ของแต่ละ pack/patch คำถาม
--   quiz_questions  → คำถามทั้งหมด (linked กับ patch)
--
-- RLS Policy:
--   - anon / authenticated อ่านได้เฉพาะ patch/questions ที่ is_active = true
--   - service_role เขียน/ลบได้เต็ม (ใช้จาก Admin Web ผ่าน service key)
-- =================================================================

-- --------------------------------------------------
-- 1. Table: quiz_patches
-- --------------------------------------------------
CREATE TABLE IF NOT EXISTS public.quiz_patches (
    id           bigserial PRIMARY KEY,
    name         text        NOT NULL,                  -- ชื่อ patch เช่น "Pack 1 - ความรู้ทั่วไป"
    description  text        DEFAULT '',               -- คำอธิบายเพิ่มเติม
    is_active    boolean     NOT NULL DEFAULT true,    -- toggle เปิด/ปิด patch นี้
    total_questions integer  NOT NULL DEFAULT 0,       -- จำนวนคำถามใน patch (อัปเดตอัตโนมัติ)
    created_at   timestamptz NOT NULL DEFAULT timezone('utc', now()),
    updated_at   timestamptz NOT NULL DEFAULT timezone('utc', now())
);

-- Comment
COMMENT ON TABLE  public.quiz_patches IS 'Metadata สำหรับแต่ละ patch/pack ของคำถามควิซ';
COMMENT ON COLUMN public.quiz_patches.is_active IS 'ถ้า false คำถามใน patch นี้จะไม่ถูกดึงไปใช้ในเกม';

-- --------------------------------------------------
-- 2. Table: quiz_questions
-- --------------------------------------------------
CREATE TABLE IF NOT EXISTS public.quiz_questions (
    id              bigserial   PRIMARY KEY,
    patch_id        bigint      NOT NULL REFERENCES public.quiz_patches(id) ON DELETE CASCADE,
    external_id     text        DEFAULT '',            -- id จากไฟล์ JSON เดิม (เช่น "q001")
    category        text        NOT NULL DEFAULT 'ทั่วไป',
    difficulty      text        NOT NULL DEFAULT 'medium'
                                CHECK (difficulty IN ('easy', 'medium', 'hard')),
    question        text        NOT NULL,
    choices         jsonb       NOT NULL,              -- array 4 ตัวเลือก ["A","B","C","D"]
    correct_index   smallint    NOT NULL
                                CHECK (correct_index BETWEEN 0 AND 3),
    created_at      timestamptz NOT NULL DEFAULT timezone('utc', now())
);

-- Index สำหรับ query ตาม patch
CREATE INDEX IF NOT EXISTS idx_quiz_questions_patch_id
    ON public.quiz_questions(patch_id);

-- Index สำหรับ query เฉพาะ patch ที่ active (ใช้ JOIN กับ quiz_patches)
CREATE INDEX IF NOT EXISTS idx_quiz_patches_active
    ON public.quiz_patches(is_active)
    WHERE is_active = true;

COMMENT ON TABLE  public.quiz_questions IS 'คลังคำถามควิซทั้งหมด จัดกลุ่มตาม patch';
COMMENT ON COLUMN public.quiz_questions.choices IS 'JSON array ของ 4 ตัวเลือก เช่น ["ก","ข","ค","ง"]';
COMMENT ON COLUMN public.quiz_questions.correct_index IS 'index ของตัวเลือกที่ถูก (0-3)';

-- --------------------------------------------------
-- 3. Function: auto-update quiz_patches.total_questions
-- --------------------------------------------------
CREATE OR REPLACE FUNCTION public.update_patch_question_count()
RETURNS TRIGGER
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        UPDATE public.quiz_patches
        SET total_questions = (
            SELECT COUNT(*) FROM public.quiz_questions WHERE patch_id = OLD.patch_id
        ),
        updated_at = timezone('utc', now())
        WHERE id = OLD.patch_id;
        RETURN OLD;
    ELSE
        UPDATE public.quiz_patches
        SET total_questions = (
            SELECT COUNT(*) FROM public.quiz_questions WHERE patch_id = NEW.patch_id
        ),
        updated_at = timezone('utc', now())
        WHERE id = NEW.patch_id;
        RETURN NEW;
    END IF;
END;
$$;

CREATE OR REPLACE TRIGGER trg_quiz_question_count
AFTER INSERT OR UPDATE OR DELETE ON public.quiz_questions
FOR EACH ROW EXECUTE FUNCTION public.update_patch_question_count();

-- --------------------------------------------------
-- 4. Function: auto-update quiz_patches.updated_at
-- --------------------------------------------------
CREATE OR REPLACE FUNCTION public.update_quiz_patch_timestamp()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.updated_at = timezone('utc', now());
    RETURN NEW;
END;
$$;

CREATE OR REPLACE TRIGGER trg_quiz_patch_updated_at
BEFORE UPDATE ON public.quiz_patches
FOR EACH ROW EXECUTE FUNCTION public.update_quiz_patch_timestamp();

-- --------------------------------------------------
-- 5. Function: get_active_questions
--    ดึงคำถามทั้งหมดจาก patch ที่ active (ใช้ใน Unity)
-- --------------------------------------------------
CREATE OR REPLACE FUNCTION public.get_active_questions()
RETURNS TABLE (
    id            bigint,
    patch_id      bigint,
    patch_name    text,
    external_id   text,
    category      text,
    difficulty    text,
    question      text,
    choices       jsonb,
    correct_index smallint
)
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = public
AS $$
    SELECT
        q.id,
        q.patch_id,
        p.name  AS patch_name,
        q.external_id,
        q.category,
        q.difficulty,
        q.question,
        q.choices,
        q.correct_index
    FROM public.quiz_questions q
    INNER JOIN public.quiz_patches p ON p.id = q.patch_id
    WHERE p.is_active = true
    ORDER BY q.id;
$$;

-- --------------------------------------------------
-- 6. Function: import_quiz_patch (อัปโหลด JSON ทีเดียว)
--    ใช้จาก Admin Web ส่ง array ของคำถามพร้อม patch name
-- --------------------------------------------------
CREATE OR REPLACE FUNCTION public.import_quiz_patch(
    p_name        text,
    p_description text,
    p_questions   jsonb       -- array ของ { external_id, category, difficulty, question, choices, correct_index }
)
RETURNS jsonb
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
DECLARE
    v_patch_id bigint;
    v_count    integer := 0;
    v_item     jsonb;
BEGIN
    IF p_name IS NULL OR btrim(p_name) = '' THEN
        RETURN jsonb_build_object('status', 'error', 'message', 'patch name is required');
    END IF;

    IF jsonb_array_length(p_questions) = 0 THEN
        RETURN jsonb_build_object('status', 'error', 'message', 'questions array is empty');
    END IF;

    -- สร้าง patch ใหม่
    INSERT INTO public.quiz_patches (name, description, is_active)
    VALUES (btrim(p_name), coalesce(btrim(p_description), ''), true)
    RETURNING id INTO v_patch_id;

    -- insert คำถามทั้งหมดในครั้งเดียว
    FOR v_item IN SELECT * FROM jsonb_array_elements(p_questions)
    LOOP
        INSERT INTO public.quiz_questions (
            patch_id,
            external_id,
            category,
            difficulty,
            question,
            choices,
            correct_index
        ) VALUES (
            v_patch_id,
            coalesce(v_item->>'id', v_item->>'external_id', ''),
            coalesce(nullif(btrim(v_item->>'category'), ''), 'ทั่วไป'),
            coalesce(
                CASE WHEN (v_item->>'difficulty') IN ('easy','medium','hard')
                     THEN v_item->>'difficulty' END,
                'medium'
            ),
            btrim(v_item->>'question'),
            CASE
                WHEN jsonb_typeof(v_item->'choices') = 'array' THEN v_item->'choices'
                ELSE '[]'::jsonb
            END,
            coalesce((v_item->>'correctIndex')::smallint, (v_item->>'correct_index')::smallint, 0)
        );
        v_count := v_count + 1;
    END LOOP;

    RETURN jsonb_build_object(
        'status',     'success',
        'patch_id',   v_patch_id,
        'patch_name', p_name,
        'imported',   v_count
    );
END;
$$;

-- --------------------------------------------------
-- 7. RLS Policies
-- --------------------------------------------------
ALTER TABLE public.quiz_patches   ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.quiz_questions ENABLE ROW LEVEL SECURITY;

-- อ่าน patch ที่ active ได้ทุกคน (รวม anon / Unity client)
CREATE POLICY "read_active_patches" ON public.quiz_patches
    FOR SELECT
    USING (is_active = true);

-- อ่านคำถามจาก patch ที่ active ได้ทุกคน
CREATE POLICY "read_active_questions" ON public.quiz_questions
    FOR SELECT
    USING (
        EXISTS (
            SELECT 1 FROM public.quiz_patches p
            WHERE p.id = patch_id AND p.is_active = true
        )
    );

-- service_role (Admin Web) เขียน/อ่าน/ลบได้ทุกอย่าง
-- (service_role bypass RLS โดย default — ไม่ต้องสร้าง policy เพิ่ม)

-- --------------------------------------------------
-- 8. Grant execute ให้ anon / authenticated เรียก function ได้
-- --------------------------------------------------
GRANT EXECUTE ON FUNCTION public.get_active_questions()       TO anon, authenticated;
GRANT EXECUTE ON FUNCTION public.import_quiz_patch(text,text,jsonb) TO authenticated;
-- import_quiz_patch ให้เฉพาะ authenticated (Admin login แล้ว) เท่านั้น
