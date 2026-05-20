using UnityEngine;
using UnityEditor;

public class DailyQuizEditorTools
{
    [MenuItem("Tools/Reset Daily Quiz Status")]
    public static void ResetDailyQuiz()
    {
        PlayerPrefs.DeleteKey("LastDailyQuizDate");
        PlayerPrefs.Save();
        Debug.Log("<color=green><b>Daily Quiz Status Reset!</b></color>");
        EditorUtility.DisplayDialog("Daily Quiz Reset", "Reset successful!", "OK");
    }

    [MenuItem("Tools/Clear All PlayerPrefs (Nuclear Reset)")]
    public static void ClearAll()
    {
        if (EditorUtility.DisplayDialog("Nuclear Reset", "Clear ALL PlayerPrefs?", "Yes", "No"))
        {
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
            Debug.Log("<color=red><b>All Data Cleared!</b></color>");
        }
    }
}
