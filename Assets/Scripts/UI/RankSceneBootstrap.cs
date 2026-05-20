using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Auto-populates RankScene at runtime.
/// RankScene.unity ใน project มีเพียง Main Camera (ไม่มี Canvas/RankUI/EventSystem)
/// ทำให้กดปุ่ม LEADERBOARD แล้วจอว่าง สคริปต์นี้แก้โดยสร้าง:
///   1) GameObject "Leaderboard" + RankUI  (สร้าง UI ด้วยโค้ดเอง)
///   2) EventSystem + StandaloneInputModule (จำเป็นต่อการกดปุ่ม BACK)
/// ทำงานอัตโนมัติเมื่อ Scene ชื่อ "RankScene" ถูกโหลด ไม่ต้องตั้งค่าใน Editor
/// </summary>
public static class RankSceneBootstrap
{
    private const string TargetSceneName = "RankScene";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register()
    {
        // ป้องกัน subscribe ซ้ำเมื่อกลับสู่ Play Mode ใน Editor
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != TargetSceneName) return;

        EnsureEventSystem();
        EnsureRankUI();
    }

    private static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;

        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
        Debug.Log("[RankSceneBootstrap] EventSystem created.");
    }

    private static void EnsureRankUI()
    {
        if (Object.FindFirstObjectByType<RankUI>() != null) return;

        // ต้องสร้างพร้อม RectTransform + Canvas ตั้งแต่แรก
        // เพราะ AddComponent<Canvas>() บน GameObject ที่มีแค่ Transform
        // จะไม่สลับ Transform → RectTransform ให้อัตโนมัติตอน runtime
        // ทำให้ RankUI.BuildUI() ตั้ง renderMode ไม่ได้ (Canvas เป็น null)
        var go = new GameObject(
            "Leaderboard",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));

        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        // อ้างอิงทั้งแนวตั้งและแนวนอน (Expand) → UI ขยายตามจอ ไม่ถูกตัดและไม่เล็กจิ๋ว
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.Expand;

        var rankUi = go.AddComponent<RankUI>();
        // หลังกด BACK ใน RankScene → กลับไป Main Menu
        rankUi.backSceneName = "MainMenu 1";
        Debug.Log("[RankSceneBootstrap] RankUI spawned in RankScene.");
    }
}
