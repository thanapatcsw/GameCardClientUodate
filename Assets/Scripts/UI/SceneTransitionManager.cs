using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneTransitionManager : MonoBehaviour
{
    [Header("ชื่อ Scene ที่จะให้โหลด")]
    public string mainMenuSceneName = "MainMenu 1";
    public string gameSceneName = "SampleScene"; // ชื่อหน้าเกมหลัก (แก้ไขให้ตรงกัน)
    public string storeSceneName = "StoreScene";
    public string rankSceneName = "RankScene";
    public string tutorialSceneName = "TutorialScene";

    // ฟังก์ชันสำหรับใช้ผูกกับปุ่ม OnClick() ใน Unity
    public void GoToMainMenu()
    {
        GameLog.Log($"กำลังโหลดหน้า: {mainMenuSceneName}");
        SceneManager.LoadScene(mainMenuSceneName);
    }

    public void GoToGame()
    {
        GameLog.Log($"กำลังโหลดหน้า: {gameSceneName}");
        SceneManager.LoadScene(gameSceneName);
    }

    public void GoToStore()
    {
        GameLog.Log($"กำลังโหลดหน้า: {storeSceneName}");
        SceneManager.LoadScene(storeSceneName);
    }

    public void GoToRank()
    {
        GameLog.Log($"กำลังโหลดหน้า: {rankSceneName}");
        SceneManager.LoadScene(rankSceneName);
    }

    public void GoToTutorial()
    {
        GameLog.Log($"กำลังโหลดหน้า: {tutorialSceneName}");
        SceneManager.LoadScene(tutorialSceneName);
    }

    public void QuitGame()
    {
        GameLog.Log("ออกจากการเล่นเกม");
        Application.Quit();
        
        // ถ้าใช้ในด่าน Editor ให้หยุดรัน
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
