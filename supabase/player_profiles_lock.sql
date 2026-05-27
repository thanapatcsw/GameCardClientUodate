-- ============================================================
-- ⚠️ รันไฟล์นี้เป็นขั้นสุดท้าย หลังทดสอบใน Unity ว่า shop/quiz/match ทำงานผ่าน server แล้ว ⚠️
-- ล็อกการเขียน player_profiles จากฝั่ง client (ปิดช่องโกง gems/mmr ให้สมบูรณ์)
--
-- ต้องย้ายทุกเส้นทางเขียนไปเป็น Edge Function ครบก่อน:
--   [x] MMR + รางวัลจบเกม -> submit-match-result
--   [x] ซื้อ/สวมไอเทม      -> purchase-item / equip-cosmetic
--   [x] รางวัล Daily Quiz   -> grant-quiz-reward
--   [x] สร้างโปรไฟล์ครั้งแรก-> init-profile
-- (ทั้งหมด deploy แล้ว — เหลือทดสอบใน Unity ว่า client เรียกได้จริง)
-- ============================================================

alter table public.player_profiles enable row level security;

-- ลบ policy เดิมทั้งหมด (รวมตัวที่อนุญาตให้เจ้าของเขียนแถวตัวเอง ซึ่งเป็นช่องโกง)
do $$
declare pol record;
begin
    for pol in
        select policyname from pg_policies
        where schemaname = 'public' and tablename = 'player_profiles'
    loop
        execute format('drop policy if exists %I on public.player_profiles', pol.policyname);
    end loop;
end $$;

-- อ่านได้เฉพาะผู้ล็อกอิน (ใช้ทำ leaderboard + โหลดโปรไฟล์ตัวเอง) — ไม่เปิดให้ anon
create policy "pp_select_authenticated"
    on public.player_profiles
    for select
    to authenticated
    using (true);

-- ❌ ไม่มี policy insert/update/delete ให้ client
--    => client เขียน gems/mmr/wins/losses/owned_frames เองไม่ได้
--       เขียนได้เฉพาะ service_role (Edge Functions) ที่ตรวจสอบค่าก่อนเขียน

-- ตรวจสอบผล: rowsecurity = true และมี policy ชื่อ pp_select_authenticated ตัวเดียว (cmd = SELECT)
select tablename, rowsecurity from pg_tables where tablename = 'player_profiles';
select policyname, cmd, roles from pg_policies where tablename = 'player_profiles';
