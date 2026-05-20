using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

/// <summary>
/// Script สำหรับ item card แต่ละใบในหน้าร้านค้า
/// ใส่ใน Prefab: Assets/Prefabs/UI/ShopItemCardPrefab
///
/// Hierarchy ของ Prefab:
///   ShopItemCardPrefab (Image + Button)
///   ├── ItemIcon       (Image)       ← itemIcon
///   ├── ItemName       (TMP)         ← itemNameText
///   ├── PriceRow
///   │   ├── GemIcon    (Image)
///   │   └── PriceText  (TMP)         ← priceText
///   ├── OwnedBadge     (GameObject)  ← ownedBadge
///   └── EquippedBadge  (GameObject)  ← equippedBadge
/// </summary>
public class ShopItemCardUI : MonoBehaviour
{
    [Header("UI References")]
    public Image itemIcon;
    public TextMeshProUGUI itemNameText;
    public TextMeshProUGUI priceText;
    public GameObject ownedBadge;
    public GameObject equippedBadge;
    public Button selectButton;
    public Image cardBackground;

    private ShopItemData _data;
    private Action<ShopItemData> _onClicked;

    public void Setup(ShopItemData data, Action<ShopItemData> onClicked)
    {
        _data      = data;
        _onClicked = onClicked;

        if (itemNameText != null) itemNameText.text = data.itemName;
        if (itemIcon     != null && data.previewSprite != null)
        {
            itemIcon.sprite  = data.previewSprite;
            itemIcon.enabled = true;
        }

        if (selectButton != null)
        {
            selectButton.onClick.RemoveAllListeners();
            selectButton.onClick.AddListener(() => _onClicked?.Invoke(_data));
        }

        RefreshState();
    }

    public void RefreshState()
    {
        if (_data == null) return;

        bool owned    = ShopManager.OwnsItem(_data.itemId);
        bool equipped = ShopManager.GetEquippedFrame() == _data.itemId;

        if (priceText != null)
            priceText.text = owned ? "✅ มีแล้ว" : _data.price.ToString();

        if (ownedBadge    != null) ownedBadge.SetActive(owned && !equipped);
        if (equippedBadge != null) equippedBadge.SetActive(equipped);

        // เปลี่ยนสีกรอบ card ตามสถานะ
        if (cardBackground != null)
        {
            if (equipped)      cardBackground.color = new Color(1f, 0.84f, 0f, 0.3f);    // ทอง
            else if (owned)    cardBackground.color = new Color(0.2f, 0.9f, 0.5f, 0.2f); // เขียว
            else               cardBackground.color = new Color(0.15f, 0.2f, 0.3f, 0.8f); // มืด
        }
    }
}
