using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// ใช้ Interface 3 ตัวนี้เพื่อจับการแตะ ปล่อย และลากนิ้วออก
public class CardDisplay : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler 
{
    public CardData data;
    public bool isReserved = false; 
    public PlayerUI ownerUI; 
    public Image cardImage; 

    [Header("ตั้งค่าระบบสัมผัส (มือถือ)")]
    public float holdDuration = 0.5f; // กดค้าง 0.5 วินาทีถึงจะนับว่าจอง (ปรับให้ไวขึ้นจะได้ทันใจ)
    private bool isPointerDown = false;
    private float timePressed = 0f;
    private bool isLongPressTriggered = false;
    private GameController gameController; // cache ไว้เต็มๆ ไม่ต้องค้นหาใหม่ทุกคลิค

    void Awake()
    {
        if (cardImage == null) cardImage = GetComponent<Image>();
        gameController = FindFirstObjectByType<GameController>();
    }

    public void LoadCardData(CardData cardData)
    {
        data = cardData;
        if (cardImage != null && data != null) {
            // Auto-link รูปภาพจาก Card Image/{category}/Tier{tier}/{imageName}
            string imagePath = $"Card Image/{data.category}/Tier{data.tier}/{data.imageName}";
            Sprite loadedSprite = Resources.Load<Sprite>(imagePath);
            if (loadedSprite != null) {
                cardImage.sprite = loadedSprite;
            } else {
                Debug.LogWarning($"[CardDisplay] ไม่พบรูปการ์ด: {imagePath}");
            }
        }
    }

    // ฟังก์ชันนี้จะคอยนับเวลาตอนที่เราแตะนิ้วค้างไว้
    void Update()
    {
        if (isPointerDown && !isLongPressTriggered)
        {
            timePressed += Time.deltaTime;
            // ถ้านิ้วกดค้างนานเกินเวลาที่ตั้งไว้ ให้สั่ง "จองการ์ด" ทันที
            if (timePressed >= holdDuration)
            {
                isLongPressTriggered = true; // ล็อกไว้ไม่ให้ทำงานซ้ำ
                OnLongPress(); 
            }
        }
    }

    // 1. เมื่อเริ่มแตะจอ
    public void OnPointerDown(PointerEventData eventData)
    {
        isPointerDown = true;
        timePressed = 0f;
        isLongPressTriggered = false;
    }

    // 2. เมื่อยกนิ้วขึ้น
    public void OnPointerUp(PointerEventData eventData)
    {
        // ถ้าปล่อยนิ้วก่อนที่เวลาจะครบ 0.5 วินาที = ถือว่าเป็นการ "แตะสั้นๆ เพื่อซื้อการ์ด"
        if (isPointerDown && !isLongPressTriggered)
        {
            OnShortTap();
        }
        ResetPress();
    }

    // 3. เมื่อลากนิ้วหลุดออกนอกกรอบการ์ด (ป้องกันการกดลั่น)
    public void OnPointerExit(PointerEventData eventData)
    {
        ResetPress();
    }

    private void ResetPress()
    {
        isPointerDown = false;
        timePressed = 0f;
    }

    // ==========================================
    // แยกการทำงานให้ชัดเจน
    // ==========================================

    private void OnShortTap()
    {
        if (data == null) return;
        if (gameController == null) gameController = FindFirstObjectByType<GameController>();
        if (gameController == null) return;

        GameLog.Log($"[แตะสั้นๆ] สั่งซื้อการ์ด ID: {data.cardId}");

        if (isReserved) {
            gameController.BuyReservedCard(this);
        } else {
            gameController.OnCardClicked(this);
        }
    }

    private void OnLongPress()
    {
        if (data == null) return;
        if (gameController == null) gameController = FindFirstObjectByType<GameController>();
        if (gameController == null) return;

        if (!isReserved) {
            GameLog.Log($"[กดค้าง] เรียกหน้าต่างจองการ์ด ID: {data.cardId}");
            gameController.PromptReserveCard(this);
        }
    }
}