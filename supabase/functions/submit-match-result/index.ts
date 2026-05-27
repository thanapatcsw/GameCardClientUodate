// ============================================================
// Edge Function: submit-match-result (server-authoritative)
// client ส่งแค่ "อันดับที่จบ" + "จำนวนผู้เล่น" — server เป็นคนคำนวณ MMR/gems เอง
// => client เขียนค่า MMR/gems ตรง ๆ ไม่ได้ (กันเติมเงิน/ปั่นแรงก์แบบ +9999)
// หมายเหตุ: ยังเชื่อ "อันดับที่อ้าง" อยู่ (P2P ไม่มี authoritative server) แต่จำกัด
//           ความเสียหายให้อยู่ในกรอบค่าที่ถูกต้องของเกม
// deploy: supabase functions deploy submit-match-result   (ต้อง verify-jwt = เปิด)
// ============================================================
import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const ANON_KEY = Deno.env.get("SUPABASE_ANON_KEY")!;
const SERVICE_ROLE = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;

const CORS = {
    "Access-Control-Allow-Origin": "*",
    "Access-Control-Allow-Headers": "authorization, apikey, content-type",
    "Access-Control-Allow-Methods": "POST, OPTIONS",
};
const json = (b: unknown, s = 200) =>
    new Response(JSON.stringify(b), { status: s, headers: { ...CORS, "Content-Type": "application/json" } });

// ── พอร์ตสูตรจาก MmrCalculator.cs (ต้องตรงกับฝั่งเกม) ──
function mmrDelta(placement: number, totalPlayers: number): number {
    if (totalPlayers === 2) return placement === 1 ? 25 : -25;
    if (totalPlayers === 3) {
        if (placement === 1) return 25;
        if (placement === 2) return -5;
        return -20;
    }
    if (placement === 1) return 30;
    if (placement === 2) return 10;
    if (placement === 3) return -10;
    return -25;
}
// รางวัล gems ตามอันดับ (ตรงกับ GameController)
function gemReward(placement: number): number {
    return placement === 1 ? 5 : placement === 2 ? 3 : placement === 3 ? 2 : 1;
}

Deno.serve(async (req) => {
    if (req.method === "OPTIONS") return new Response("ok", { headers: CORS });
    if (req.method !== "POST") return json({ error: "Method not allowed" }, 405);

    try {
        // ── ระบุตัวตนผู้เล่นจาก JWT (verify-jwt เปิดอยู่) ──
        const authHeader = req.headers.get("Authorization") ?? "";
        const userClient = createClient(SUPABASE_URL, ANON_KEY, {
            global: { headers: { Authorization: authHeader } },
        });
        const { data: { user }, error: userErr } = await userClient.auth.getUser();
        if (userErr || !user) return json({ error: "ไม่ได้เข้าสู่ระบบ" }, 401);

        // ── ตรวจ input ──
        const { placement, totalPlayers } = await req.json();
        if (
            !Number.isInteger(placement) || !Number.isInteger(totalPlayers) ||
            totalPlayers < 2 || totalPlayers > 4 ||
            placement < 1 || placement > totalPlayers
        ) {
            return json({ error: "ข้อมูลผลการแข่งไม่ถูกต้อง" }, 400);
        }

        const db = createClient(SUPABASE_URL, SERVICE_ROLE);

        // ── อ่านค่าปัจจุบันของผู้เล่น (ฝั่ง server เท่านั้น) ──
        const { data: profile } = await db
            .from("player_profiles")
            .select("mmr, wins, losses")
            .eq("id", user.id)
            .maybeSingle();

        const curMmr = profile?.mmr ?? 1000;
        const curWins = profile?.wins ?? 0;
        const curLosses = profile?.losses ?? 0;

        // ── server คำนวณเอง ──
        const delta = mmrDelta(placement, totalPlayers);
        const newMmr = Math.max(0, curMmr + delta);
        const reward = gemReward(placement);
        const won = placement === 1;

        // อ่าน gems ปัจจุบันแล้วบวกรางวัล (server เป็นคนบวก ไม่ใช่ client ส่งยอดมา)
        const { data: gemRow } = await db
            .from("player_profiles")
            .select("gems")
            .eq("id", user.id)
            .maybeSingle();
        const newGems = (gemRow?.gems ?? 0) + reward;

        // ── เขียนกลับ (service_role) ──
        const { error: upErr } = await db.from("player_profiles").update({
            mmr: newMmr,
            wins: won ? curWins + 1 : curWins,
            losses: won ? curLosses : curLosses + 1,
            gems: newGems,
            updated_at: new Date().toISOString(),
        }).eq("id", user.id);
        if (upErr) throw upErr;

        return json({
            newMmr,
            mmrDelta: delta,
            gemReward: reward,
            gems: newGems,
            won,
        });
    } catch (e) {
        console.error(e);
        return json({ error: "เกิดข้อผิดพลาดภายในระบบ" }, 500);
    }
});
