// ============================================================
// Edge Function: equip-cosmetic  (server-authoritative)
// สวมกรอบ/เลือกตัวละคร — server เช็คว่า "เป็นเจ้าของกรอบจริง" ก่อนสวม
// (ความเสี่ยงต่ำ แต่ต้องผ่าน server เพราะตาราง player_profiles จะถูกล็อกการเขียน)
// deploy: supabase functions deploy equip-cosmetic   (verify-jwt = เปิด)
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

        const { equippedFrame, selectedCharacter } = await req.json();

        const db = createClient(SUPABASE_URL, SERVICE_ROLE);
        const { data: profile } = await db
            .from("player_profiles").select("owned_frames, equipped_frame, selected_character")
            .eq("id", user.id).maybeSingle();
        if (!profile) return json({ error: "ไม่พบโปรไฟล์" }, 404);

        const update: Record<string, unknown> = { updated_at: new Date().toISOString() };

        if (typeof equippedFrame === "string") {
            const owned: string[] = profile.owned_frames ?? ["frame_default"];
            if (!owned.includes(equippedFrame)) {
                return json({ error: "ยังไม่ได้เป็นเจ้าของกรอบนี้" }, 403);
            }
            update.equipped_frame = equippedFrame;
        }
        if (Number.isInteger(selectedCharacter)) {
            update.selected_character = selectedCharacter;
        }

        const { error: upErr } = await db.from("player_profiles")
            .update(update).eq("id", user.id);
        if (upErr) throw upErr;

        return json({
            equippedFrame: update.equipped_frame ?? profile.equipped_frame,
            selectedCharacter: update.selected_character ?? profile.selected_character,
        });
    } catch (e) {
        console.error(e);
        return json({ error: "เกิดข้อผิดพลาดภายในระบบ" }, 500);
    }
});
