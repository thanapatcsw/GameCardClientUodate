// ============================================================
// Edge Function: grant-quiz-reward  (server-authoritative)
// server เป็นคนกำหนดจำนวน gems + กันรับซ้ำในวันเดียวกัน (daily_quiz_claims)
// หมายเหตุ: ยังเชื่อว่า client "ตอบถูก" (ตรรกะควิซอยู่ฝั่ง client) แต่จำกัดได้ 1 ครั้ง/วัน
//           => อย่างมากที่โกงได้คือรับ 100/วันโดยไม่ตอบ ซึ่งเท่ากับที่เล่นจริงได้อยู่แล้ว
// deploy: supabase functions deploy grant-quiz-reward   (verify-jwt = เปิด)
// ============================================================
import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const ANON_KEY = Deno.env.get("SUPABASE_ANON_KEY")!;
const SERVICE_ROLE = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;

const QUIZ_REWARD = 100; // gems/วัน (ตรงกับ DailyQuizManager)

const CORS = {
    "Access-Control-Allow-Origin": "*",
    "Access-Control-Allow-Headers": "authorization, apikey, content-type",
    "Access-Control-Allow-Methods": "POST, OPTIONS",
};
const json = (b: unknown, s = 200) =>
    new Response(JSON.stringify(b), { status: s, headers: { ...CORS, "Content-Type": "application/json" } });

Deno.serve(async (req) => {
    if (req.method === "OPTIONS") return new Response("ok", { headers: CORS });
    if (req.method !== "POST") return json({ error: "Method not allowed" }, 405);

    try {
        const authHeader = req.headers.get("Authorization") ?? "";
        const userClient = createClient(SUPABASE_URL, ANON_KEY, {
            global: { headers: { Authorization: authHeader } },
        });
        const { data: { user } } = await userClient.auth.getUser();
        if (!user) return json({ error: "ไม่ได้เข้าสู่ระบบ" }, 401);

        const db = createClient(SUPABASE_URL, SERVICE_ROLE);
        // ใช้เวลาไทย (UTC+7) เพื่อให้ "วันใหม่" ของ daily quiz รีเซ็ตเที่ยงคืนตามผู้เล่นจริง
        // ถ้าใช้ UTC ตรงๆ จะรีเซ็ตตอน 7 โมงเช้าไทย — ผู้เล่นสับสน
        const today = new Date(Date.now() + 7 * 60 * 60 * 1000).toISOString().slice(0, 10); // YYYY-MM-DD (Asia/Bangkok)

        // กันรับซ้ำ: insert claim ของวันนี้ ถ้าชนกัน = รับไปแล้ว
        const { error: claimErr } = await db
            .from("daily_quiz_claims")
            .insert({ user_id: user.id, claim_date: today });
        if (claimErr) {
            if (claimErr.code === "23505") { // unique_violation
                return json({ error: "วันนี้รับรางวัลควิซไปแล้ว" }, 409);
            }
            throw claimErr;
        }

        // อ่าน gems ปัจจุบันแล้วบวกรางวัล (server บวกเอง)
        const { data: profile } = await db
            .from("player_profiles").select("gems").eq("id", user.id).maybeSingle();
        const newGems = (profile?.gems ?? 0) + QUIZ_REWARD;

        const { error: upErr } = await db.from("player_profiles").update({
            gems: newGems,
            updated_at: new Date().toISOString(),
        }).eq("id", user.id);
        if (upErr) throw upErr;

        return json({ gems: newGems, reward: QUIZ_REWARD });
    } catch (e) {
        console.error(e);
        return json({ error: "เกิดข้อผิดพลาดภายในระบบ" }, 500);
    }
});
