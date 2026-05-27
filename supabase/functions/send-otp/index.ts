// ============================================================
// Edge Function: send-otp
// หน้าที่: สุ่มโค้ด OTP 6 หลัก -> hash เก็บลง DB -> ส่งอีเมลผ่าน Resend
// generate ทำบน server เท่านั้น (client มองไม่เห็นเลขจริง)
// deploy:  supabase functions deploy send-otp --no-verify-jwt
// ============================================================
import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

// secrets ตั้งด้วย: supabase secrets set BREVO_API_KEY=... OTP_FROM_EMAIL=...
// OTP_FROM_EMAIL ต้องเป็นอีเมลที่ verify ใน Brevo แล้ว (Single Sender)
const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const SERVICE_ROLE = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
const BREVO_API_KEY = Deno.env.get("BREVO_API_KEY")!;
const FROM_EMAIL = Deno.env.get("OTP_FROM_EMAIL")!;

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

// hash โค้ดด้วย SHA-256 ก่อนเก็บ (ไม่เก็บ plaintext)
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
        const { email, username } = await req.json();

        // ── ตรวจ input ──
        if (!email || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
            return json({ error: "รูปแบบอีเมลไม่ถูกต้อง" }, 400);
        }

        const db = createClient(SUPABASE_URL, SERVICE_ROLE);

        // ── กันสมัครด้วยอีเมลที่มี user อยู่แล้ว (เช็คตั้งแต่ต้น ไม่ต้องรอ verify) ──
        const { data: exists, error: existsErr } = await db.rpc("email_exists", {
            p_email: email,
        });
        if (existsErr) throw existsErr;
        if (exists) {
            return json({ error: "อีเมลนี้ถูกใช้งานไปแล้ว กรุณาใช้อีเมลอื่น" }, 409);
        }

        // ── housekeeping: ลบแถวที่หมดอายุเกิน 1 วันทิ้ง (เผื่อยังไม่ได้เปิด pg_cron) ──
        await db.from("otp_codes").delete().lt(
            "expires_at",
            new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString(),
        );

        // ── กันส่งซ้ำถี่เกินไป: ห้ามส่งใหม่ภายใน 60 วินาที ──
        const { data: recent } = await db
            .from("otp_codes")
            .select("created_at")
            .eq("email", email)
            .order("created_at", { ascending: false })
            .limit(1)
            .maybeSingle();

        if (recent) {
            const ageSec = (Date.now() - new Date(recent.created_at).getTime()) / 1000;
            if (ageSec < 60) {
                return json(
                    { error: `กรุณารออีก ${Math.ceil(60 - ageSec)} วินาทีก่อนขอรหัสใหม่` },
                    429,
                );
            }
        }

        // ── สุ่มโค้ด 6 หลัก (crypto-secure) ──
        const code = String(crypto.getRandomValues(new Uint32Array(1))[0] % 1_000_000)
            .padStart(6, "0");
        const codeHash = await sha256(code);
        const expiresAt = new Date(Date.now() + 5 * 60 * 1000).toISOString(); // 5 นาที

        // ── ทำให้โค้ดเก่าที่ยังไม่ใช้ของอีเมลนี้ใช้ไม่ได้ ──
        await db.from("otp_codes").update({ used: true }).eq("email", email).eq("used", false);

        // ── เก็บโค้ดใหม่ ──
        const { error: insErr } = await db
            .from("otp_codes")
            .insert({ email, code_hash: codeHash, expires_at: expiresAt });
        if (insErr) throw insErr;

        // ── ส่งอีเมลผ่าน Brevo (Single Sender — ไม่ต้องมีโดเมน) ──
        const emailRes = await fetch("https://api.brevo.com/v3/smtp/email", {
            method: "POST",
            headers: {
                "api-key": BREVO_API_KEY,
                "Content-Type": "application/json",
                "accept": "application/json",
            },
            body: JSON.stringify({
                sender: { email: FROM_EMAIL, name: "Startup City" },
                to: [{ email, name: username ?? "" }],
                subject: "รหัส OTP ยืนยันการสมัคร — Startup City",
                htmlContent: `
                    <div style="font-family:sans-serif;max-width:420px;margin:auto">
                      <h2>ยืนยันการสมัครสมาชิก</h2>
                      <p>สวัสดีคุณ ${username ?? ""} รหัส OTP ของคุณคือ</p>
                      <p style="font-size:32px;font-weight:bold;letter-spacing:8px">${code}</p>
                      <p style="color:#666">รหัสนี้ใช้ได้ภายใน 5 นาที ห้ามบอกผู้อื่น</p>
                    </div>`,
            }),
        });

        if (!emailRes.ok) {
            const detail = await emailRes.text();
            console.error("Brevo error:", detail);
            return json({ error: "ส่งอีเมลไม่สำเร็จ กรุณาลองใหม่" }, 502);
        }

        return json({ success: true });
    } catch (e) {
        console.error(e);
        return json({ error: "เกิดข้อผิดพลาดภายในระบบ" }, 500);
    }
});
