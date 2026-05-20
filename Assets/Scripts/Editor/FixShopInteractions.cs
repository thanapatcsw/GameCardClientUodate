using UnityEngine;
using UnityEditor;
using UnityEngine.UI;

public class FixShopInteractions
{
    [MenuItem("Tools/Fix Shop Interactions")]
    public static void Fix()
    {
        ShopUI shopUI = Object.FindFirstObjectByType<ShopUI>();
        if (shopUI == null) return;

        // 1. Disable raycastTarget on all Text and Image components that shouldn't block clicks
        Graphic[] graphics = shopUI.GetComponentsInChildren<Graphic>(true);
        foreach (Graphic g in graphics)
        {
            g.raycastTarget = false;
        }

        // 2. Enable raycastTarget ONLY on interactive elements
        if (shopUI.backButton != null)
        {
            Image bg = shopUI.backButton.GetComponent<Image>();
            if (bg) bg.raycastTarget = true;
            shopUI.backButton.targetGraphic = bg;
        }

        if (shopUI.leftButton != null)
        {
            Image bg = shopUI.leftButton.GetComponent<Image>();
            if (bg) bg.raycastTarget = true;
            shopUI.leftButton.targetGraphic = bg;
        }

        if (shopUI.rightButton != null)
        {
            Image bg = shopUI.rightButton.GetComponent<Image>();
            if (bg) bg.raycastTarget = true;
            shopUI.rightButton.targetGraphic = bg;
        }

        if (shopUI.actionButton != null)
        {
            Image bg = shopUI.actionButton.GetComponent<Image>();
            if (bg) bg.raycastTarget = true;
            shopUI.actionButton.targetGraphic = bg;
        }

        EditorUtility.SetDirty(shopUI);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        Debug.Log("Shop interactions fixed (simplified)!");
    }
}
