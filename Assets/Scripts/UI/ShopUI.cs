using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;
using StartupCity.Audio;

public class ShopUI : MonoBehaviour
{
    [Header("Header")]
    public TextMeshProUGUI gemBalanceText;
    public Button backButton;

    [Header("Shop Items")]
    public ShopItemData[] allItems;

    [Header("Carousel Display")]
    public GameObject previewPanel;
    public Image previewFrameImage;
    public TextMeshProUGUI previewNameText;
    public TMP_FontAsset previewNameFont; // เพิ่มตัวแปรสำหรับรับฟอนต์ ThaleahFat SDF
    public TextMeshProUGUI selectedItemNameText;
    public TextMeshProUGUI selectedItemPriceText;
    public Button actionButton;
    public TextMeshProUGUI actionButtonText;
    
    [Header("Carousel Navigation")]
    public Button leftButton;
    public Button rightButton;
    public TextMeshProUGUI pageIndicatorText;

    [Header("Toast")]
    public TextMeshProUGUI toastText;

    private int _currentIndex = 0;
    private ShopItemData _selectedItem;
    private Coroutine _toastCoroutine;

    private void OnEnable()
    {
        UpdateGemDisplay();
    }

    private void Start()
    {
        if (backButton != null) backButton.onClick.AddListener(OnClickBack);
        if (actionButton != null) actionButton.onClick.AddListener(OnClickAction);
        
        if (leftButton != null) leftButton.onClick.AddListener(NavigateLeft);
        if (rightButton != null) rightButton.onClick.AddListener(NavigateRight);

        if (previewPanel != null) previewPanel.SetActive(true);
        if (toastText != null) toastText.gameObject.SetActive(false);

        // เปลี่ยนฟอนต์ตามที่ลากใส่ใน Inspector
        if (previewNameFont != null && previewNameText != null)
        {
            previewNameText.font = previewNameFont;
        }

        UpdateGemDisplay();

        if (CurrencyManager.Instance != null)
            CurrencyManager.Instance.OnGemsChanged += OnGemsUpdated;

        if (allItems != null && allItems.Length > 0)
        {
            _currentIndex = 0;
            ShowItem(_currentIndex);
        }
    }

    private void OnDestroy()
    {
        if (CurrencyManager.Instance != null)
            CurrencyManager.Instance.OnGemsChanged -= OnGemsUpdated;
    }

    private void OnGemsUpdated(int newVal)
    {
        UpdateGemDisplay();
    }

    private void UpdateGemDisplay()
    {
        if (gemBalanceText == null) return;
        int gems = CurrencyManager.Instance != null
            ? CurrencyManager.Instance.Gems
            : PlayerPrefs.GetInt("TotalGems", 0);
        gemBalanceText.text = gems.ToString();
    }

    public void NavigateLeft()
    {
        AudioManager.Instance?.PlayButtonClick();
        if (allItems == null || allItems.Length == 0) return;
        _currentIndex--;
        if (_currentIndex < 0) _currentIndex = allItems.Length - 1;
        ShowItem(_currentIndex);
    }

    public void NavigateRight()
    {
        AudioManager.Instance?.PlayButtonClick();
        if (allItems == null || allItems.Length == 0) return;
        _currentIndex++;
        if (_currentIndex >= allItems.Length) _currentIndex = 0;
        ShowItem(_currentIndex);
    }

    private void ShowItem(int index)
    {
        if (allItems == null || index < 0 || index >= allItems.Length) return;
        
        _selectedItem = allItems[index];
        
        if (pageIndicatorText != null)
            pageIndicatorText.text = $"{index + 1} / {allItems.Length}";

        OpenPreview(_selectedItem);
    }

    private void OpenPreview(ShopItemData item)
    {
        if (previewPanel == null) return;
        previewPanel.SetActive(true);

        if (selectedItemNameText != null) 
            selectedItemNameText.text = !string.IsNullOrEmpty(item.itemNameEn) ? item.itemNameEn : item.itemName;

        bool owned    = ShopManager.OwnsItem(item.itemId);
        bool equipped = ShopManager.GetEquippedFrame() == item.itemId;

        if (selectedItemPriceText != null)
            selectedItemPriceText.text = owned ? "" : item.price.ToString();

        if (previewFrameImage != null)
        {
            // [FIX] ถ้ามี Sprite (PNG Frame) ให้ใช้สีขาว 100% เพื่อไม่ให้สีไปย้อมทับรูป
            previewFrameImage.color   = (item.frameSprite != null) ? Color.white : item.frameColor;
            previewFrameImage.sprite  = item.frameSprite;
            previewFrameImage.enabled = true;
        }
        if (previewNameText != null)
            previewNameText.text = PlayerPrefs.GetString("Username", "Player 1");

        if (actionButtonText != null)
        {
            if (equipped)      actionButtonText.text = "✅ EQUIPPED";
            else if (owned)    actionButtonText.text = "EQUIP";
            else               actionButtonText.text = $"BUY ({item.price} Gems)";
        }
        if (actionButton != null)
            actionButton.interactable = !equipped;
    }

    public void OnClickAction()
    {
        if (_selectedItem == null) return;
        bool owned = ShopManager.OwnsItem(_selectedItem.itemId);

        if (!owned)
        {
            bool success = ShopManager.TryBuyItem(_selectedItem);
            if (success)
            {
                AudioManager.Instance?.PlayBuySuccess();
                ShopManager.EquipFrame(_selectedItem.itemId);
                DisplayToast($"Bought '{_selectedItem.itemNameEn}' successfully! 🎉");
                UpdateGemDisplay();
                ShowItem(_currentIndex);
            }
            else
            {
                AudioManager.Instance?.PlayWarningText();
                DisplayToast("Not enough Gems!");
            }
        }
        else
        {
            AudioManager.Instance?.PlayEquipFrame();
            ShopManager.EquipFrame(_selectedItem.itemId);
            DisplayToast($"Equipped '{_selectedItem.itemNameEn}'!");
            ShowItem(_currentIndex);
        }
    }

    public void OnClickBack()
    {
        AudioManager.Instance?.PlayButtonClick();
        SceneManager.LoadScene("MainMenu 1");
    }

    private void DisplayToast(string msg)
    {
        if (toastText == null) return;
        if (_toastCoroutine != null) StopCoroutine(_toastCoroutine);
        _toastCoroutine = StartCoroutine(RunToast(msg));
    }

    private IEnumerator RunToast(string msg)
    {
        toastText.text = msg;
        toastText.gameObject.SetActive(true);
        yield return new WaitForSeconds(2.5f);
        toastText.gameObject.SetActive(false);
    }
}
