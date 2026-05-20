using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class PlayerUI : MonoBehaviour
{
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI scoreText;
    public Image panelBackground;
    public Image characterPortrait; // ภาพประจำตัวละคร
    public bool isBot; // ระบุว่าเป็นบอทหรือไม่

    [Header("Name Frame")]
    public Image nameFrameImage; // Image ที่ครอบ nameText — ใส่กรอบจากร้านค้า

    [Header("Resources & Score")]
    public int currentScore = 0; 
    public int[] coins = new int[6]; 
    public int quizBlackCoins = 0;
    public TextMeshProUGUI[] coinTexts; 

    [Header("Card Bonuses")]
    public int[] bonuses = new int[5]; 
    public TextMeshProUGUI[] bonusTexts; 

    [Header("Timer UI")]
    public Image timerBarFill; 

    [Header("Reserved Cards (การ์ดที่จองไว้)")]
    public List<CardData> reservedCards = new List<CardData>(); 
    public Transform reservedAreaTransform; // <--- เพิ่มบรรทัดนี้ เพื่อบอกว่าการ์ดจองต้องไปเกิดที่ไหน

    public void SetupPlayer(string newName)
    {
        if (nameText != null) nameText.text = newName;
        currentScore = 0;
        System.Array.Clear(coins, 0, 6);
        quizBlackCoins = 0;
        System.Array.Clear(bonuses, 0, 5);
        reservedCards.Clear();
        if (reservedAreaTransform != null) {
            foreach (Transform child in reservedAreaTransform) Destroy(child.gameObject);
        }
        UpdateUI();
        if (scoreText != null) scoreText.text = "0";
    }

    public void AddScore(int points) {
        currentScore += points;
        if (scoreText != null) scoreText.text = currentScore.ToString();
    }

    public void AddBonus(int bonusIndex) {
        if (bonusIndex >= 0 && bonusIndex < 5) {
            bonuses[bonusIndex]++; UpdateUI();
        }
    }

    public void ReceiveCoins(int[] pickedCoins) {
        for (int i = 0; i < 6; i++) coins[i] += pickedCoins[i];
        UpdateUI();
    }

    public void AddQuizBlackCoin(int amount = 1)
    {
        if (amount <= 0)
        {
            return;
        }

        quizBlackCoins += amount;
        coins[5] += amount;
        UpdateUI();
    }

    public int SpendWildcardCoins(int amount)
    {
        if (amount <= 0)
        {
            return 0;
        }

        int availableWildcardCoins = Mathf.Max(0, coins[5]);
        int amountToSpend = Mathf.Min(amount, availableWildcardCoins);
        int blackCoinsSpent = Mathf.Min(amountToSpend, quizBlackCoins);
        int goldCoinsSpent = amountToSpend - blackCoinsSpent;

        quizBlackCoins -= blackCoinsSpent;
        coins[5] -= amountToSpend;

        return goldCoinsSpent;
    }

    public void UpdateUI() {
        // [FIX] null-guard array ก่อน + bounds check ทั้งสองด้าน ป้องกัน NullRef / IndexOutOfRange
        if (coinTexts != null)
            for (int i = 0; i < coinTexts.Length && i < coins.Length; i++)
                if (coinTexts[i] != null) coinTexts[i].text = coins[i].ToString();

        if (bonusTexts != null)
            for (int i = 0; i < bonusTexts.Length && i < bonuses.Length; i++)
                if (bonusTexts[i] != null) bonusTexts[i].text = bonuses[i].ToString();
    }

    [Header("Turn Indicator")]
    public GameObject activeTurnBorder; // ใส่ GameObject ที่เป็นกรอบสีเหลืองเรืองแสงสำหรับเทิร์นนี้

    // จัดเก็บ sprite เริ่มต้นของ panel เผื่อไว้ใช้ตอนไม่ได้ใส่กรอบ
    private Sprite defaultPanelSprite;

    private void Awake()
    {
        if (panelBackground != null)
        {
            defaultPanelSprite = panelBackground.sprite;
        }
    }

    public void SetActiveTurn(bool isActive) {
        // 1. เปิด/ปิด กรอบเน้นขอบ (ถ้าได้เชื่อมต่อไว้ใน Inspector)
        if (activeTurnBorder != null)
        {
            activeTurnBorder.SetActive(isActive);
        }

        if (panelBackground != null)
        {
            // [FIX] ไม่ดรอปสีเป็นสีเทา (0.7f) แล้ว เพื่อรักษาสีจริงของกรอบ PNG ให้สวยงามตลอดเวลา
            // และใช้ activeTurnBorder เป็นตัวบอกว่าใครกำลังเล่นแทน
            panelBackground.color = Color.white;
        }

        if (!isActive && timerBarFill != null) timerBarFill.fillAmount = 0;
    }

    public void UpdateTimerBar(float fillAmount) {
        if (timerBarFill != null) timerBarFill.fillAmount = fillAmount;
    }

    /// <summary>
    /// เปลี่ยนพื้นหลังของ Player Panel เป็นรูปภาพจากร้านค้า
    /// </summary>
    public void ApplyNameFrame(Sprite frameSprite, Color frameColor)
    {
        if (panelBackground == null) return;

        if (frameSprite != null)
        {
            // ถ้ามีรูประบุมา ให้ใช้รูปนั้น และตั้งสีเป็นขาว (สว่างสุด) เพื่อให้สีรูปดั้งเดิมไม่เพี้ยน
            panelBackground.sprite = frameSprite;
            panelBackground.color = Color.white; 
        }
        else
        {
            // ถ้าไม่มีรูประบุ (ระบบกรอบสี) ให้ใช้กรอบเดิมและเปลี่ยนสี
            if (defaultPanelSprite != null)
                panelBackground.sprite = defaultPanelSprite;
            panelBackground.color = frameColor;
        }
    }

    /// <summary>เอาพื้นหลังพิเศษออก</summary>
    public void HideNameFrame()
    {
        if (panelBackground == null) return;

        if (defaultPanelSprite != null)
        {
            panelBackground.sprite = defaultPanelSprite;
        }
        // ตั้งค่าสีกลับเป็นปกติ (สีเทา 70% สำหรับตอนรอเทิร์น)
        panelBackground.color = new Color(0.7f, 0.7f, 0.7f, 1f);
    }
}
