// ============================================================
// Edge Function: create-room  (server-authoritative)
// บันทึกห้องเกมแบบ manual ลงตาราง rooms ด้วย service_role
// แทน CreateRoom เดิมที่ client insert ตรง (ติด RLS rooms_public_read)
// deploy: supabase functions deploy create-room   (verify-jwt = เปิด)
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

interface CreateRoomBody {
    roomCode?: string;
    sessionName?: string;
    playerCount?: number;
    status?: string;            // "waiting" | "playing" | "finished"
}

const ALLOWED_STATUS = ["waiting", "playing", "finished"];

Deno.serve(async (req) => {
    if (req.method === "OPTIONS") return new Response("ok", { headers: CORS });
    if (req.method !== "POST") return json({ error: "Method not allowed" }, 405);

    try {
        // ── 1. ตรวจสอบสิทธิ์ผู้ใช้จาก JWT ──
        const authHeader = req.headers.get("Authorization") ?? "";
        const userClient = createClient(SUPABASE_URL, ANON_KEY, {
            global: { headers: { Authorization: authHeader } },
        });
        const { data: { user } } = await userClient.auth.getUser();
        if (!user) return json({ error: "ไม่ได้เข้าสู่ระบบ" }, 401);

        // ── 2. validate input ──
        const body = (await req.json().catch(() => ({}))) as CreateRoomBody;
        const roomCode = (body.roomCode ?? "").trim();
        const sessionName = body.sessionName != null ? String(body.sessionName).trim() : null;
        const playerCount = body.playerCount != null
            ? Math.max(1, Math.min(4, Number(body.playerCount)))
            : null;
        const status = body.status != null ? String(body.status).trim() : null;

        if (!roomCode) return json({ error: "roomCode is required" }, 400);
        if (roomCode.length > 64) return json({ error: "roomCode too long" }, 400);
        if (status !== null && !ALLOWED_STATUS.includes(status)) {
            return json({ error: `status must be one of ${ALLOWED_STATUS.join(", ")}` }, 400);
        }

        // ── 3. ดึง username ของ host จาก player_profiles (fallback = email/Player) ──
        const db = createClient(SUPABASE_URL, SERVICE_ROLE);
        const { data: profile } = await db
            .from("player_profiles").select("username").eq("id", user.id).maybeSingle();
        const hostName = profile?.username ?? user.email ?? "Player";

        // ── 4. upsert ด้วย service_role (bypass RLS) ──
        //     ถ้ามีอยู่แล้ว → update player_count (ใช้ตอนมีผู้เล่น join/leave ห้อง)
        //     ถ้ายังไม่มี → insert ใหม่
        //     หมายเหตุ: ต้องเรียก db.from("rooms") ใหม่ทุกครั้ง — postgrest-js mutate state
        //              ภายใน query builder, การ reuse instance ทำให้ request ผิดรูป
        const { data: existing, error: selErr } = await db
            .from("rooms").select("*").eq("room_code", roomCode).maybeSingle();
        if (selErr) throw selErr;

        if (existing) {
            // partial update — เฉพาะ field ที่ส่งมา
            const updates: Record<string, unknown> = {};
            if (playerCount !== null) updates.player_count = playerCount;
            if (status !== null) updates.status = status;
            if (Object.keys(updates).length === 0) return json(existing); // ไม่มีอะไรอัปเดต

            const { data: updated, error: updErr } = await db
                .from("rooms")
                .update(updates)
                .eq("room_code", roomCode)
                .select().single();
            if (updErr) throw updErr;
            return json(updated);
        }

        const { data: created, error: insErr } = await db
            .from("rooms")
            .insert({
                room_code: roomCode,
                session_name: sessionName ?? roomCode,
                host_name: hostName,
                player_count: playerCount ?? 1,
                status: status ?? "waiting",
            })
            .select().single();

        if (insErr) throw insErr;
        return json(created);
    } catch (e) {
        // log ข้อความ error จริงไปยัง Dashboard logs เพื่อ debug
        const msg = e instanceof Error ? e.message : String(e);
        console.error("[create-room] error:", msg, e);
        return json({ error: "เกิดข้อผิดพลาดภายในระบบ", detail: msg }, 500);
    }
});
