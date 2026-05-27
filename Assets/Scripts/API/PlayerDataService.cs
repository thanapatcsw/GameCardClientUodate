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
            req.Content = new StringContent(
                $"{{\"placement\":{placement},\"totalPlayers\":{totalPlayers}}}",
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
                
                // แจ้งเตือน UI ให้รีเฟรชค่า Gems/Coins
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
                    var result = await sb.From<PlayerProfile>().Select("*").Single();
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

    /// <summary>รับรางวัลควิซรายวัน — server กำหนดจำนวน + กันรับซ้ำ/วัน</summary>
    public static async Task<bool> GrantQuizRewardAsync()
    {
        var (ok, status, body) = await CallAuthedFnAsync("grant-quiz-reward", "{}");
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

    /// <summary>สวมกรอบ — server ตรวจ ownership ก่อนสวม</summary>
    public static async Task EquipFrameAsync(string itemId)
    {
        await CallAuthedFnAsync("equip-cosmetic", $"{{\"equippedFrame\":\"{itemId}\"}}");
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
        };
        string owned = PlayerPrefs.GetString("OwnedItems", "");
        LocalProfile.OwnedFrames = string.IsNullOrEmpty(owned)
            ? new List<string> { "frame_default" }
            : new List<string>(owned.Split(','));
    }
}
