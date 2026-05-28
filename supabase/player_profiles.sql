-- ============================================================
-- ตาราง player_profiles — โปรไฟล์ผู้เล่นหลัก (สกุลเงิน, สถิติ, การจัดอันดับ, ไอเทมตกแต่ง)
--
-- หมายเหตุ:
--   • id อ้างอิง auth.users(id) แบบ 1:1 (ผูกกับบัญชี Supabase Auth)
--   • เขียน/แก้ไขได้เฉพาะ service_role (Edge Functions) เท่านั้น
--     ส่วน RLS policy ฝั่ง client ดูที่ player_profiles_lock.sql
--   • คอลัมน์ coins (สกุลเงินพรีเมียมเดิม) ถูกถอดออกแล้ว ดู migrations/20260522_remove_coins.sql
-- ============================================================

create table if not exists public.player_profiles (
    id                 uuid        not null
                                   references auth.users (id) on delete cascade,
    username           text        not null default 'Player',
    gems               integer     not null default 0,
    mmr                integer     not null default 1000,
    wins               integer     not null default 0,
    losses             integer     not null default 0,
    owned_frames       text[]      not null default array['frame_default']::text[],
    equipped_frame     text        not null default 'frame_default',
    selected_character integer              default 0,
    updated_at         timestamptz not null default now(),

    constraint player_profiles_pkey primary key (id)
);
