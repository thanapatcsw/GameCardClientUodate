using UnityEngine;
using System;

/// <summary>
/// Singleton จัดการสกุลเงิน Gem และ Coin
/// DontDestroyOnLoad — อยู่ทุก Scene
/// </summary>
public class CurrencyManager : MonoBehaviour
{
    public static CurrencyManager Instance
    {
        get
        {
            if (_isQuitting) return null;

            if (_instance == null)
            {
                _instance = UnityEngine.Object.FindFirstObjectByType<CurrencyManager>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("CurrencyManager (Auto)");
                    _instance = go.AddComponent<CurrencyManager>();
                    DontDestroyOnLoad(go);
                }
            }
            return _instance;
        }
    }
    private static CurrencyManager _instance;
    private static bool _isQuitting = false;

    private const string GEMS_KEY = "TotalGems";
    private const string COINS_KEY = "TotalCoins";

    public event Action<int> OnGemsChanged;
    public event Action<int> OnCoinsChanged;

    public int Gems => PlayerPrefs.GetInt(GEMS_KEY, 0);
    public int Coins => PlayerPrefs.GetInt(COINS_KEY, 0);

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        _isQuitting = false;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    private void OnApplicationQuit()
    {
        _isQuitting = true;
    }

    public void AddGems(int amount)
    {
        if (amount <= 0) return;
        PlayerPrefs.SetInt(GEMS_KEY, Gems + amount);
        PlayerPrefs.Save();
        OnGemsChanged?.Invoke(Gems);
        _ = PlayerDataService.SaveCurrencyAsync(Gems, Coins);
        Debug.Log($"[CurrencyManager] +{amount} Gems → รวม {Gems}");
    }

    public bool SpendGems(int amount)
    {
        if (amount <= 0) return true;
        if (Gems < amount)
        {
            Debug.Log($"[CurrencyManager] Gems ไม่พอ! มี {Gems} ต้องการ {amount}");
            return false;
        }
        PlayerPrefs.SetInt(GEMS_KEY, Gems - amount);
        PlayerPrefs.Save();
        OnGemsChanged?.Invoke(Gems);
        _ = PlayerDataService.SaveCurrencyAsync(Gems, Coins);
        Debug.Log($"[CurrencyManager] -{amount} Gems → รวม {Gems}");
        return true;
    }

    public void AddCoins(int amount)
    {
        if (amount <= 0) return;
        PlayerPrefs.SetInt(COINS_KEY, Coins + amount);
        PlayerPrefs.Save();
        OnCoinsChanged?.Invoke(Coins);
        _ = PlayerDataService.SaveCurrencyAsync(Gems, Coins);
    }

    /// <summary>
    /// เรียกตอนจบเกม — บันทึก Coin จากเกมและ Gem reward
    /// </summary>
    public void SaveEndGameRewards(int earnedCoins, int earnedGems)
    {
        AddCoins(earnedCoins);
        AddGems(earnedGems);
        Debug.Log($"[CurrencyManager] รางวัลจบเกม: +{earnedCoins} Coins, +{earnedGems} Gems");
    }

    /// <summary>
    /// รีเฟรชค่าและส่ง Event แจ้ง UI — เรียกใช้หลังจาก Sync ข้อมูลจาก Database
    /// </summary>
    public void RefreshFromLocalCache()
    {
        OnGemsChanged?.Invoke(Gems);
        OnCoinsChanged?.Invoke(Coins);
        Debug.Log($"[CurrencyManager] Refreshed from local cache. Gems: {Gems}, Coins: {Coins}");
    }

    /// <summary>
    /// Debug: เพิ่ม Gems สำหรับทดสอบ
    /// </summary>
    [ContextMenu("Add 50 Gems (Debug)")]
    public void DebugAddGems()
    {
        AddGems(50);
    }
}
