using System.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Service จัดการ CRUD ข้อมูล Player กับ Supabase
/// ใช้ LocalCache (PlayerPrefs) เป็น fallback ถ้าออฟไลน์
/// </summary>
public static class PlayerDataService
{
    public static PlayerProfile LocalProfile { get; private set; }

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
            var result = await sb.From<PlayerProfile>().Select("*").Single();
            if (result != null)
            {
                LocalProfile = result;
                SyncToLocalCache(result);
                
                // แจ้งเตือน UI ให้รีเฟรชค่า Gems/Coins
                if (CurrencyManager.Instance != null)
                {
                    CurrencyManager.Instance.RefreshFromLocalCache();
                }

                Debug.Log($"[PlayerData] Profile loaded: {result.Username} | MMR: {result.Mmr} | Gems: {result.Gems}");
            }
        }
        catch (System.Exception e)
        {
            Debug.Log($"[PlayerData] No profile found, creating new one: {e.Message}");
            
            // ถ้ายังไม่มี Profile ให้สร้างใหม่ (Upsert)
            var newProfile = new PlayerProfile
            {
                Id = sb.Auth.CurrentUser.Id,
                Username = SupabaseManager.Instance.GetCurrentUsername(),
                Gems = PlayerPrefs.GetInt("TotalGems", 0),
                Mmr = 1000,
                OwnedFrames = new List<string> { "frame_default" },
                EquippedFrame = "frame_default",
                SelectedCharacter = 0,
                UpdatedAt = System.DateTime.UtcNow
            };

            try
            {
                await sb.From<PlayerProfile>().Upsert(newProfile);
                LocalProfile = newProfile;
                SyncToLocalCache(newProfile);
                Debug.Log("[PlayerData] New profile created successfully.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[PlayerData] Failed to create new profile: {ex.Message}");
                LoadFromLocalCache();
            }
            
            if (CurrencyManager.Instance != null) CurrencyManager.Instance.RefreshFromLocalCache();
        }
    }

    public static async Task SaveCurrencyAsync(int gems)
    {
        PlayerPrefs.SetInt("TotalGems", gems);
        PlayerPrefs.Save();

        if (LocalProfile != null)
        {
            LocalProfile.Gems = gems;
        }

        var sb = SupabaseManager.Instance?.Client;
        if (sb == null) return;

        try
        {
            var updateData = new PlayerProfile 
            { 
                Id = sb.Auth.CurrentUser.Id, 
                Username = SupabaseManager.Instance.GetCurrentUsername(),
                Gems = gems, 
                Mmr = LocalProfile?.Mmr ?? PlayerPrefs.GetInt("MMR", 1000),
                Wins = LocalProfile?.Wins ?? 0,
                Losses = LocalProfile?.Losses ?? 0,
                OwnedFrames = LocalProfile?.OwnedFrames ?? new List<string>(PlayerPrefs.GetString("OwnedItems", "frame_default").Split(',')),
                EquippedFrame = LocalProfile?.EquippedFrame ?? PlayerPrefs.GetString("EquippedFrame", "frame_default"),
                SelectedCharacter = LocalProfile?.SelectedCharacter ?? PlayerPrefs.GetInt("SelectedCharacter", 0),
                UpdatedAt = System.DateTime.UtcNow 
            };
            await sb.From<PlayerProfile>().Upsert(updateData);
            Debug.Log($"[PlayerData] Currency synced: Gems={gems}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[PlayerData] Currency sync failed: {e.Message}");
        }
    }

    public static async Task SaveInventoryAsync(List<string> ownedFrames, string equippedFrame)
    {
        PlayerPrefs.SetString("OwnedItems", string.Join(",", ownedFrames));
        PlayerPrefs.SetString("EquippedFrame", equippedFrame);
        PlayerPrefs.Save();

        if (LocalProfile != null)
        {
            LocalProfile.OwnedFrames = ownedFrames;
            LocalProfile.EquippedFrame = equippedFrame;
        }

        var sb = SupabaseManager.Instance?.Client;
        if (sb == null) return;

        try
        {
            var updateData = new PlayerProfile 
            { 
                Id = sb.Auth.CurrentUser.Id, 
                Username = SupabaseManager.Instance.GetCurrentUsername(),
                Gems = LocalProfile?.Gems ?? PlayerPrefs.GetInt("TotalGems", 0),
                Mmr = LocalProfile?.Mmr ?? PlayerPrefs.GetInt("MMR", 1000),
                Wins = LocalProfile?.Wins ?? 0,
                Losses = LocalProfile?.Losses ?? 0,
                OwnedFrames = ownedFrames, 
                EquippedFrame = equippedFrame, 
                SelectedCharacter = LocalProfile?.SelectedCharacter ?? PlayerPrefs.GetInt("SelectedCharacter", 0),
                UpdatedAt = System.DateTime.UtcNow 
            };
            await sb.From<PlayerProfile>().Upsert(updateData);
            Debug.Log($"[PlayerData] Inventory synced. Equipped: {equippedFrame}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[PlayerData] Inventory sync failed: {e.Message}");
        }
    }

    public static async Task SaveCharacterAsync(int characterIndex)
    {
        PlayerPrefs.SetInt("SelectedCharacter", characterIndex);
        PlayerPrefs.Save();

        if (LocalProfile != null)
        {
            LocalProfile.SelectedCharacter = characterIndex;
        }

        var sb = SupabaseManager.Instance?.Client;
        if (sb == null) return;

        try
        {
            var updateData = new PlayerProfile 
            { 
                Id = sb.Auth.CurrentUser.Id, 
                Username = SupabaseManager.Instance.GetCurrentUsername(),
                Gems = LocalProfile?.Gems ?? PlayerPrefs.GetInt("TotalGems", 0),
                Mmr = LocalProfile?.Mmr ?? PlayerPrefs.GetInt("MMR", 1000),
                Wins = LocalProfile?.Wins ?? 0,
                Losses = LocalProfile?.Losses ?? 0,
                OwnedFrames = LocalProfile?.OwnedFrames ?? new List<string>(PlayerPrefs.GetString("OwnedItems", "frame_default").Split(',')),
                EquippedFrame = LocalProfile?.EquippedFrame ?? PlayerPrefs.GetString("EquippedFrame", "frame_default"),
                SelectedCharacter = characterIndex, 
                UpdatedAt = System.DateTime.UtcNow 
            };
            await sb.From<PlayerProfile>().Upsert(updateData);
            Debug.Log($"[PlayerData] Character synced: {characterIndex}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[PlayerData] Character sync failed: {e.Message}");
        }
    }

    public static async Task SaveMatchResultAsync(int newMmr, bool won)
    {
        PlayerPrefs.SetInt("MMR", newMmr);
        PlayerPrefs.Save();

        if (LocalProfile != null)
        {
            LocalProfile.Mmr = newMmr;
            if (won) LocalProfile.Wins++;
            else LocalProfile.Losses++;
        }

        var sb = SupabaseManager.Instance?.Client;
        if (sb == null) return;

        try
        {
            int wins = LocalProfile?.Wins ?? 0;
            int losses = LocalProfile?.Losses ?? 0;

            var updateData = new PlayerProfile 
            { 
                Id = sb.Auth.CurrentUser.Id, 
                Username = SupabaseManager.Instance.GetCurrentUsername(),
                Gems = LocalProfile?.Gems ?? PlayerPrefs.GetInt("TotalGems", 0),
                Mmr = newMmr, 
                Wins = wins, 
                Losses = losses, 
                OwnedFrames = LocalProfile?.OwnedFrames ?? new List<string>(PlayerPrefs.GetString("OwnedItems", "frame_default").Split(',')),
                EquippedFrame = LocalProfile?.EquippedFrame ?? PlayerPrefs.GetString("EquippedFrame", "frame_default"),
                SelectedCharacter = LocalProfile?.SelectedCharacter ?? PlayerPrefs.GetInt("SelectedCharacter", 0),
                UpdatedAt = System.DateTime.UtcNow 
            };
            await sb.From<PlayerProfile>().Upsert(updateData);
            Debug.Log($"[PlayerData] MMR synced: {newMmr} | W:{wins} L:{losses}");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PlayerData] MMR sync failed: {e.Message}");
        }
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
