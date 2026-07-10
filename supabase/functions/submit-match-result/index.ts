// ============================================================
// Edge Function: submit-match-result (server-authoritative)
// client ส่ง "อันดับ + จำนวนผู้เล่น + room_code/room_id" — server คำนวณ MMR/gems เอง
// กันโกง 2 ชั้น:
//   (1) server คำนวณค่า → client ส่ง +9999 ไม่ได้
//   (2) ผูกผลกับ "ห้องจริงที่ status='finished'" + dedup ใน match_results UNIQUE(user_id, room_id)
//       → เรียก loop ซ้ำ/ฟาร์มรางวัลไม่ได้ (1 ห้อง = รับได้ครั้งเดียวต่อคน) และต้องเป็นแมตช์ออนไลน์จริง
// หมายเหตุ: ไม่มีตาราง participant → ยังเช็ค membership ไม่ได้ (จำกัดที่ห้อง finished + dedup)
//           ปลายทางที่สมบูรณ์ = ให้ host (online authority) ส่งผลของทุกคน
// deploy: supabase functions deploy submit-match-result   (verify-jwt = เปิด)
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
function gemReward(placement: number): number {
    return placement === 1 ? 5 : placement === 2 ? 3 : placement === 3 ? 2 : 1;
}

Deno.serve(async (req) => {
    if (req.method === "OPTIONS") return new Response("ok", { headers: CORS });
    if (req.method !== "POST") return json({ error: "Method not allowed" }, 405);

    try {
        // ── ระบุตัวตนจาก JWT ──
        const authHeader = req.headers.get("Authorization") ?? "";
        const userClient = createClient(SUPABASE_URL, ANON_KEY, {
            global: { headers: { Authorization: authHeader } },
        });
        const { data: { user }, error: userErr } = await userClient.auth.getUser();
        if (userErr || !user) return json({ error: "ไม่ได้เข้าสู่ระบบ" }, 401);

        // ── ตรวจ input ──
        const { placement, totalPlayers, roomCode, roomId } = await req.json();
        if (
            !Number.isInteger(placement) || !Number.isInteger(totalPlayers) ||
            totalPlayers < 2 || totalPlayers > 4 ||
            placement < 1 || placement > totalPlayers
        ) {
            return json({ error: "ข้อมูลผลการแข่งไม่ถูกต้อง" }, 400);
        }

        const db = createClient(SUPABASE_URL, SERVICE_ROLE);

        // ── ผูกกับห้องจริง (กันฟาร์มรางวัล) ──
        // รับรางวัลได้เฉพาะแมตช์ออนไลน์ที่มีห้องจริง; offline (ไม่มีห้อง) จะไม่ได้รางวัล server
        // หมายเหตุ: ไม่ require status='finished' เพราะ host เซ็ต finished แบบ async — client มัก
        //   ส่งผลก่อนสถานะอัปเดตทัน จะถูกปฏิเสธทั้งที่เล่นจริง. กันฟาร์มอาศัย "ห้องจริง + dedup" พอแล้ว
        const roomCodeStr = typeof roomCode === "string" ? roomCode.trim() : "";
        const roomIdNum = Number.isInteger(roomId) ? roomId
            : (typeof roomId === "string" && /^\d+$/.test(roomId.trim()) ? parseInt(roomId.trim(), 10) : NaN);

        let roomQuery = db.from("rooms").select("id, created_at, room_code");
        if (roomCodeStr.length > 0) roomQuery = roomQuery.eq("room_code", roomCodeStr);
        else if (Number.isInteger(roomIdNum)) roomQuery = roomQuery.eq("id", roomIdNum);
        else return json({ error: "ต้องเป็นแมตช์ออนไลน์ (ไม่พบรหัสห้อง)" }, 400);

        const { data: rooms } = await roomQuery.order("created_at", { ascending: false }).limit(1);
        const room = rooms?.[0];
        if (!room) return json({ error: "ไม่พบห้อง — รับรางวัลได้เฉพาะแมตช์ออนไลน์จริง" }, 400);

        // ── กันฟาร์มรางวัล (แก้จริง): ต้องเป็น "สมาชิกห้องที่ระบบจับคู่ให้จริง" ──
        // matchmaking_queue.player_id ถูกตั้ง = auth.uid() ฝั่ง server เสมอ (payload ถูกมองข้าม)
        //   → client ปลอมไม่ได้. คน create-room เองแล้วยิงผล จะไม่มีแถวในคิว → ถูกปฏิเสธ
        // ผลข้างเคียงที่ตั้งใจ: ห้องส่วนตัว (custom room ที่ไม่ผ่าน matchmaking) = unranked ไม่ได้ MMR/gems
        // จับคู่ได้ทั้ง room_id (bigint) หรือ room_code (text) แล้วแต่คิวเก็บค่าไหนไว้ — robust ทั้งสองแบบ
        const memberFilters = [`room_id.eq.${room.id}`];
        if (room.room_code) memberFilters.push(`room_code.eq.${room.room_code}`);
        const { data: queueRows } = await db.from("matchmaking_queue")
            .select("id").eq("player_id", user.id).or(memberFilters.join(",")).limit(1);
        if (!queueRows || queueRows.length === 0) {
            return json({ error: "รับรางวัลได้เฉพาะแมตช์ที่ระบบจับคู่ให้ (ranked) เท่านั้น" }, 403);
        }

        // ── server คำนวณค่าเอง ──
        const delta = mmrDelta(placement, totalPlayers);
        const reward = gemReward(placement);
        const won = placement === 1;

        // ── dedup: insert match_results ก่อน (UNIQUE(user_id, room_id) เป็นด่านกัน) ──
        const { error: insErr } = await db.from("match_results").insert({
            user_id: user.id, room_id: room.id, placement, total_players: totalPlayers,
            mmr_delta: delta, gems_awarded: reward,
        });

        if (insErr) {
            // 23505 = unique_violation → เคยรับรางวัลห้องนี้ไปแล้ว: คืนค่าปัจจุบัน ไม่ให้ซ้ำ
            if ((insErr as { code?: string }).code === "23505") {
                const { data: cur } = await db.from("player_profiles")
                    .select("mmr, gems").eq("id", user.id).maybeSingle();
                return json({
                    newMmr: cur?.mmr ?? 1000, mmrDelta: 0, gemReward: 0,
                    gems: cur?.gems ?? 0, won, alreadySubmitted: true,
                });
            }
            throw insErr;
        }

        // ── ผ่านด่าน dedup แล้วค่อยเครดิต (optimistic concurrency — กัน race read-modify-write) ──
        //   guard ด้วยค่าเดิม (.eq mmr/gems) → ถ้ามีคำขออื่นแก้ค่าแทรกกลางคัน update จะไม่โดนแถว → อ่านใหม่แล้วลองใหม่
        let newMmr = 1000, newGems = 0;
        let credited = false;
        for (let attempt = 0; attempt < 5 && !credited; attempt++) {
            const { data: profile } = await db.from("player_profiles")
                .select("mmr, gems, wins, losses").eq("id", user.id).maybeSingle();
            const curMmr = profile?.mmr ?? 1000;
            const curGems = profile?.gems ?? 0;
            const curWins = profile?.wins ?? 0;
            const curLosses = profile?.losses ?? 0;
            newMmr = Math.max(0, curMmr + delta);
            newGems = curGems + reward;

            const { data: updated, error: upErr } = await db.from("player_profiles").update({
                mmr: newMmr,
                wins: won ? curWins + 1 : curWins,
                losses: won ? curLosses : curLosses + 1,
                gems: newGems,
                updated_at: new Date().toISOString(),
            }).eq("id", user.id).eq("mmr", curMmr).eq("gems", curGems).select("id");
            if (upErr) throw upErr;
            credited = (updated?.length ?? 0) > 0;
        }
        if (!credited) return json({ error: "ระบบไม่ว่าง กรุณาลองใหม่" }, 409);

        return json({ newMmr, mmrDelta: delta, gemReward: reward, gems: newGems, won });
    } catch (e) {
        console.error(e);
        return json({ error: "เกิดข้อผิดพลาดภายในระบบ" }, 500);
    }
});
