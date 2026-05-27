-- ============================================================
-- ส่วนเสริมของระบบ OTP — รันใน SQL Editor หลังจาก otp_schema.sql
-- 1) ฟังก์ชันเช็คว่าอีเมลถูกสมัครไปแล้วหรือยัง
-- 2) งานล้างแถว OTP ที่หมดอายุอัตโนมัติ (pg_cron)
-- ============================================================

-- ── 1) เช็คอีเมลซ้ำ ──────────────────────────────────────────
-- ตาราง auth.users เข้าถึงตรง ๆ จาก client ไม่ได้
-- จึงห่อด้วยฟังก์ชัน SECURITY DEFINER ให้ Edge Function เรียกผ่าน RPC
create or replace function public.email_exists(p_email text)
returns boolean
language sql
security definer
set search_path = auth, public
as $$
    select exists (select 1 from auth.users where email = lower(p_email));
$$;

-- อนุญาตให้ service_role เรียกได้ (Edge Function ใช้ key นี้)
revoke all on function public.email_exists(text) from public, anon, authenticated;
grant execute on function public.email_exists(text) to service_role;


-- ── 2) ล้างแถวที่หมดอายุอัตโนมัติ ────────────────────────────
-- ลบแถวที่หมดอายุเกิน 1 วัน (เก็บ log ระยะสั้นไว้ debug ได้)
create or replace function public.cleanup_expired_otp()
returns void
language sql
as $$
    delete from public.otp_codes
    where expires_at < now() - interval '1 day';
$$;

-- ตั้งเวลารันทุกวันตี 3 ด้วย pg_cron
-- หมายเหตุ: ต้องเปิด extension "pg_cron" ก่อนที่
--   Dashboard → Database → Extensions → ค้น "pg_cron" → Enable
create extension if not exists pg_cron;

select cron.schedule(
    'cleanup-expired-otp',         -- ชื่อ job
    '0 3 * * *',                   -- ทุกวัน 03:00
    $$ select public.cleanup_expired_otp(); $$
);
