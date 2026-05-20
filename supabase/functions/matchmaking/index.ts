import { createClient } from "https://esm.sh/@supabase/supabase-js@2.49.8";

const corsHeaders = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
  "Content-Type": "application/json",
};

type MatchmakingAction = "find" | "cancel";

type MatchmakingRequest = {
  action?: MatchmakingAction;
  playerId?: string;
  targetPlayerCount?: number;
  searchRequestId?: string;
  sceneName?: string;
  displayName?: string;
  staleTimeoutSeconds?: number;
};

function jsonResponse(body: Record<string, unknown>, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: corsHeaders,
  });
}

function clampStaleTimeoutSeconds(value: number | undefined): number {
  if (!Number.isFinite(value)) {
    return 300;
  }

  return Math.max(30, Math.min(3600, Math.floor(value as number)));
}

function normalizeRpcResult<T>(data: T | T[] | null): T | null {
  if (Array.isArray(data)) {
    return data.length > 0 ? data[0] : null;
  }

  return data;
}

Deno.serve(async (request) => {
  if (request.method === "OPTIONS") {
    return new Response("ok", { headers: corsHeaders });
  }

  if (request.method !== "POST") {
    return jsonResponse(
      { status: "error", message: "Method not allowed." },
      405,
    );
  }

  const supabaseUrl = Deno.env.get("SUPABASE_URL");
  const serviceRoleKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY");

  if (!supabaseUrl || !serviceRoleKey) {
    console.error("[matchmaking] Missing required environment variables.");
    return jsonResponse(
      { status: "error", message: "Server configuration is incomplete." },
      500,
    );
  }

  let payload: MatchmakingRequest;
  try {
    payload = await request.json();
  } catch (error) {
    console.error("[matchmaking] Invalid JSON body.", error);
    return jsonResponse(
      { status: "error", message: "Invalid JSON body." },
      400,
    );
  }

  const action: MatchmakingAction = payload.action ?? "find";
  const playerId = typeof payload.playerId === "string" ? payload.playerId.trim() : "";

  if (!["find", "cancel"].includes(action)) {
    return jsonResponse(
      { status: "error", message: "Unsupported matchmaking action." },
      400,
    );
  }

  if (!playerId) {
    return jsonResponse(
      { status: "error", message: "playerId is required." },
      400,
    );
  }

  const supabase = createClient(supabaseUrl, serviceRoleKey, {
    auth: {
      autoRefreshToken: false,
      persistSession: false,
    },
  });

  try {
    if (action === "cancel") {
      const { data, error } = await supabase.rpc("cancel_matchmaking_search", {
        p_player_id: playerId,
        p_search_request_id: typeof payload.searchRequestId === "string" && payload.searchRequestId.trim().length > 0
          ? payload.searchRequestId.trim()
          : null,
      });

      if (error) {
        console.error("[matchmaking] cancel rpc failed", error);
        return jsonResponse(
          { status: "error", message: error.message },
          500,
        );
      }

      return jsonResponse(
        normalizeRpcResult<Record<string, unknown>>(data) ?? {
          status: "cancelled",
          playerId,
        },
      );
    }

    const targetPlayerCount = Number(payload.targetPlayerCount);
    if (![2, 3, 4].includes(targetPlayerCount)) {
      return jsonResponse(
        {
          status: "error",
          message: "targetPlayerCount must be 2, 3, or 4.",
        },
        400,
      );
    }

    const searchRequestId = typeof payload.searchRequestId === "string"
      ? payload.searchRequestId.trim()
      : "";

    if (!searchRequestId) {
      return jsonResponse(
        { status: "error", message: "searchRequestId is required." },
        400,
      );
    }

    const { data, error } = await supabase.rpc("claim_matchmaking_room", {
      p_player_id: playerId,
      p_target_player_count: targetPlayerCount,
      p_search_request_id: searchRequestId,
      p_scene_name: typeof payload.sceneName === "string" && payload.sceneName.trim().length > 0
        ? payload.sceneName.trim()
        : "SampleScene",
      p_display_name: typeof payload.displayName === "string" && payload.displayName.trim().length > 0
        ? payload.displayName.trim()
        : playerId,
      p_stale_timeout_seconds: clampStaleTimeoutSeconds(payload.staleTimeoutSeconds),
    });

    if (error) {
      console.error("[matchmaking] find rpc failed", error);
      return jsonResponse(
        { status: "error", message: error.message },
        500,
      );
    }

    const result = normalizeRpcResult<Record<string, unknown>>(data);
    if (!result) {
      console.error("[matchmaking] Empty RPC result.");
      return jsonResponse(
        { status: "error", message: "Empty matchmaking response." },
        500,
      );
    }

    return jsonResponse(result);
  } catch (error) {
    console.error("[matchmaking] Unhandled exception", error);
    return jsonResponse(
      { status: "error", message: "Unhandled matchmaking error." },
      500,
    );
  }
});
