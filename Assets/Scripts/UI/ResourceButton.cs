using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class ResourceButton : MonoBehaviour, IPointerClickHandler
{
    public Image iconImage;
    public TextMeshProUGUI pendingAmountText; // เลข x1, x2 (ตอนกำลังหยิบ)
    public TextMeshProUGUI remainingAmountText; // เลขบอกจำนวนคงเหลือ
    public string resourceType;
    
    private GameController gameController;

    // Awake: สำหรับปุ่มเหรียญที่ถูกวางมือใน Scene (ไม่ได้สร้างจากโค้ด)
    void Awake()
    {
        if (gameController == null)
            gameController = FindFirstObjectByType<GameController>();
    }

    // Setup แบบมี GameController ส่งเข้ามา (สำหรับ SpawnResourceBank)
    public void Setup(GameController gc, string type)
    {
        gameController = gc;
        resourceType = type;
        Sprite s = Resources.Load<Sprite>("Tokens/" + type);
        if (s != null && iconImage != null) iconImage.sprite = s;
        UpdatePendingUI(0);
    }

    // Setup แบบไม่มี GameController (backward compatible)
    public void Setup(string type)
    {
        if (gameController == null)
            gameController = FindFirstObjectByType<GameController>();
        resourceType = type;
        Sprite s = Resources.Load<Sprite>("Tokens/" + type);
        if (s != null && iconImage != null) iconImage.sprite = s;
        UpdatePendingUI(0);
    }

    public void UpdatePendingUI(int amount)
    {
        if (pendingAmountText != null)
            pendingAmountText.text = amount > 0 ? "x" + amount.ToString() : ""; 
    }

    // อัปเดตตัวเลขเหรียญคงเหลือ
    public void UpdateRemainingUI(int amount)
    {
        if (remainingAmountText != null)
            remainingAmountText.text = amount.ToString();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // fallback: ถ้ายังไม่มี reference ให้ลองหาอีกครั้ง
        if (gameController == null)
            gameController = FindFirstObjectByType<GameController>();

        if (gameController != null)
        {
            Debug.Log($"<color=green>[ResourceButton]</color> กดเหรียญ: {resourceType}");
            gameController.OnResourceClicked(this);
        }
        else
        {
            Debug.LogError("[ResourceButton] หา GameController ไม่เจอเลย! ตรวจดูว่ามี GameController อยู่ใน Scene หรือเปล่า");
        }
    }
}