using UnityEngine;
using UnityEditor;

public class CheckPlayerPanelSize
{
    [MenuItem("Tools/Check Player Panel Size")]
    public static void CheckSize()
    {
        PlayerUI ui = Object.FindFirstObjectByType<PlayerUI>();
        if (ui != null)
        {
            RectTransform rt = ui.GetComponent<RectTransform>();
            Debug.Log($"Player Panel Size: {rt.rect.width} x {rt.rect.height}");
            
            if (ui.panelBackground != null)
            {
                RectTransform bgRt = ui.panelBackground.GetComponent<RectTransform>();
                Debug.Log($"Background Size: {bgRt.rect.width} x {bgRt.rect.height}");
            }
        }
    }
}
