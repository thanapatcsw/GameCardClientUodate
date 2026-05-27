# ระบบ OTP แบบเขียนเอง (Custom) — คู่มือติดตั้ง

ระบบนี้ **ไม่ได้ใช้** OTP สำเร็จรูปของ Supabase Auth แต่เราเขียน logic เองทั้งหมด
(สุ่มโค้ด → เก็บ hash → หมดอายุ → จำกัดจำนวนครั้ง → ยืนยัน) บน Supabase Edge Function

## ภาพรวม

```
[index.html]                      [Edge Function]                 [Postgres]
 กรอกอีเมล ── send-otp ──────────► สุ่ม 6 หลัก, hash, ส่งเมล ──────► ตาราง otp_codes
 กรอกโค้ด  ── verify-otp ────────► เทียบ hash + เวลา + ครั้ง
                                   ผ่าน → createUser ──────────────► auth.users
```

จุดสำคัญ: **สุ่มโค้ดและยืนยันทำบน server เท่านั้น** client ไม่เคยเห็นเลข OTP จริง
และ API key ของผู้ส่งอีเมลถูกเก็บเป็น secret ไม่อยู่ในหน้าเว็บ

## ขั้นตอนติดตั้ง

### 1. สร้างตาราง
เปิด Supabase Dashboard → SQL Editor → วางเนื้อหาไฟล์ [otp_schema.sql](otp_schema.sql) แล้ว Run

### 2. สมัคร Brevo + verify อีเมลผู้ส่ง (ไม่ต้องมีโดเมน)
- ไปที่ https://www.brevo.com สมัคร (ฟรี 300 อีเมล/วัน)
- **Senders, Domains & Dedicated IPs → Senders → Add a Sender**
  ใส่อีเมลของคุณ → Brevo ส่งลิงก์ยืนยันมา → กดยืนยัน (Single Sender Verification)
- เมนู **SMTP & API → API Keys → Generate a new API key** → คัดลอกเก็บไว้
- อีเมลที่ verify แล้วนี้ใช้เป็นค่า `OTP_FROM_EMAIL` และส่งหา **ใครก็ได้**

### 3. ติดตั้ง Supabase CLI แล้ว login
```powershell
npm i -g supabase
supabase login
supabase link --project-ref uwspzhwvjpkcjpoqgkhp
```

### 4. ตั้ง secrets
```powershell
supabase secrets set BREVO_API_KEY=xkeysib-xxxxxxxx
supabase secrets set OTP_FROM_EMAIL=อีเมลที่verifyใน Brevo แล้ว
```
> `SUPABASE_URL` และ `SUPABASE_SERVICE_ROLE_KEY` มีให้อัตโนมัติใน Edge Function อยู่แล้ว

### 5. Deploy ทั้งสองฟังก์ชัน
```powershell
supabase functions deploy send-otp --no-verify-jwt
supabase functions deploy verify-otp --no-verify-jwt
```
> `--no-verify-jwt` เพราะผู้สมัครยังไม่มี session — เรายืนยันตัวตนด้วยโค้ด OTP เอง

### 6. ทดสอบ
เปิดหน้าเว็บสมัคร → กรอกอีเมล → เช็คกล่องเมล (อาจอยู่ Spam) → กรอกโค้ด 6 หลัก

## กลไกความปลอดภัยที่ทำเอง (เขียนอธิบายในรายงานได้)

| กลไก | ทำที่ไหน |
|------|---------|
| สุ่มโค้ดแบบ crypto-secure | `send-otp` ใช้ `crypto.getRandomValues` |
| เก็บเป็น SHA-256 hash ไม่เก็บ plaintext | ทั้งสองฟังก์ชัน |
| หมดอายุ 5 นาที | `expires_at` + เช็คใน `verify-otp` |
| กัน brute force (ผิดเกิน 5 ครั้งล็อกโค้ด) | `attempts` ใน `verify-otp` |
| กันส่งซ้ำถี่ (60 วิ) | `send-otp` |
| โค้ดใช้ครั้งเดียว / ขอใหม่ล้างของเก่า | `used` flag |
| client เข้าตารางตรง ๆ ไม่ได้ | RLS เปิดแบบไม่มี policy |
