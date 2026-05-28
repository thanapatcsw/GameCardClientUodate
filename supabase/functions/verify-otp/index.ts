// ============================================================
// Edge Function: verify-otp
// หน้าที่: ตรวจโค้ดที่ user กรอก (hash ตรงกัน + ยังไม่หมดอายุ + ยังไม่เกินจำนวนครั้ง)
//          ถ้าผ่าน -> สร้าง user จริงใน Supabase Auth (ยืนยันอีเมลให้เลย)
// deploy:  supabase functions deploy verify-otp --no-verify-jwt
// ============================================================
import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const SERVICE_ROLE = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;

const MAX_ATTEMPTS = 5; // กรอกผิดได้ไม่เกิน 5 ครั้งต่อโค้ด

const CORS = {
    "Access-Control-Allow-Origin": "*",
    "Access-Control-Allow-Headers": "authorization, apikey, content-type",
    "Access-Control-Allow-Methods": "POST, OPTIONS",
};

const json = (body: unknown, status = 200) =>
    new Response(JSON.stringify(body), {
        status,
        headers: { ...CORS, "Content-Type": "application/json" },
    });

async function sha256(text: string): Promise<string> {
    const buf = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
    return Array.from(new Uint8Array(buf))
        .map((b) => b.toString(16).padStart(2, "0"))
        .join("");
}

Deno.serve(async (req) => {
    if (req.method === "OPTIONS") return new Response("ok", { headers: CORS });
    if (req.method !== "POST") return json({ error: "Method not allowed" }, 405);

    try {
        const body = await req.json();
        // normalize email ให้ตรงกับ send-otp (lookup โดย .eq("email", ...) ต้องเป๊ะ)
        const email = String(body.email ?? "").trim().toLowerCase();
        const code = body.code;
        const password = body.password;
        const username = body.username;

        if (!email || !code || !password) {
            return json({ error: "ข้อมูลไม่ครบ" }, 400);
        }

        const db = createClient(SUPABASE_URL, SERVICE_ROLE);

        // ── ดึงโค้ดล่าสุดที่ยังไม่ใช้ของอีเมลนี้ ──
        const { data: row } = await db
            .from("otp_codes")
            .select("*")
            .eq("email", email)
            .eq("used", false)
            .order("created_at", { ascending: false })
            .limit(1)
            .maybeSingle();

        if (!row) {
            return json({ error: "ไม่พบรหัส OTP กรุณากดขอรหัสใหม่" }, 400);
        }

        // ── เช็คหมดอายุ ──
        if (new Date(row.expires_at).getTime() < Date.now()) {
            return json({ error: "รหัส OTP หมดอายุแล้ว กรุณากดส่งรหัสใหม่" }, 400);
        }

        // ── เช็คจำนวนครั้งที่ลอง ──
        if (row.attempts >= MAX_ATTEMPTS) {
            await db.from("otp_codes").update({ used: true }).eq("id", row.id);
            return json({ error: "กรอกผิดเกินกำหนด กรุณากดส่งรหัสใหม่" }, 429);
        }

        // ── เทียบ hash ──
        const inputHash = await sha256(String(code));
        if (inputHash !== row.code_hash) {
            await db.from("otp_codes").update({ attempts: row.attempts + 1 }).eq("id", row.id);
            const left = MAX_ATTEMPTS - (row.attempts + 1);
            return json({ error: `รหัส OTP ไม่ถูกต้อง (เหลือ ${left} ครั้ง)` }, 400);
        }

        // ── โค้ดถูกต้อง: สร้าง user จริง (ยืนยันอีเมลให้เลย) ──
        const { error: createErr } = await db.auth.admin.createUser({
            email,
            password,
            email_confirm: true,
            user_metadata: { username: username ?? "" },
        });

        if (createErr) {
            if (String(createErr.message).toLowerCase().includes("already")) {
                return json({ error: "อีเมลนี้ถูกใช้งานไปแล้ว" }, 409);
            }
            throw createErr;
        }

        // ── mark โค้ดว่าใช้แล้ว ──
        await db.from("otp_codes").update({ used: true }).eq("id", row.id);

        return json({ success: true });
    } catch (e) {
        console.error(e);
        return json({ error: "เกิดข้อผิดพลาดภายในระบบ" }, 500);
    }
});
