# 🚀 GameCardClient - คู่มือการติดตั้งสำหรับทีมงาน (Setup Guide)

โปรเจคเกมการ์ด Multiplayer พัฒนาด้วย Unity 6 (Photon Fusion + Supabase)

---

## 📥 1. วิธีโหลดโปรเจคลงเครื่อง (GitHub Clone)
1. ติดตั้ง **Git** ในเครื่อง (หากยังไม่มี โหลดที่: [git-scm.com](https://git-scm.com/))
2. สร้างโฟลเดอร์สำหรับเก็บโปรเจคในเครื่องคุณ
3. เปิด Terminal หรือ Command Prompt ในโฟลเดอร์นั้น แล้วพิมพ์:
   ```bash
   git clone https://github.com/thanapatcsw/GameCardClientUodate.git
   ```
4. รอจนกว่าจะโหลดเสร็จ

---

## 🛠️ 2. การเปิดโปรเจคด้วย Unity
1. เปิด **Unity Hub**
2. กดปุ่ม **Add** -> **Add project from disk**
3. เลือกโฟลเดอร์ `GameCardClient` ที่เพิ่งโหลดมา
4. **สำคัญ:** ต้องใช้ Unity Version **`6000.3.9f1`** (Unity 6)
   * *หากไม่มีเวอร์ชันนี้ ให้กด Install ผ่าน Unity Hub (แท็บ Archive)*
5. รอ Unity ทำการ Import Assets (ขั้นตอนนี้อาจใช้เวลา 5-10 นาที)

---

## 🌐 3. การตั้งค่าระบบเครือข่าย (Photon Fusion)
เพื่อให้ระบบ Multiplayer ทำงานได้ คุณต้องใส่ App ID ที่ได้รับมา:
1. ในหน้าต่าง Unity Project ค้นหาไฟล์ชื่อ: `NetworkProjectConfig` 
   *(อยู่ที่ Assets > Photon > Fusion > Resources)*
2. คลิกที่ไฟล์นั้น แล้วมองไปที่หน้าต่าง **Inspector**
3. ที่หัวข้อ **App Id Fusion** ให้วางโค้ดนี้ลงไป:
   `35ac34e9-137f-46ab-b2c3-95ae99a5d5ed`
4. กด Ctrl + S เพื่อเซฟ

---

## 💾 4. การตั้งค่าฐานข้อมูล (Supabase)
เพื่อให้ระบบ Login และจัดอันดับทำงานได้ ต้องตั้งค่าใน Scene แรกของเกม:
1. เปิด Scene ชื่อ: **`LoginScece`** *(อยู่ที่ Assets > Scenes)*
2. ในหน้าต่าง **Hierarchy** หา GameObject ที่ชื่อว่า **`SupabaseManager`**
3. คลิกที่มัน แล้วดูที่หน้าต่าง **Inspector** จะเจอช่องให้ใส่ข้อมูลดังนี้:
   * **Supabase Url:** `https://uwspzhwvjpkcjpoqgkhp.supabase.co`
   * **Supabase Key (Anon Key):** 
     `eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InV3c3B6aHd2anBrY2pwb3Fna2hwIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzQ4MzUwMzYsImV4cCI6MjA5MDQxMTAzNn0.hgTN21pBcTD2meqXxKnydit0U7inI3OpMOAFVy9NtEE`
4. กด Ctrl + S เพื่อเซฟ

---

## 🎮 5. วิธีรันเกมเพื่อทดสอบ
1. ตรวจสอบว่าอยู่ที่ Scene **`LoginScece`**
2. กดปุ่ม **Play** ใน Unity
3. ระบบจะทำงานเชื่อมต่อกับ Database และ Server Photon ทันที
4. หากต้องการทดสอบ Multiplayer (2 จอ) ให้ใช้ระบบ **ParrelSync** (ถ้ามีการติดตั้งไว้) หรือ Build เป็นไฟล์ .exe ออกมาเพื่อเปิดทดสอบคู่กัน

---

### 📝 หมายเหตุเพิ่มเติม
* หากเปิดโปรเจคครั้งแรกแล้วเจอ Error สีแดงเต็ม Console ให้ลองไปที่เมนู `Window > Analysis > Project Validator` (ถ้ามี) หรือกดปุ่ม `Fusion > Rebuild Open Scenes` 
* หากมีคำถามเพิ่มเติม ติดต่อหัวหน้าทีมได้ทันที!
