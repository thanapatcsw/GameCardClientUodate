using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Static helper จัดการการซื้อ / สวมใส่ / เช็ค ownership ของ Shop Items
/// ไม่ต้องแนบเป็น Component — ใช้ได้เลยจากทุกที่
/// </summary>
public static class ShopManager
{
    private const string OWNED_KEY    = "OwnedItems";
    private const string EQUIPPED_KEY = "EquippedFrame";
    public const  string DEFAULT_FRAME = "frame_default";

    // ─── Ownership ────────────────────────────────────────────────────────

    public static HashSet<string> GetOwnedItems()
    {
        string saved = PlayerPrefs.GetString(OWNED_KEY, "");
        var set = new HashSet<string> { DEFAULT_FRAME };
        if (!string.IsNullOrEmpty(saved))
        {
            foreach (var id in saved.Split(','))
            {
                if (!string.IsNullOrEmpty(id)) set.Add(id.Trim());
            }
        }
        return set;
    }

    public static bool OwnsItem(string itemId)
    {
        if (itemId == DEFAULT_FRAME) return true;
        return GetOwnedItems().Contains(itemId);
    }

    private static void SaveOwnedItems(HashSet<string> items)
    {
        PlayerPrefs.SetString(OWNED_KEY, string.Join(",", items));
        PlayerPrefs.Save();
    }

    // ─── Buy ──────────────────────────────────────────────────────────────

    public static bool TryBuyItem(ShopItemData item)
    {
        if (item == null) return false;
        if (OwnsItem(item.itemId))
        {
            Debug.Log($"[Shop] มีไอเทม '{item.itemId}' แล้ว");
            return false;
        }

        if (CurrencyManager.Instance != null)
        {
            if (!CurrencyManager.Instance.SpendGems(item.price)) return false;
        }
        else
        {
            // Fallback for testing/debugging when starting scene directly
            int currentGems = PlayerPrefs.GetInt("TotalGems", 0);
            if (currentGems < item.price) return false;
            int newTotal = currentGems - item.price;
            PlayerPrefs.SetInt("TotalGems", newTotal);
            PlayerPrefs.Save();
            
            // บันทึกลง Database ด้วย (ถ้าล็อกอินอยู่)
            _ = PlayerDataService.SaveCurrencyAsync(newTotal);
            
            Debug.LogWarning("[Shop] CurrencyManager missing, used PlayerPrefs gems. Synced to Database.");
        }

        var owned = GetOwnedItems();
        owned.Add(item.itemId);
        SaveOwnedItems(owned);
        
        // [FIX] log error ถ้าบันทึกลง DB ไม่สำเร็จ (local PlayerPrefs อัปเดตแล้ว แต่ DB อาจพลาด)
        var saveTask = PlayerDataService.SaveInventoryAsync(
            new System.Collections.Generic.List<string>(owned),
            GetEquippedFrame()
        );
        saveTask.ContinueWith(t =>
        {
            if (t.IsFaulted)
                Debug.LogError($"[Shop] บันทึก inventory ลง DB ไม่สำเร็จ: {t.Exception?.GetBaseException().Message}");
        }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());

        Debug.Log($"[Shop] ซื้อ '{item.itemName}' สำเร็จ! เสียไป {item.price} Gems");
        return true;
    }

    // ─── Equip ────────────────────────────────────────────────────────────

    public static string GetEquippedFrame()
    {
        return PlayerPrefs.GetString(EQUIPPED_KEY, DEFAULT_FRAME);
    }

    public static void EquipFrame(string itemId)
    {
        if (!OwnsItem(itemId))
        {
            Debug.LogWarning($"[Shop] ยังไม่ได้ซื้อ '{itemId}'");
            return;
        }
        PlayerPrefs.SetString(EQUIPPED_KEY, itemId);
        PlayerPrefs.Save();
        
        var saveTask = PlayerDataService.SaveInventoryAsync(
            new System.Collections.Generic.List<string>(GetOwnedItems()),
            itemId
        );
        saveTask.ContinueWith(t =>
        {
            if (t.IsFaulted)
                Debug.LogError($"[Shop] บันทึก equip ลง DB ไม่สำเร็จ: {t.Exception?.GetBaseException().Message}");
        }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());

        Debug.Log($"[Shop] Equip frame: {itemId}");
    }

    // ─── Sprite Loader ────────────────────────────────────────────────────

    public static Sprite LoadEquippedFrameSprite()
    {
        string frameId = GetEquippedFrame();
        Sprite sp = Resources.Load<Sprite>($"Frames/{frameId}");
        if (sp == null && frameId != DEFAULT_FRAME)
        {
            sp = Resources.Load<Sprite>($"Frames/{DEFAULT_FRAME}");
        }
        return sp;
    }
}
