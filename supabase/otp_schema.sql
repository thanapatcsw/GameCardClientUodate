-- ============================================================
-- ระบบ OTP แบบเขียนเอง (custom) — ตารางเก็บรหัส OTP
-- รันใน Supabase Dashboard > SQL Editor ครั้งเดียว
-- ============================================================

create table if not exists public.otp_codes (
    id          uuid        primary key default gen_random_uuid(),
    email       text        not null,
    code_hash   text        not null,            -- เก็บ SHA-256 ของโค้ด ไม่เก็บเลขตรง ๆ
    expires_at  timestamptz not null,            -- หมดอายุ = now() + 5 นาที
    attempts    int         not null default 0,  -- จำนวนครั้งที่กรอกผิด (กัน brute force)
    used        boolean     not null default false,
    created_at  timestamptz not null default now()
);

-- index ช่วยค้นโค้ดล่าสุดของแต่ละอีเมลได้เร็ว
create index if not exists idx_otp_email_created
    on public.otp_codes (email, created_at desc);

-- เปิด RLS แล้วไม่สร้าง policy ใด ๆ
-- => client (anon key) อ่าน/เขียนตารางนี้ตรง ๆ ไม่ได้เลย
--    เข้าถึงได้เฉพาะ Edge Function ที่ใช้ service_role key เท่านั้น
alter table public.otp_codes enable row level security;
