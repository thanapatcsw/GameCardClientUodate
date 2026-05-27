// ============================================================
// Edge Function: init-profile  (server-authoritative)
// สร้างโปรไฟล์เริ่มต้นให้ผู้เล่นถ้ายังไม่มี (gems=0, mmr=1000, frame_default)
// แทนการ Upsert จาก client เดิม — กันการตั้งค่าเริ่มต้นเกินจริง
// deploy: supabase functions deploy init-profile   (verify-jwt = เปิด)
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

        const { data: existing } = await db
            .from("player_profiles").select("*").eq("id", user.id).maybeSingle();

        if (existing) return json(existing); // มีแล้ว คืนของเดิม

        const username = (user.user_metadata?.username as string) ?? user.email ?? "Player";
        const profile = {
            id: user.id,
            username,
            gems: 0,
            mmr: 1000,
            wins: 0,
            losses: 0,
            owned_frames: ["frame_default"],
            equipped_frame: "frame_default",
            selected_character: 0,
            updated_at: new Date().toISOString(),
        };
        const { data: created, error } = await db
            .from("player_profiles").insert(profile).select().single();
        if (error) throw error;

        return json(created);
    } catch (e) {
        console.error(e);
        return json({ error: "เกิดข้อผิดพลาดภายในระบบ" }, 500);
    }
});
