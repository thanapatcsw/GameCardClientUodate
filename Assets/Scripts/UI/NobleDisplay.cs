using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class NobleDisplay : MonoBehaviour
{
    [Header("ข้อมูลขุนนาง")]
    public NobleData nobleData;

    [Header("UI Elements")]
    public Image artworkImage;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI vpText;
    
    [Header("UI เงื่อนไขการสุ่ม (Bonuses)")]
    // อาร์เรย์เก็บตัวหนังสือแสดงจำนวนที่ต้องการ เรียงตามลำดับ (CPU, RAM, Network, Storage, Security)
    public TextMeshProUGUI[] requirementTexts = new TextMeshProUGUI[5];
    public GameObject[] requirementIcons = new GameObject[5];
    [Header("UI เมื่อถูกครอบครอง")]
    public GameObject claimOverlay; // กล่องสี่เหลี่ยมสีดำโปร่งแสงที่จะเอามาบังการ์ด
    public TextMeshProUGUI ownerText; // ข้อความบอกว่าใครได้ไป

    public void SetupNoble(NobleData data)
    {
        nobleData = data;
        
        if (nameText != null) nameText.text = data.nobleName;
        if (vpText != null) vpText.text = data.victoryPoints.ToString();
        if (artworkImage != null && data.artwork != null) artworkImage.sprite = data.artwork;

        // ซ่อน overlay ตั้งแต่ต้นเกม
        if (claimOverlay != null) claimOverlay.SetActive(false);
        if (ownerText != null) ownerText.text = "";

        // วนลูปแสดงเฉพาะเงื่อนไขที่ต้องการมากกว่า 0
        for (int i = 0; i < 5; i++)
        {
            int req = data.requiredBonuses[i];
            if (req > 0)
            {
                if (requirementIcons[i] != null) requirementIcons[i].SetActive(true);
                if (requirementTexts[i] != null) requirementTexts[i].text = req.ToString();
            }
            else
            {
                if (requirementIcons[i] != null) requirementIcons[i].SetActive(false);
            }
        }
    }

    // ฟังก์ชันบอกว่าใครสะสมครบและได้การ์ดไป
    public void ClaimNoble(string playerName)
    {
        // เปิดหน้าจอบังและแสดงชื่อผู้เล่นไปทับเลย
        if (claimOverlay != null) claimOverlay.SetActive(true);
        if (ownerText != null) 
        {
            ownerText.gameObject.SetActive(true);
            ownerText.text = "Claimed by\n" + playerName;
        }

        // ทำให้มืดลงเล็กน้อยเพื่อให้รู้ว่าโดนเอาไปแล้ว
        if (artworkImage != null)
        {
            artworkImage.color = new Color(0.5f, 0.5f, 0.5f, 1f); 
        }
    }
}
