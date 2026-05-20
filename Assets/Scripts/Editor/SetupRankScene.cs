using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SetupRankScene
{
    [MenuItem("Tools/Setup RankScene Leaderboard")]
    public static void Setup()
    {
        // หา RankScene ใน project
        string[] guids = AssetDatabase.FindAssets("RankScene t:Scene");
        if (guids.Length == 0)
        {
            EditorUtility.DisplayDialog("Error", "ไม่พบ RankScene ใน project", "OK");
            return;
        }

        string scenePath = AssetDatabase.GUIDToAssetPath(guids[0]);
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        // เช็คว่ามี RankUI อยู่แล้วหรือไม่
        var existing = Object.FindFirstObjectByType<RankUI>();
        if (existing != null)
        {
            EditorUtility.DisplayDialog("แจ้งเตือน",
                "RankScene มี RankUI อยู่แล้ว ไม่ต้องเพิ่มอีก", "OK");
            return;
        }

        // ── สร้าง Leaderboard GameObject พร้อม Canvas stack ──
        // ต้องสร้างพร้อม RectTransform + Canvas ตั้งแต่ Constructor
        // เพราะ AddComponent<Canvas>() บน GameObject ที่มีแค่ Transform
        // จะไม่สลับ Transform → RectTransform ให้อัตโนมัติ
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
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.Expand;

        // เพิ่ม RankUI (จะ trigger OnEnable → BuildUI ทันที ด้วย [ExecuteAlways])
        var rankUi = go.AddComponent<RankUI>();
        rankUi.backSceneName = "MainMenu 1";

        // ── เพิ่ม EventSystem ถ้ายังไม่มี (จำเป็นต่อปุ่ม BACK / เริ่มแมตช์) ──
        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            var esGo = new GameObject("EventSystem",
                typeof(EventSystem),
                typeof(StandaloneInputModule));
            esGo.transform.SetSiblingIndex(0);
        }

        // Save scene
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        EditorUtility.DisplayDialog("สำเร็จ!",
            "เพิ่ม Leaderboard (RankUI) + EventSystem ใน RankScene เรียบร้อยแล้ว\n\n" +
            "หน้า Leaderboard จะแสดงใน Scene/Game view ทันที โดยไม่ต้องกด Play", "OK");

        Debug.Log("[SetupRankScene] ✅ เพิ่ม RankUI + EventSystem ใน RankScene เรียบร้อย");
    }

    // ── เครื่องมือสำหรับลบ Leaderboard ออกจาก scene (ถ้าต้องการรีเซ็ต) ──
    [MenuItem("Tools/Remove RankScene Leaderboard")]
    public static void Remove()
    {
        var existing = Object.FindFirstObjectByType<RankUI>();
        if (existing == null)
        {
            EditorUtility.DisplayDialog("แจ้งเตือน", "ไม่พบ RankUI ใน scene ปัจจุบัน", "OK");
            return;
        }

        Object.DestroyImmediate(existing.gameObject);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("สำเร็จ", "ลบ Leaderboard ออกแล้ว", "OK");
    }
}
