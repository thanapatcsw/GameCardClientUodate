// ============================================================
// Edge Function: purchase-item  (server-authoritative)
// client ส่งแค่ itemId — server ดูราคาจากตาราง shop_items, เช็ค gems, หักเงิน, เพิ่มไอเทม
// => ปลอมราคา / ได้ไอเทมฟรี / gems ติดลบ ไม่ได้
// deploy: supabase functions deploy purchase-item   (verify-jwt = เปิด)
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

        const { itemId } = await req.json();
        if (!itemId || typeof itemId !== "string") return json({ error: "itemId ไม่ถูกต้อง" }, 400);

        const db = createClient(SUPABASE_URL, SERVICE_ROLE);

        // ราคา = อ่านจาก server ไม่เชื่อ client
        const { data: item } = await db
            .from("shop_items").select("price").eq("item_id", itemId).maybeSingle();
        if (!item) return json({ error: "ไม่พบไอเทมนี้" }, 404);

        const { data: profile } = await db
            .from("player_profiles").select("gems, owned_frames").eq("id", user.id).maybeSingle();
        if (!profile) return json({ error: "ไม่พบโปรไฟล์ กรุณาเข้าเกมใหม่" }, 404);

        const owned: string[] = profile.owned_frames ?? ["frame_default"];
        if (owned.includes(itemId)) {
            return json({ error: "มีไอเทมนี้อยู่แล้ว", gems: profile.gems, ownedFrames: owned }, 409);
        }
        if ((profile.gems ?? 0) < item.price) {
            return json({ error: "Gems ไม่พอ" }, 402);
        }

        const newGems = profile.gems - item.price;
        const newOwned = [...owned, itemId];
        // สวมกรอบที่เพิ่งซื้อให้เลยในคำสั่งเดียว (เกม auto-equip หลังซื้ออยู่แล้ว)
        // กัน race กับ equip-cosmetic ที่ client ยิงตามมา
        const { error: upErr } = await db.from("player_profiles").update({
            gems: newGems,
            owned_frames: newOwned,
            equipped_frame: itemId,
            updated_at: new Date().toISOString(),
        }).eq("id", user.id);
        if (upErr) throw upErr;

        return json({ gems: newGems, ownedFrames: newOwned, equippedFrame: itemId });
    } catch (e) {
        console.error(e);
        return json({ error: "เกิดข้อผิดพลาดภายในระบบ" }, 500);
    }
});
