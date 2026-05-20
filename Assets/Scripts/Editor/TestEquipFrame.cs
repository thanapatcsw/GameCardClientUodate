using UnityEngine;
using UnityEditor;

public class TestEquipFrame
{
    [MenuItem("Tools/Test Equip Purple Frame")]
    public static void EquipPurple()
    {
        PlayerPrefs.SetString("EquippedFrame", "frame_purple");
        PlayerPrefs.Save();
        Debug.Log("Equipped frame_purple for testing.");
    }

    [MenuItem("Tools/Reset Equip Frame")]
    public static void ResetFrame()
    {
        PlayerPrefs.SetString("EquippedFrame", "frame_default");
        PlayerPrefs.Save();
        Debug.Log("Reset to frame_default.");
    }
}
