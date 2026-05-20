using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;
using System.Linq;

public class MainMenuSetupTool
{
    [MenuItem("Tools/Setup Main Menu Shop UI")]
    public static void SetupMainMenu()
    {
        ModeSelectUI modeSelect = Object.FindFirstObjectByType<ModeSelectUI>(FindObjectsInactive.Include);
        if (modeSelect == null)
        {
            Debug.LogError("Could not find ModeSelectUI!");
            return;
        }

        // Add Gem Text
        if (modeSelect.totalGemsText == null)
        {
            GameObject coinTextObj = modeSelect.totalCoinsText != null ? modeSelect.totalCoinsText.gameObject : null;
            if (coinTextObj != null)
            {
                GameObject gemTextObj = Object.Instantiate(coinTextObj, coinTextObj.transform.parent);
                gemTextObj.name = "TotalGemsText";
                RectTransform rect = gemTextObj.GetComponent<RectTransform>();
                rect.anchoredPosition += new Vector2(0, -60); // Shift down
                TextMeshProUGUI tmp = gemTextObj.GetComponent<TextMeshProUGUI>();
                tmp.color = Color.cyan;
                tmp.text = "Gems: 0";
                modeSelect.totalGemsText = tmp;
            }
        }

        // Add Shop Button
        Button existingBtn = null;
        if (modeSelect.mainMenuPanel != null)
        {
            existingBtn = modeSelect.mainMenuPanel.GetComponentsInChildren<Button>(true).FirstOrDefault();
            if (existingBtn != null)
            {
                bool hasShopBtn = modeSelect.mainMenuPanel.GetComponentsInChildren<Button>(true).Any(b => b.gameObject.name == "ShopButton");
                if (!hasShopBtn)
                {
                    GameObject shopBtnObj = Object.Instantiate(existingBtn.gameObject, existingBtn.transform.parent);
                    shopBtnObj.name = "ShopButton";
                    RectTransform rect = shopBtnObj.GetComponent<RectTransform>();
                    rect.anchoredPosition += new Vector2(0, -100); // Shift down
                    
                    TextMeshProUGUI tmp = shopBtnObj.GetComponentInChildren<TextMeshProUGUI>();
                    if (tmp != null) tmp.text = "ร้านค้า (Shop)";
                    
                    Button shopBtn = shopBtnObj.GetComponent<Button>();
                    shopBtn.onClick = new Button.ButtonClickedEvent();
                    UnityEditor.Events.UnityEventTools.AddPersistentListener(shopBtn.onClick, modeSelect.OnClickShop);
                }
            }
        }

        EditorUtility.SetDirty(modeSelect);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        Debug.Log("MainMenu Shop UI setup complete!");
    }
}
