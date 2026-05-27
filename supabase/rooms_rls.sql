-- ============================================================
-- ล็อกความปลอดภัยตาราง rooms — รันใน SQL Editor (รันทั้งไฟล์)
-- ปัญหาเดิม: anon key (ฝังในตัวเกม) เขียน/ลบ/แก้ rooms ได้ → ป่วนระบบจับคู่
-- แก้: เปิด RLS + ลบ policy เก่าที่อนุญาตทุกอย่างทิ้ง + ใส่ policy ใหม่ "อ่านได้ เขียนไม่ได้"
--      ระบบ matchmaking ฝั่ง server ใช้ service_role ซึ่ง bypass RLS จึงยังทำงานปกติ
-- ============================================================

alter table public.rooms enable row level security;

-- ลบ policy เดิมทั้งหมดบน rooms (รวมตัวที่อนุญาตทุกอย่างที่ค้างอยู่)
do $$
declare pol record;
begin
    for pol in
        select policyname from pg_policies
        where schemaname = 'public' and tablename = 'rooms'
    loop
        execute format('drop policy if exists %I on public.rooms', pol.policyname);
    end loop;
end $$;

-- อนุญาตเฉพาะการอ่าน (ใช้แสดง lobby) — ไม่มี policy insert/update/delete
create policy "rooms_public_read"
    on public.rooms
    for select
    to anon, authenticated
    using (true);

-- ตรวจสอบผล: ควรเห็น rowsecurity = true และมี policy ชื่อ rooms_public_read เพียงตัวเดียว (cmd = SELECT)
select tablename, rowsecurity from pg_tables where tablename = 'rooms';
select policyname, cmd, roles from pg_policies where tablename = 'rooms';
