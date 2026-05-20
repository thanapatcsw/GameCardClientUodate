using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

public class FixShopLayout
{
    [MenuItem("Tools/Fix Shop Carousel Layout")]
    public static void FixLayout()
    {
        ShopUI shopUI = Object.FindFirstObjectByType<ShopUI>();
        if (shopUI == null) return;

        Canvas canvas = shopUI.GetComponent<Canvas>();
        
        // 1. Delete old ShopScrollView if it exists
        Transform oldScroll = canvas.transform.Find("ShopScrollView");
        if (oldScroll != null)
        {
            Object.DestroyImmediate(oldScroll.gameObject);
        }

        // 2. Setup PreviewPanel to be a nice centered card
        Transform previewPanel = canvas.transform.Find("PreviewPanel");
        if (previewPanel != null)
        {
            RectTransform pRect = previewPanel.GetComponent<RectTransform>();
            pRect.anchorMin = new Vector2(0.5f, 0.5f);
            pRect.anchorMax = new Vector2(0.5f, 0.5f);
            pRect.sizeDelta = new Vector2(700, 600); // Fixed size card
            pRect.anchoredPosition = new Vector2(0, -30);

            // Make background nice
            Image pImg = previewPanel.GetComponent<Image>();
            if (pImg) pImg.color = new Color(0.15f, 0.15f, 0.18f, 1f); 
            
            // Adjust Preview Frame Image (make it look like a frame, not full screen)
            Transform frameTrans = previewPanel.Find("PreviewFrameImage");
            if (frameTrans != null)
            {
                RectTransform fRect = frameTrans.GetComponent<RectTransform>();
                fRect.anchorMin = new Vector2(0.5f, 1f);
                fRect.anchorMax = new Vector2(0.5f, 1f);
                // scaled down 880x480 -> 440x240
                fRect.sizeDelta = new Vector2(440, 240);
                fRect.anchoredPosition = new Vector2(0, -180);
            }

            // Adjust Item Name Text
            Transform itemTextTrans = previewPanel.Find("ItemNameText");
            if (itemTextTrans != null)
            {
                RectTransform itRect = itemTextTrans.GetComponent<RectTransform>();
                itRect.anchorMin = new Vector2(0, 0);
                itRect.anchorMax = new Vector2(1, 0);
                itRect.sizeDelta = new Vector2(0, 50);
                itRect.anchoredPosition = new Vector2(0, 200);
            }

            // Adjust Price Text
            Transform priceTrans = previewPanel.Find("PriceText");
            if (priceTrans != null)
            {
                RectTransform prRect = priceTrans.GetComponent<RectTransform>();
                prRect.anchorMin = new Vector2(0, 0);
                prRect.anchorMax = new Vector2(1, 0);
                prRect.sizeDelta = new Vector2(0, 50);
                prRect.anchoredPosition = new Vector2(0, 140);
            }

            // Adjust Action Button
            Transform actionTrans = previewPanel.Find("ActionButton");
            if (actionTrans != null)
            {
                RectTransform aRect = actionTrans.GetComponent<RectTransform>();
                aRect.anchorMin = new Vector2(0.5f, 0f);
                aRect.anchorMax = new Vector2(0.5f, 0f);
                aRect.sizeDelta = new Vector2(250, 60);
                aRect.anchoredPosition = new Vector2(0, 60);
            }

            // Setup Left Button (placed to the left of the card)
            Transform leftTrans = previewPanel.Find("LeftButton");
            if (leftTrans != null)
            {
                RectTransform lRect = leftTrans.GetComponent<RectTransform>();
                lRect.anchorMin = new Vector2(0f, 0.5f);
                lRect.anchorMax = new Vector2(0f, 0.5f);
                lRect.sizeDelta = new Vector2(80, 80);
                lRect.anchoredPosition = new Vector2(-70, 0); // outside the card
                shopUI.leftButton = leftTrans.GetComponent<Button>();
            }

            // Setup Right Button (placed to the right of the card)
            Transform rightTrans = previewPanel.Find("RightButton");
            if (rightTrans != null)
            {
                RectTransform rRect = rightTrans.GetComponent<RectTransform>();
                rRect.anchorMin = new Vector2(1f, 0.5f);
                rRect.anchorMax = new Vector2(1f, 0.5f);
                rRect.sizeDelta = new Vector2(80, 80);
                rRect.anchoredPosition = new Vector2(70, 0); // outside the card
                shopUI.rightButton = rightTrans.GetComponent<Button>();
            }

            // Setup Page Indicator
            Transform pageTrans = previewPanel.Find("PageIndicatorText");
            if (pageTrans != null)
            {
                RectTransform pgRect = pageTrans.GetComponent<RectTransform>();
                pgRect.anchorMin = new Vector2(0.5f, 0f);
                pgRect.anchorMax = new Vector2(0.5f, 0f);
                pgRect.sizeDelta = new Vector2(200, 40);
                pgRect.anchoredPosition = new Vector2(0, -40); // below the card
                shopUI.pageIndicatorText = pageTrans.GetComponent<TextMeshProUGUI>();
            }
        }

        // Setup Toast Text properly centered
        Transform toastTrans = canvas.transform.Find("ToastText");
        if (toastTrans != null)
        {
            RectTransform tRect = toastTrans.GetComponent<RectTransform>();
            tRect.anchorMin = new Vector2(0.5f, 0.1f);
            tRect.anchorMax = new Vector2(0.5f, 0.1f);
            tRect.sizeDelta = new Vector2(600, 60);
            tRect.anchoredPosition = new Vector2(0, 0);
            
            TextMeshProUGUI toastTmp = toastTrans.GetComponent<TextMeshProUGUI>();
            if (toastTmp)
            {
                toastTmp.alignment = TextAlignmentOptions.Center;
            }
        }

        // Link the array
        string[] guids = AssetDatabase.FindAssets("t:ShopItemData", new[] { "Assets/ScriptableObjects/ShopItems" });
        shopUI.allItems = new ShopItemData[guids.Length];
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            shopUI.allItems[i] = AssetDatabase.LoadAssetAtPath<ShopItemData>(path);
        }

        EditorUtility.SetDirty(shopUI);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        Debug.Log("Carousel layout fixed and saved! (Card Style)");
    }
}
