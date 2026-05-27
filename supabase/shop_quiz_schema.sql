-- ============================================================
-- เฟส shop/quiz — ตารางฝั่ง server (รันใน SQL Editor)
-- 1) shop_items : ราคาไอเทม (source of truth — client ปลอมราคาไม่ได้)
-- 2) daily_quiz_claims : กันรับรางวัลควิซซ้ำในวันเดียวกัน
-- ============================================================

-- ── 1) ราคาไอเทม ────────────────────────────────────────────
create table if not exists public.shop_items (
    item_id text primary key,
    price   int  not null check (price >= 0)
);

insert into public.shop_items (item_id, price) values
    ('frame_default', 0),
    ('frame_green',  25),
    ('frame_cyan',   30),
    ('frame_red',    35),
    ('frame_purple', 40),
    ('frame_gold',   50),
    ('frame_rainbow',100)
on conflict (item_id) do update set price = excluded.price;

alter table public.shop_items enable row level security;
drop policy if exists "shop_items_read" on public.shop_items;
-- อ่านราคาได้ (หน้าร้านใช้แสดง) แต่ client แก้ราคาไม่ได้ (ไม่มี policy write)
create policy "shop_items_read"
    on public.shop_items for select to anon, authenticated using (true);


-- ── 2) บันทึกการรับรางวัลควิซรายวัน ──────────────────────────
create table if not exists public.daily_quiz_claims (
    user_id    uuid not null references auth.users(id) on delete cascade,
    claim_date date not null,
    created_at timestamptz not null default now(),
    primary key (user_id, claim_date)   -- 1 คน รับได้ 1 ครั้ง/วัน
);

alter table public.daily_quiz_claims enable row level security;
-- ไม่มี policy ให้ client => เข้าถึงได้เฉพาะ service_role (Edge Function) เท่านั้น
