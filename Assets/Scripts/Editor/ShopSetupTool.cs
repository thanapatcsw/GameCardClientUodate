using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

public class ShopSetupTool
{
    [MenuItem("Tools/Setup Shop UI")]
    public static void SetupShopUI()
    {
        // 1. Create Canvas
        GameObject canvasObj = new GameObject("Canvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasObj.AddComponent<GraphicRaycaster>();
        
        // 2. Create EventSystem
        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject esObj = new GameObject("EventSystem");
            esObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esObj.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        // 3. ShopUI Component
        ShopUI shopUI = canvasObj.AddComponent<ShopUI>();

        // 4. Background
        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(canvasObj.transform, false);
        Image bgImg = bgObj.AddComponent<Image>();
        bgImg.color = new Color(0.1f, 0.1f, 0.1f, 1f);
        RectTransform bgRect = bgObj.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        // 5. HeaderPanel
        GameObject headerObj = new GameObject("HeaderPanel");
        headerObj.transform.SetParent(canvasObj.transform, false);
        Image headerImg = headerObj.AddComponent<Image>();
        headerImg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        RectTransform headerRect = headerObj.GetComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0, 0.9f);
        headerRect.anchorMax = new Vector2(1, 1);
        headerRect.offsetMin = Vector2.zero;
        headerRect.offsetMax = Vector2.zero;

        // Back Button
        GameObject backBtnObj = new GameObject("BackButton");
        backBtnObj.transform.SetParent(headerObj.transform, false);
        Image backBtnImg = backBtnObj.AddComponent<Image>();
        backBtnImg.color = Color.gray;
        Button backBtn = backBtnObj.AddComponent<Button>();
        RectTransform backBtnRect = backBtnObj.GetComponent<RectTransform>();
        backBtnRect.anchorMin = new Vector2(0, 0);
        backBtnRect.anchorMax = new Vector2(0, 1);
        backBtnRect.sizeDelta = new Vector2(150, 0);
        backBtnRect.anchoredPosition = new Vector2(75, 0);
        
        GameObject backTextObj = new GameObject("Text");
        backTextObj.transform.SetParent(backBtnObj.transform, false);
        TextMeshProUGUI backText = backTextObj.AddComponent<TextMeshProUGUI>();
        backText.text = "Back";
        backText.color = Color.white;
        backText.alignment = TextAlignmentOptions.Center;
        RectTransform backTextRect = backTextObj.GetComponent<RectTransform>();
        backTextRect.anchorMin = Vector2.zero;
        backTextRect.anchorMax = Vector2.one;
        backTextRect.offsetMin = Vector2.zero;
        backTextRect.offsetMax = Vector2.zero;
        
        shopUI.backButton = backBtn;

        // Title
        GameObject titleObj = new GameObject("TitleText");
        titleObj.transform.SetParent(headerObj.transform, false);
        TextMeshProUGUI titleText = titleObj.AddComponent<TextMeshProUGUI>();
        titleText.text = "SHOP";
        titleText.fontSize = 48;
        titleText.alignment = TextAlignmentOptions.Center;
        RectTransform titleRect = titleObj.GetComponent<RectTransform>();
        titleRect.anchorMin = Vector2.zero;
        titleRect.anchorMax = Vector2.one;
        titleRect.offsetMin = Vector2.zero;
        titleRect.offsetMax = Vector2.zero;

        // Gem Balance
        GameObject gemBalObj = new GameObject("GemBalanceText");
        gemBalObj.transform.SetParent(headerObj.transform, false);
        TextMeshProUGUI gemBalText = gemBalObj.AddComponent<TextMeshProUGUI>();
        gemBalText.text = "Gems: 0";
        gemBalText.fontSize = 36;
        gemBalText.alignment = TextAlignmentOptions.Right;
        gemBalText.color = Color.cyan;
        RectTransform gemBalRect = gemBalObj.GetComponent<RectTransform>();
        gemBalRect.anchorMin = new Vector2(1, 0);
        gemBalRect.anchorMax = new Vector2(1, 1);
        gemBalRect.sizeDelta = new Vector2(300, 0);
        gemBalRect.anchoredPosition = new Vector2(-150, 0);
        
        shopUI.gemBalanceText = gemBalText;

        // 6. ShopScrollView
        GameObject scrollObj = new GameObject("ShopScrollView");
        scrollObj.transform.SetParent(canvasObj.transform, false);
        RectTransform scrollRect = scrollObj.AddComponent<RectTransform>();
        scrollRect.anchorMin = new Vector2(0, 0);
        scrollRect.anchorMax = new Vector2(0.6f, 0.9f);
        scrollRect.offsetMin = new Vector2(20, 20);
        scrollRect.offsetMax = new Vector2(-20, -20);
        
        GameObject viewportObj = new GameObject("Viewport");
        viewportObj.transform.SetParent(scrollObj.transform, false);
        RectTransform viewportRect = viewportObj.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;
        viewportObj.AddComponent<RectMask2D>();
        
        GameObject contentObj = new GameObject("Content");
        contentObj.transform.SetParent(viewportObj.transform, false);
        RectTransform contentRect = contentObj.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1);
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0, 800);
        
        GridLayoutGroup grid = contentObj.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(200, 250);
        grid.spacing = new Vector2(20, 20);
        grid.padding = new RectOffset(20, 20, 20, 20);
        
        ScrollRect scrollRectComp = scrollObj.AddComponent<ScrollRect>();
        scrollRectComp.content = contentRect;
        scrollRectComp.viewport = viewportRect;
        scrollRectComp.horizontal = false;
        scrollRectComp.vertical = true;
        
        // shopUI.itemGridContent = contentObj.transform;

        // 7. PreviewPanel
        GameObject previewObj = new GameObject("PreviewPanel");
        previewObj.transform.SetParent(canvasObj.transform, false);
        Image previewBg = previewObj.AddComponent<Image>();
        previewBg.color = new Color(0.15f, 0.15f, 0.15f, 1f);
        RectTransform previewRect = previewObj.GetComponent<RectTransform>();
        previewRect.anchorMin = new Vector2(0.6f, 0);
        previewRect.anchorMax = new Vector2(1f, 0.9f);
        previewRect.offsetMin = new Vector2(20, 20);
        previewRect.offsetMax = new Vector2(-20, -20);
        
        GameObject pFrameObj = new GameObject("PreviewFrameImage");
        pFrameObj.transform.SetParent(previewObj.transform, false);
        Image pFrameImg = pFrameObj.AddComponent<Image>();
        RectTransform pFrameRect = pFrameObj.GetComponent<RectTransform>();
        pFrameRect.anchorMin = new Vector2(0.5f, 0.7f);
        pFrameRect.anchorMax = new Vector2(0.5f, 0.7f);
        pFrameRect.sizeDelta = new Vector2(200, 60);
        
        GameObject pNameObj = new GameObject("PreviewNameText");
        pNameObj.transform.SetParent(pFrameObj.transform, false);
        TextMeshProUGUI pNameText = pNameObj.AddComponent<TextMeshProUGUI>();
        pNameText.text = "PlayerName";
        pNameText.alignment = TextAlignmentOptions.Center;
        RectTransform pNameRect = pNameObj.GetComponent<RectTransform>();
        pNameRect.anchorMin = Vector2.zero;
        pNameRect.anchorMax = Vector2.one;
        pNameRect.offsetMin = Vector2.zero;
        pNameRect.offsetMax = Vector2.zero;
        
        GameObject pItemNameObj = new GameObject("ItemNameText");
        pItemNameObj.transform.SetParent(previewObj.transform, false);
        TextMeshProUGUI pItemNameText = pItemNameObj.AddComponent<TextMeshProUGUI>();
        pItemNameText.text = "Item Name";
        pItemNameText.fontSize = 32;
        pItemNameText.alignment = TextAlignmentOptions.Center;
        RectTransform pItemNameRect = pItemNameObj.GetComponent<RectTransform>();
        pItemNameRect.anchorMin = new Vector2(0, 0.4f);
        pItemNameRect.anchorMax = new Vector2(1, 0.5f);
        pItemNameRect.offsetMin = Vector2.zero;
        pItemNameRect.offsetMax = Vector2.zero;
        
        GameObject pPriceObj = new GameObject("PriceText");
        pPriceObj.transform.SetParent(previewObj.transform, false);
        TextMeshProUGUI pPriceText = pPriceObj.AddComponent<TextMeshProUGUI>();
        pPriceText.text = "Price: 50";
        pPriceText.fontSize = 28;
        pPriceText.alignment = TextAlignmentOptions.Center;
        pPriceText.color = Color.cyan;
        RectTransform pPriceRect = pPriceObj.GetComponent<RectTransform>();
        pPriceRect.anchorMin = new Vector2(0, 0.3f);
        pPriceRect.anchorMax = new Vector2(1, 0.4f);
        pPriceRect.offsetMin = Vector2.zero;
        pPriceRect.offsetMax = Vector2.zero;
        
        GameObject actionBtnObj = new GameObject("ActionButton");
        actionBtnObj.transform.SetParent(previewObj.transform, false);
        Image actionBtnImg = actionBtnObj.AddComponent<Image>();
        actionBtnImg.color = new Color(0.2f, 0.8f, 0.2f, 1f);
        Button actionBtn = actionBtnObj.AddComponent<Button>();
        RectTransform actionBtnRect = actionBtnObj.GetComponent<RectTransform>();
        actionBtnRect.anchorMin = new Vector2(0.2f, 0.1f);
        actionBtnRect.anchorMax = new Vector2(0.8f, 0.25f);
        actionBtnRect.offsetMin = Vector2.zero;
        actionBtnRect.offsetMax = Vector2.zero;
        
        GameObject actionTextObj = new GameObject("Text");
        actionTextObj.transform.SetParent(actionBtnObj.transform, false);
        TextMeshProUGUI actionText = actionTextObj.AddComponent<TextMeshProUGUI>();
        actionText.text = "Buy / Equip";
        actionText.fontSize = 24;
        actionText.color = Color.white;
        actionText.alignment = TextAlignmentOptions.Center;
        RectTransform actionTextRect = actionTextObj.GetComponent<RectTransform>();
        actionTextRect.anchorMin = Vector2.zero;
        actionTextRect.anchorMax = Vector2.one;
        actionTextRect.offsetMin = Vector2.zero;
        actionTextRect.offsetMax = Vector2.zero;
        
        shopUI.previewPanel = previewObj;
        shopUI.previewFrameImage = pFrameImg;
        shopUI.previewNameText = pNameText;
        shopUI.selectedItemNameText = pItemNameText;
        shopUI.selectedItemPriceText = pPriceText;
        shopUI.actionButton = actionBtn;
        shopUI.actionButtonText = actionText;

        // 8. ToastText
        GameObject toastObj = new GameObject("ToastText");
        toastObj.transform.SetParent(canvasObj.transform, false);
        TextMeshProUGUI toastText = toastObj.AddComponent<TextMeshProUGUI>();
        toastText.text = "Toast Message";
        toastText.fontSize = 32;
        toastText.alignment = TextAlignmentOptions.Center;
        toastText.color = Color.yellow;
        RectTransform toastRect = toastObj.GetComponent<RectTransform>();
        toastRect.anchorMin = new Vector2(0, 0);
        toastRect.anchorMax = new Vector2(1, 0.1f);
        toastRect.offsetMin = Vector2.zero;
        toastRect.offsetMax = Vector2.zero;
        
        shopUI.toastText = toastText;

        // Populate Items Array in ShopUI
        string[] guids = AssetDatabase.FindAssets("t:ShopItemData", new[] { "Assets/ScriptableObjects/ShopItems" });
        shopUI.allItems = new ShopItemData[guids.Length];
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            shopUI.allItems[i] = AssetDatabase.LoadAssetAtPath<ShopItemData>(path);
        }

        // ==========================================
        // CREATE PREFAB
        // ==========================================
        GameObject pCardObj = new GameObject("ShopItemCardPrefab");
        Image pCardImg = pCardObj.AddComponent<Image>();
        pCardImg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        Button pCardBtn = pCardObj.AddComponent<Button>();
        RectTransform pCardRect = pCardObj.GetComponent<RectTransform>();
        pCardRect.sizeDelta = new Vector2(200, 250);
        
        ShopItemCardUI cardUI = pCardObj.AddComponent<ShopItemCardUI>();
        cardUI.selectButton = pCardBtn;
        cardUI.cardBackground = pCardImg;
        
        GameObject cIconObj = new GameObject("ItemIcon");
        cIconObj.transform.SetParent(pCardObj.transform, false);
        Image cIconImg = cIconObj.AddComponent<Image>();
        RectTransform cIconRect = cIconObj.GetComponent<RectTransform>();
        cIconRect.anchorMin = new Vector2(0.1f, 0.4f);
        cIconRect.anchorMax = new Vector2(0.9f, 0.9f);
        cIconRect.offsetMin = Vector2.zero;
        cIconRect.offsetMax = Vector2.zero;
        cardUI.itemIcon = cIconImg;
        
        GameObject cNameObj = new GameObject("ItemNameText");
        cNameObj.transform.SetParent(pCardObj.transform, false);
        TextMeshProUGUI cNameText = cNameObj.AddComponent<TextMeshProUGUI>();
        cNameText.text = "Name";
        cNameText.alignment = TextAlignmentOptions.Center;
        cNameText.fontSize = 20;
        RectTransform cNameRect = cNameObj.GetComponent<RectTransform>();
        cNameRect.anchorMin = new Vector2(0, 0.2f);
        cNameRect.anchorMax = new Vector2(1, 0.4f);
        cNameRect.offsetMin = Vector2.zero;
        cNameRect.offsetMax = Vector2.zero;
        cardUI.itemNameText = cNameText;
        
        GameObject cPriceObj = new GameObject("PriceText");
        cPriceObj.transform.SetParent(pCardObj.transform, false);
        TextMeshProUGUI cPriceText = cPriceObj.AddComponent<TextMeshProUGUI>();
        cPriceText.text = "Price: 50";
        cPriceText.alignment = TextAlignmentOptions.Center;
        cPriceText.color = Color.cyan;
        RectTransform cPriceRect = cPriceObj.GetComponent<RectTransform>();
        cPriceRect.anchorMin = new Vector2(0, 0);
        cPriceRect.anchorMax = new Vector2(1, 0.2f);
        cPriceRect.offsetMin = Vector2.zero;
        cPriceRect.offsetMax = Vector2.zero;
        cardUI.priceText = cPriceText;
        
        GameObject cOwnedObj = new GameObject("OwnedBadge");
        cOwnedObj.transform.SetParent(pCardObj.transform, false);
        Image cOwnedImg = cOwnedObj.AddComponent<Image>();
        cOwnedImg.color = new Color(0, 0, 0, 0.5f);
        RectTransform cOwnedRect = cOwnedObj.GetComponent<RectTransform>();
        cOwnedRect.anchorMin = Vector2.zero;
        cOwnedRect.anchorMax = Vector2.one;
        cOwnedRect.offsetMin = Vector2.zero;
        cOwnedRect.offsetMax = Vector2.zero;
        GameObject cOwnedTextObj = new GameObject("Text");
        cOwnedTextObj.transform.SetParent(cOwnedObj.transform, false);
        TextMeshProUGUI cOwnedText = cOwnedTextObj.AddComponent<TextMeshProUGUI>();
        cOwnedText.text = "OWNED";
        cOwnedText.color = Color.green;
        cOwnedText.alignment = TextAlignmentOptions.Center;
        RectTransform cOwnedTextRect = cOwnedTextObj.GetComponent<RectTransform>();
        cOwnedTextRect.anchorMin = Vector2.zero;
        cOwnedTextRect.anchorMax = Vector2.one;
        cOwnedTextRect.offsetMin = Vector2.zero;
        cOwnedTextRect.offsetMax = Vector2.zero;
        cardUI.ownedBadge = cOwnedObj;
        
        GameObject cEquipObj = new GameObject("EquippedBadge");
        cEquipObj.transform.SetParent(pCardObj.transform, false);
        Image cEquipImg = cEquipObj.AddComponent<Image>();
        cEquipImg.color = new Color(0, 0, 0, 0.5f);
        RectTransform cEquipRect = cEquipObj.GetComponent<RectTransform>();
        cEquipRect.anchorMin = Vector2.zero;
        cEquipRect.anchorMax = Vector2.one;
        cEquipRect.offsetMin = Vector2.zero;
        cEquipRect.offsetMax = Vector2.zero;
        GameObject cEquipTextObj = new GameObject("Text");
        cEquipTextObj.transform.SetParent(cEquipObj.transform, false);
        TextMeshProUGUI cEquipText = cEquipTextObj.AddComponent<TextMeshProUGUI>();
        cEquipText.text = "EQUIPPED";
        cEquipText.color = Color.yellow;
        cEquipText.alignment = TextAlignmentOptions.Center;
        RectTransform cEquipTextRect = cEquipTextObj.GetComponent<RectTransform>();
        cEquipTextRect.anchorMin = Vector2.zero;
        cEquipTextRect.anchorMax = Vector2.one;
        cEquipTextRect.offsetMin = Vector2.zero;
        cEquipTextRect.offsetMax = Vector2.zero;
        cardUI.equippedBadge = cEquipObj;
        
        string prefabPath = "Assets/Prefabs/UI/ShopItemCardPrefab.prefab";
        GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(pCardObj, prefabPath);
        Object.DestroyImmediate(pCardObj);
        
        // shopUI.shopItemCardPrefab = savedPrefab;
        
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        Debug.Log("Shop UI setup complete!");
    }
}
