using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using UnityEngine;

/// <summary>
/// Service จัดการ CRUD ข้อมูล Player กับ Supabase
/// ใช้ LocalCache (PlayerPrefs) เป็น fallback ถ้าออฟไลน์
/// </summary>
public static class PlayerDataService
{
    public static PlayerProfile LocalProfile { get; private set; }

    private static readonly HttpClient _http = new HttpClient();

    // ผลลัพธ์ที่ server คำนวณและคืนกลับมา (server-authoritative)
    [System.Serializable]
    public class MatchResult
    {
        public int newMmr;
        public int mmrDelta;
        public int gemReward;
        public int gems;
        public bool won;
    }

    /// <summary>
    /// ส่งผลการแข่ง (อันดับ + จำนวนผู้เล่น) ให้ Edge Function คำนวณ MMR/รางวัลเอง
    /// client ไม่ได้เป็นคนกำหนดค่า MMR/gems อีกต่อไป (กันโกง)
    /// คืน null ถ้าล้มเหลว (เช่น ออฟไลน์) — ให้ caller fallback เป็น local ได้
    /// </summary>
    public static async Task<MatchResult> SubmitMatchResultAsync(int placement, int totalPlayers)
    {
        var sb = SupabaseManager.Instance?.Client;
        string token = sb?.Auth?.CurrentSession?.AccessToken;
        if (sb == null || string.IsNullOrEmpty(token))
        {
            Debug.LogWarning("[PlayerData] ไม่มี session — ข้ามการบันทึกผลฝั่ง server");
            return null;
        }

        try
        {
            string url = $"{SupabaseConfig.Url}/functions/v1/submit-match-result";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.TryAddWithoutValidation("apikey", SupabaseConfig.AnonKey);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            // ส่งรหัสห้อง (online) เพื่อผูกผลกับห้องจริง — server กันฟาร์มรางวัลด้วย room_code/room_id
            // offline: ค่าว่าง → server ปฏิเสธ (ไม่ได้ MMR/gems ลง DB ตามดีไซน์)
            string roomCode = PlayerPrefs.GetString("MatchmakingRoomCode", "");
            string roomId = PlayerPrefs.GetString("MatchmakingRoomId", "");
            req.Content = new StringContent(
                $"{{\"placement\":{placement},\"totalPlayers\":{totalPlayers},\"roomCode\":\"{roomCode}\",\"roomId\":\"{roomId}\"}}",
                Encoding.UTF8, "application/json");

            var resp = await _http.SendAsync(req);
            string body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                Debug.LogError($"[PlayerData] submit-match-result ล้มเหลว ({(int)resp.StatusCode}): {body}");
                return null;
            }

            var result = JsonUtility.FromJson<MatchResult>(body);

            // อัปเดต local cache จากค่า "ที่ server ยืนยัน" (source of truth)
            PlayerPrefs.SetInt("MMR", result.newMmr);
            PlayerPrefs.SetInt("LastMmrDelta", result.mmrDelta);
            PlayerPrefs.SetInt("TotalGems", result.gems);
            PlayerPrefs.Save();

            if (LocalProfile != null)
            {
                LocalProfile.Mmr = result.newMmr;
                LocalProfile.Gems = result.gems;
                if (result.won) LocalProfile.Wins++;
                else LocalProfile.Losses++;
            }

            if (CurrencyManager.Instance != null)
                CurrencyManager.Instance.RefreshFromLocalCache();

            GameLog.Log($"[PlayerData] ผลแข่ง (server): MMR {result.newMmr} ({result.mmrDelta:+#;-#;0}) | +{result.gemReward} gems");
            return result;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[PlayerData] SubmitMatchResult error: {e.Message}");
            return null;
        }
    }

    public static async Task LoadProfileAsync()
    {
        var sb = SupabaseManager.Instance?.Client;
        if (sb == null)
        {
            Debug.LogWarning("[PlayerData] Supabase not ready, using local cache.");
            LoadFromLocalCache();
            return;
        }

        try
        {
            // พยายามโหลด Profile ของ User ปัจจุบัน
            // ต้อง filter id ตัวเองชัดเจน เพราะ RLS select เปิดให้เห็นทุกแถว (ใช้ทำ leaderboard)
            // ถ้าไม่ filter .Single() จะเจอหลายแถวแล้ว error
            var result = await sb.From<PlayerProfile>()
                .Filter("id", Postgrest.Constants.Operator.Equals, sb.Auth.CurrentUser.Id)
                .Single();
            if (result != null)
            {
                LocalProfile = result;
                SyncToLocalCache(result);
                
                // แจ้งเตือน UI ให้รีเฟรชค่า Gems
                if (CurrencyManager.Instance != null)
                {
                    CurrencyManager.Instance.RefreshFromLocalCache();
                }

                GameLog.Log($"[PlayerData] Profile loaded: {result.Username} | MMR: {result.Mmr} | Gems: {result.Gems}");
            }
        }
        catch (System.Exception e)
        {
            GameLog.Log($"[PlayerData] No profile found, creating via server: {e.Message}");

            // สร้างโปรไฟล์ฝั่ง server (server กำหนดค่าเริ่มต้นเอง) แล้วโหลดกลับมาตามปกติ
            bool created = await InitProfileAsync();
            if (created)
            {
                try
                {
                    var result = await sb.From<PlayerProfile>()
                        .Filter("id", Postgrest.Constants.Operator.Equals, sb.Auth.CurrentUser.Id)
                        .Single();
                    if (result != null)
                    {
                        LocalProfile = result;
                        SyncToLocalCache(result);
                        GameLog.Log("[PlayerData] New profile created on server.");
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[PlayerData] Created but failed to reload profile: {ex.Message}");
                    LoadFromLocalCache();
                }
            }
            else
            {
                Debug.LogError("[PlayerData] Failed to create profile on server.");
                LoadFromLocalCache();
            }

            if (CurrencyManager.Instance != null) CurrencyManager.Instance.RefreshFromLocalCache();
        }
    }

    /// <summary>
    /// ดึงข้อมูลกรอบและรูปประจำตัวของผู้เล่นคนอื่นจาก DB โดยตรง (ป้องกันการโกง/ข้อมูลไม่ตรงกันผ่านเครือข่าย)
    /// </summary>
    public static async Task<(string frame, int character)> GetPlayerCosmeticsAsync(string uid)
    {
        var sb = SupabaseManager.Instance?.Client;
        if (sb == null || string.IsNullOrEmpty(uid)) return ("frame_default", 0);
        
        try
        {
            var result = await sb.From<PlayerProfile>()
                .Select("equipped_frame, selected_character")
                .Filter("id", Postgrest.Constants.Operator.Equals, uid)
                .Single();
            
            if (result != null)
            {
                return (result.EquippedFrame, result.SelectedCharacter);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PlayerData] GetPlayerCosmetics failed for {uid}: {e.Message}");
        }
        return ("frame_default", 0);
    }

    // local-only: อัปเดต cache สำหรับ UI เท่านั้น — การเขียน gems ลง DB ทำผ่าน
    // server function (purchase-item / grant-quiz-reward / submit-match-result) เท่านั้น
    public static Task SaveCurrencyAsync(int gems)
    {
        PlayerPrefs.SetInt("TotalGems", gems);
        PlayerPrefs.Save();
        if (LocalProfile != null) LocalProfile.Gems = gems;
        return Task.CompletedTask;
    }

    // local-only: การเขียน inventory ลง DB ทำผ่าน purchase-item / equip-cosmetic เท่านั้น
    public static Task SaveInventoryAsync(List<string> ownedFrames, string equippedFrame)
    {
        PlayerPrefs.SetString("OwnedItems", string.Join(",", ownedFrames));
        PlayerPrefs.SetString("EquippedFrame", equippedFrame);
        PlayerPrefs.Save();
        if (LocalProfile != null)
        {
            LocalProfile.OwnedFrames = ownedFrames;
            LocalProfile.EquippedFrame = equippedFrame;
        }
        return Task.CompletedTask;
    }

    // เลือกตัวละคร — local + เขียน DB ผ่าน server (equip-cosmetic)
    public static async Task SaveCharacterAsync(int characterIndex)
    {
        PlayerPrefs.SetInt("SelectedCharacter", characterIndex);
        PlayerPrefs.Save();
        if (LocalProfile != null) LocalProfile.SelectedCharacter = characterIndex;

        await CallAuthedFnAsync("equip-cosmetic", $"{{\"selectedCharacter\":{characterIndex}}}");
    }

    // ───────── server-authoritative helpers (เรียก Edge Function ด้วย JWT ผู้ใช้) ─────────

    private static async Task<(bool ok, int status, string body)> CallAuthedFnAsync(string fn, string jsonBody)
    {
        var sb = SupabaseManager.Instance?.Client;
        string token = sb?.Auth?.CurrentSession?.AccessToken;
        if (sb == null || string.IsNullOrEmpty(token))
        {
            Debug.LogWarning($"[PlayerData] ไม่มี session — ข้าม {fn}");
            return (false, 0, null);
        }
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{SupabaseConfig.Url}/functions/v1/{fn}");
            req.Headers.TryAddWithoutValidation("apikey", SupabaseConfig.AnonKey);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            req.Content = new StringContent(jsonBody ?? "{}", Encoding.UTF8, "application/json");
            var resp = await _http.SendAsync(req);
            string body = await resp.Content.ReadAsStringAsync();
            return (resp.IsSuccessStatusCode, (int)resp.StatusCode, body);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[PlayerData] call {fn} error: {e.Message}");
            return (false, 0, null);
        }
    }

    [System.Serializable] private class PurchaseResp { public int gems; public string[] ownedFrames; public string equippedFrame; public string error; }
    [System.Serializable] private class QuizResp { public int gems; public int reward; public string error; }

    /// <summary>สร้างโปรไฟล์เริ่มต้นฝั่ง server (ถ้ายังไม่มี) — คืน true ถ้าสำเร็จ</summary>
    public static async Task<bool> InitProfileAsync()
    {
        var (ok, _, _) = await CallAuthedFnAsync("init-profile", "{}");
        return ok;
    }

    /// <summary>ซื้อไอเทม — server หักเงิน/เพิ่มไอเทมเอง แล้ว reconcile local จากค่าที่ server ยืนยัน</summary>
    public static async Task<(bool ok, string error)> PurchaseItemAsync(string itemId)
    {
        var (ok, _, body) = await CallAuthedFnAsync("purchase-item", $"{{\"itemId\":\"{itemId}\"}}");
        if (!ok)
        {
            string err = "ซื้อไม่สำเร็จ";
            if (!string.IsNullOrEmpty(body))
            {
                var r = JsonUtility.FromJson<PurchaseResp>(body);
                if (r != null && !string.IsNullOrEmpty(r.error)) err = r.error;
            }
            return (false, err);
        }
        var resp = JsonUtility.FromJson<PurchaseResp>(body);
        PlayerPrefs.SetInt("TotalGems", resp.gems);
        if (resp.ownedFrames != null) PlayerPrefs.SetString("OwnedItems", string.Join(",", resp.ownedFrames));
        if (!string.IsNullOrEmpty(resp.equippedFrame)) PlayerPrefs.SetString("EquippedFrame", resp.equippedFrame);
        PlayerPrefs.Save();
        if (LocalProfile != null)
        {
            LocalProfile.Gems = resp.gems;
            if (resp.ownedFrames != null) LocalProfile.OwnedFrames = new List<string>(resp.ownedFrames);
            if (!string.IsNullOrEmpty(resp.equippedFrame)) LocalProfile.EquippedFrame = resp.equippedFrame;
        }
        CurrencyManager.Instance?.RefreshFromLocalCache();
        return (true, "");
    }

    /// <summary>รับรางวัลควิซรายวัน — server กำหนดจำนวน + กันรับซ้ำ/วัน + บันทึก question_id เพื่อกันถามซ้ำ</summary>
    public static async Task<bool> GrantQuizRewardAsync(string questionId = null)
    {
        // ส่ง question_id ไปให้ server บันทึกใน daily_quiz_claims ด้วย
        string payload = string.IsNullOrEmpty(questionId)
            ? "{}"
            : $"{{\"question_id\":\"{questionId}\"}}";

        var (ok, status, body) = await CallAuthedFnAsync("grant-quiz-reward", payload);
        if (!ok)
        {
            Debug.LogWarning($"[PlayerData] quiz reward not granted ({status}): {body}");
            return false;
        }
        var resp = JsonUtility.FromJson<QuizResp>(body);
        PlayerPrefs.SetInt("TotalGems", resp.gems);
        PlayerPrefs.Save();
        if (LocalProfile != null) LocalProfile.Gems = resp.gems;
        CurrencyManager.Instance?.RefreshFromLocalCache();
        return true;
    }

    [System.Serializable]
    public class SubmitQuizResponseRow {
        public bool success;
        public string message;
        public int reward_gems;
    }
    [System.Serializable] private class SubmitQuizWrapper { public SubmitQuizResponseRow[] rows; }

    /// <summary>บันทึกคำตอบลงฐานข้อมูล (ทำทั้งตอบถูกและผิด เพื่อป้องกันการปั๊มตอบซ้ำ)</summary>
    public static async Task<(bool success, string message, int gems)> SubmitDailyQuizAnswerAsync(bool isCorrect, int rewardGems)
    {
        var sb = SupabaseManager.Instance?.Client;
        if (sb?.Auth?.CurrentUser == null) return (false, "Not logged in", 0);

        try
        {
            string userId = sb.Auth.CurrentUser.Id;
            string url = $"{SupabaseConfig.Url}/rest/v1/rpc/submit_daily_quiz_answer";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.TryAddWithoutValidation("apikey", SupabaseConfig.AnonKey);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {sb.Auth.CurrentSession.AccessToken}");
            
            string payload = $"{{\"p_user_id\":\"{userId}\", \"p_is_correct\":{isCorrect.ToString().ToLower()}, \"p_reward_gems\":{rewardGems}}}";
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            var resp = await _http.SendAsync(req);
            string body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                Debug.LogWarning($"[PlayerData] submit_daily_quiz_answer failed: {body}");
                return (false, body, 0);
            }

            string wrapped = $"{{\"rows\":{body}}}";
            var data = JsonUtility.FromJson<SubmitQuizWrapper>(wrapped);
            if (data != null && data.rows != null && data.rows.Length > 0)
            {
                var row = data.rows[0];
                return (row.success, row.message, row.reward_gems);
            }
            return (false, "Invalid format", 0);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PlayerData] SubmitDailyQuizAnswer error: {e.Message}");
            return (false, e.Message, 0);
        }
    }

    // แถวคำถามรายวันจาก DB (RPC get_daily_quiz_question) — เนื้อคำถาม + เฉลยมาจาก server
    [System.Serializable]
    public class DailyQuizQuestionRow
    {
        public string external_id;
        public string category;
        public string difficulty;
        public string question;
        public string[] choices;
        public int    correct_index;
        public bool   already_answered;
    }
    [System.Serializable] private class DailyQuizQuestionWrapper { public DailyQuizQuestionRow[] rows; }

    /// <summary>
    /// ดึงคำถามประจำวันของผู้เล่น (เนื้อคำถาม + choices + เฉลย) จาก DB ผ่าน RPC get_daily_quiz_question
    /// สำคัญ: RPC ตัวนี้ "INSERT แถว daily_quiz_claims ของวันนี้" ให้ด้วย — ถ้าไม่มีแถว claim นี้
    /// submit_daily_quiz_answer จะตอบ 'ยังไม่ได้รับคำถามวันนี้' → ตอบถูกก็ถูกนับเป็นผิดทุกครั้ง
    /// คืน null เมื่อออฟไลน์/ยังไม่ล็อกอิน/DB ว่าง → ให้ client fallback ไป JSON local
    /// </summary>
    public static async Task<DailyQuizQuestionRow> FetchDailyQuizQuestionAsync()
    {
        var sb = SupabaseManager.Instance?.Client;
        if (sb?.Auth?.CurrentUser == null) return null;

        try
        {
            string userId = sb.Auth.CurrentUser.Id;
            string url = $"{SupabaseConfig.Url}/rest/v1/rpc/get_daily_quiz_question";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.TryAddWithoutValidation("apikey", SupabaseConfig.AnonKey);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {sb.Auth.CurrentSession.AccessToken}");
            req.Content = new StringContent($"{{\"p_user_id\":\"{userId}\"}}", Encoding.UTF8, "application/json");

            var resp = await _http.SendAsync(req);
            string body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                Debug.LogWarning($"[PlayerData] get_daily_quiz_question failed: {body}");
                return null;
            }

            // RPC คืน JSON array 1 แถว — wrap เพื่อให้ JsonUtility parse ได้ (choices jsonb → string[])
            var wrapper = JsonUtility.FromJson<DailyQuizQuestionWrapper>("{\"rows\":" + body + "}");
            if (wrapper?.rows != null && wrapper.rows.Length > 0)
                return wrapper.rows[0];

            // DB ว่าง/ไม่มี patch active → null → client fallback ไป JSON local
            return null;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PlayerData] FetchDailyQuizQuestion error: {e.Message}");
            return null;
        }
    }

    [System.Serializable]
    public class DailyQuizStatusRow {
        public bool has_claimed;
        public bool already_answered;
        public int reward_gems;
    }
    [System.Serializable] private class DailyQuizStatusWrapper { public DailyQuizStatusRow[] rows; }

    /// <summary>ตรวจสอบผ่าน Supabase RPC ว่าวันนี้เคยตอบและรับรางวัลไปแล้วหรือยัง</summary>
    public static async Task<bool> HasClaimedDailyQuizTodayAsync()
    {
        var sb = SupabaseManager.Instance?.Client;
        if (sb?.Auth?.CurrentUser == null) return false;

        try
        {
            string userId = sb.Auth.CurrentUser.Id;
            string url = $"{SupabaseConfig.Url}/rest/v1/rpc/has_claimed_daily_quiz_today";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.TryAddWithoutValidation("apikey", SupabaseConfig.AnonKey);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {sb.Auth.CurrentSession.AccessToken}");
            req.Content = new StringContent($"{{\"p_user_id\":\"{userId}\"}}", Encoding.UTF8, "application/json");

            var resp = await _http.SendAsync(req);
            string body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                Debug.LogWarning($"[PlayerData] has_claimed_daily_quiz_today failed: {body}");
                return false;
            }
            string wrapped = $"{{\"rows\":{body}}}";
            var data = JsonUtility.FromJson<DailyQuizStatusWrapper>(wrapped);
            if (data != null && data.rows != null && data.rows.Length > 0)
            {
                return data.rows[0].already_answered;
            }
            return false;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PlayerData] HasClaimedDailyQuizToday error: {e.Message}");
            return false;
        }
    }

    // ───────── Tutorial completion (ต่อบัญชี) ─────────

    /// <summary>เคยเล่นฝึกสอนจบครบทุก step แล้วหรือยัง (ใช้ตัดสินว่าจะเด้งฝึกสอนหลังล็อกอินไหม)</summary>
    public static bool IsTutorialCompleted()
    {
        if (LocalProfile != null) return LocalProfile.TutorialCompleted;
        return PlayerPrefs.GetInt("TutorialCompleted", 0) == 1;
    }

    /// <summary>
    /// บันทึกว่า "เล่นฝึกสอนจบจริง" ต่อบัญชี — เขียน local ทันที (กันเด้งซ้ำแม้ออฟไลน์)
    /// แล้วยิง RPC mark_tutorial_completed ให้ server เซ็ต flag ตาม auth.uid() (นับเฉพาะเล่นจบ ไม่ใช่กด Skip)
    /// </summary>
    public static async Task MarkTutorialCompletedAsync()
    {
        // local fast-path: เซ็ตก่อน await เสมอ เพื่อให้เมนูอ่านได้ทันแม้ scene เปลี่ยน/ออฟไลน์
        PlayerPrefs.SetInt("TutorialCompleted", 1);
        PlayerPrefs.Save();
        if (LocalProfile != null) LocalProfile.TutorialCompleted = true;

        var sb = SupabaseManager.Instance?.Client;
        if (sb?.Auth?.CurrentSession == null)
        {
            Debug.LogWarning("[PlayerData] ไม่มี session — เก็บสถานะฝึกสอนไว้ local ก่อน (จะไม่ sync ขึ้น server)");
            return;
        }

        try
        {
            string url = $"{SupabaseConfig.Url}/rest/v1/rpc/mark_tutorial_completed";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.TryAddWithoutValidation("apikey", SupabaseConfig.AnonKey);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {sb.Auth.CurrentSession.AccessToken}");
            req.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                Debug.LogWarning($"[PlayerData] mark_tutorial_completed failed ({(int)resp.StatusCode}): {await resp.Content.ReadAsStringAsync()}");
            else
                GameLog.Log("[PlayerData] บันทึกสถานะ 'ผ่านฝึกสอน' ขึ้น server แล้ว");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PlayerData] MarkTutorialCompleted error: {e.Message}");
        }
    }

    /// <summary>สวมกรอบ — server ตรวจ ownership ก่อนสวม</summary>
    public static async Task EquipFrameAsync(string itemId)
    {
        await CallAuthedFnAsync("equip-cosmetic", $"{{\"equippedFrame\":\"{itemId}\"}}");
    }

    /// <summary>
    /// Upsert ห้องเกมผ่าน Edge Function (server-authoritative)
    /// แทนการ insert ตรงจาก client ที่ติด RLS rooms_public_read
    /// • ครั้งแรก (ห้องยังไม่มี): ต้องส่งครบ — sessionName, playerCount, status
    /// • อัปเดตเฉพาะบาง field: ส่งเฉพาะ field ที่เปลี่ยน (ตัวอื่นเป็น null)
    /// </summary>
    public static async Task<bool> CreateRoomAsync(
        string roomCode,
        string sessionName = null,
        int? playerCount = null,
        string status = null)
    {
        if (string.IsNullOrEmpty(roomCode))
        {
            Debug.LogWarning("[PlayerData] CreateRoom — roomCode is empty");
            return false;
        }

        // ประกอบ JSON เฉพาะ field ที่มีค่า (ฝั่ง server จะ partial-update)
        var parts = new List<string> { $"\"roomCode\":\"{roomCode}\"" };
        if (sessionName != null) parts.Add($"\"sessionName\":\"{sessionName}\"");
        if (playerCount.HasValue) parts.Add($"\"playerCount\":{Mathf.Clamp(playerCount.Value, 1, 4)}");
        if (status != null) parts.Add($"\"status\":\"{status}\"");
        string body = "{" + string.Join(",", parts) + "}";

        var (ok, httpStatus, respBody) = await CallAuthedFnAsync("create-room", body);
        if (!ok)
        {
            Debug.LogWarning($"[PlayerData] create-room failed ({httpStatus}): {respBody}");
            return false;
        }
        GameLog.Log($"<color=green>✅ [PlayerData] upsert ห้อง [{roomCode}] สำเร็จ</color>");
        return true;
    }

    public static async Task<List<PlayerProfile>> GetLeaderboardAsync(int limit = 50)
    {
        var sb = SupabaseManager.Instance?.Client;
        if (sb == null) return new List<PlayerProfile>();
        try
        {
            var result = await sb.From<PlayerProfile>()
                .Select("username, mmr, wins, losses")
                .Order("mmr", Postgrest.Constants.Ordering.Descending)
                .Limit(limit)
                .Get();
            return result.Models ?? new List<PlayerProfile>();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PlayerData] Leaderboard failed: {e.Message}");
            return new List<PlayerProfile>();
        }
    }

    private static void SyncToLocalCache(PlayerProfile p)
    {
        if (p == null) return;
        
        PlayerPrefs.SetInt("TotalGems", p.Gems);
        PlayerPrefs.SetInt("MMR", p.Mmr);
        PlayerPrefs.SetString("Username", p.Username);
        
        if (p.OwnedFrames != null && p.OwnedFrames.Count > 0)
        {
            PlayerPrefs.SetString("OwnedItems", string.Join(",", p.OwnedFrames));
        }
        else
        {
            PlayerPrefs.SetString("OwnedItems", "frame_default");
        }
        
        PlayerPrefs.SetString("EquippedFrame", string.IsNullOrEmpty(p.EquippedFrame) ? "frame_default" : p.EquippedFrame);
        PlayerPrefs.SetInt("SelectedCharacter", p.SelectedCharacter);
        PlayerPrefs.SetInt("TutorialCompleted", p.TutorialCompleted ? 1 : 0);
        PlayerPrefs.Save();
    }

    private static void LoadFromLocalCache()
    {
        LocalProfile = new PlayerProfile
        {
            Gems = PlayerPrefs.GetInt("TotalGems", 0),
            Mmr = PlayerPrefs.GetInt("MMR", 1000),
            Username = PlayerPrefs.GetString("Username", "Player"),
            EquippedFrame = PlayerPrefs.GetString("EquippedFrame", "frame_default"),
            SelectedCharacter = PlayerPrefs.GetInt("SelectedCharacter", 0),
            TutorialCompleted = PlayerPrefs.GetInt("TutorialCompleted", 0) == 1,
        };
        string owned = PlayerPrefs.GetString("OwnedItems", "");
        LocalProfile.OwnedFrames = string.IsNullOrEmpty(owned)
            ? new List<string> { "frame_default" }
            : new List<string>(owned.Split(','));
    }
}
