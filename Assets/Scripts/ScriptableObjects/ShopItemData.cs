using UnityEngine;

/// <summary>
/// ScriptableObject สำหรับไอเทมในร้านค้า
/// สร้างจาก: Right-click > Game Specific > Shop Item
/// </summary>
[CreateAssetMenu(fileName = "NewShopItem", menuName = "Game Specific/Shop Item")]
public class ShopItemData : ScriptableObject
{
    [Header("ข้อมูลพื้นฐาน")]
    public string itemId;           // unique key เช่น "frame_gold"
    public string itemName;         // ชื่อแสดง เช่น "กรอบทอง"
    public string itemNameEn;       // ชื่อภาษาอังกฤษ เช่น "Gold Frame"
    [TextArea(2, 4)]
    public string description;      // คำอธิบาย

    [Header("ราคา")]
    public int price;               // ราคาหน่วย Gem (0 = ฟรี)

    [Header("Visuals")]
    public Sprite previewSprite;    // รูป icon ในหน้าร้าน
    public Sprite frameSprite;      // รูปกรอบจริง (9-sliced) ที่ใส่รอบชื่อ
    public Color frameColor = Color.white;

    [Header("ประเภท")]
    public ItemType itemType = ItemType.NameFrame;

    public enum ItemType
    {
        NameFrame,
        Avatar
    }
}
