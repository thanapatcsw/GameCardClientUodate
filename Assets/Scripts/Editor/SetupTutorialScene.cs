using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Editor utility — ตั้งค่า TutorialScene และปุ่มใน MainMenu ให้ครบในครั้งเดียว
/// เรียกใช้ผ่าน Menu: GameCard / Setup Tutorial Scene
/// </summary>
public static class SetupTutorialScene
{
    private const string TutorialScenePath = "Assets/Scenes/TutorialScene.unity/TutorialScene.unity";
    private const string MainMenuScenePath = "Assets/Scenes/MainMenu 1.unity";
    private const string TutorialSceneName = "TutorialScene";

    // ─────────────────────────────────────────────────────────
    // STEP 1: Setup TutorialScene
    // ─────────────────────────────────────────────────────────
    [MenuItem("GameCard/Setup Tutorial Scene")]
    public static void Run()
    {
        Debug.Log("═══ [SetupTutorial] เริ่มต้นการตั้งค่า ═══");

        // เพิ่ม TutorialScene เข้า Build Settings
        AddToBuildSettings();

        // โหลด TutorialScene
        var tutScene = EditorSceneManager.OpenScene(TutorialScenePath, OpenSceneMode.Single);
        SetupTutorialSceneContents(tutScene);
        EditorSceneManager.SaveScene(tutScene, TutorialScenePath);

        // โหลด MainMenu แล้วผูกปุ่ม
        var mainScene = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);
        WireTutorialButtonInMainMenu(mainScene);
        EditorSceneManager.SaveScene(mainScene);

        // กลับไป Tutorial เพื่อตรวจสอบ
        EditorSceneManager.OpenScene(TutorialScenePath, OpenSceneMode.Single);

        Debug.Log("═══ [SetupTutorial] ✅ เสร็จสมบูรณ์! ═══");
    }

    // ─────────────────────────────────────────────────────────
    // ตั้งค่าโครงสร้างภายใน TutorialScene
    // ─────────────────────────────────────────────────────────
    private static void SetupTutorialSceneContents(Scene scene)
    {
        // หา TutorialCanvas ที่มีอยู่แล้ว
        Canvas tutCanvas = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            tutCanvas = root.GetComponent<Canvas>();
            if (tutCanvas != null) break;
        }

        if (tutCanvas == null)
        {
            Debug.LogError("[SetupTutorial] ไม่พบ Canvas ใน TutorialScene!");
            return;
        }

        // ลบ Leaderboard เก่าที่ไม่มี RectTransform ออก (ป้องกัน duplicate)
        var oldPanel = tutCanvas.transform.Find("TutorialPanel");
        if (oldPanel != null) Object.DestroyImmediate(oldPanel.gameObject);

        // สร้าง TutorialPanel ใหม่อย่างถูกต้อง (ด้วย RectTransform)
        var panelGo = new GameObject("TutorialPanel");
        panelGo.transform.SetParent(tutCanvas.transform, false);
        var panelRT = panelGo.AddComponent<RectTransform>();
        panelRT.anchorMin = Vector2.zero;
        panelRT.anchorMax = Vector2.one;
        panelRT.offsetMin = Vector2.zero;
        panelRT.offsetMax = Vector2.zero;

        // ใส่ TutorialUI
        var tutUI = panelGo.AddComponent<TutorialUI>();

        // Auto-load font
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/FC Quantum [Non-commercial] SDF.asset");
        if (font == null)
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/LayijiMahaniyom-Bao-1.asset");
        if (font != null) tutUI.customFont = font;

        tutUI.mainMenuSceneName = "MainMenu 1";

        // ── CanvasSparkleSpawner (วิ้งๆ เหมือน MainMenu) ──
        var sparkleGo = new GameObject("BackgroundParticles");
        sparkleGo.transform.SetParent(panelGo.transform, false);
        var sparkleRT = sparkleGo.AddComponent<RectTransform>();
        sparkleRT.anchorMin = Vector2.zero;
        sparkleRT.anchorMax = Vector2.one;
        sparkleRT.offsetMin = Vector2.zero;
        sparkleRT.offsetMax = Vector2.zero;
        // เพิ่ม CanvasSparkleSpawner ถ้ามี
        var spawnerType = System.Type.GetType("CanvasSparkleSpawner, Assembly-CSharp");
        if (spawnerType != null)
            sparkleGo.AddComponent(spawnerType);

        // ── ตั้งค่า Camera ──
        foreach (var root in scene.GetRootGameObjects())
        {
            var cam = root.GetComponent<Camera>();
            if (cam != null)
            {
                cam.backgroundColor = new Color(0.04f, 0.07f, 0.12f, 1f);
                cam.clearFlags = CameraClearFlags.SolidColor;
            }
        }

        Debug.Log($"[SetupTutorial] ✅ สร้าง TutorialPanel พร้อม TutorialUI สำเร็จ (font: {(font != null ? font.name : "none")})");
    }

    // ─────────────────────────────────────────────────────────
    // ผูกปุ่ม TutorialButton ใน MainMenu
    // ─────────────────────────────────────────────────────────
    private static void WireTutorialButtonInMainMenu(Scene mainScene)
    {
        // หา SceneTransitionManager บน MainCanvas
        SceneTransitionManager stm = null;
        foreach (var root in mainScene.GetRootGameObjects())
        {
            stm = root.GetComponentInChildren<SceneTransitionManager>(true);
            if (stm != null) break;
        }

        if (stm == null)
        {
            Debug.LogError("[SetupTutorial] ไม่พบ SceneTransitionManager ใน MainMenu!");
            return;
        }

        // หา TutorialButton
        Button tutBtn = null;
        foreach (var root in mainScene.GetRootGameObjects())
        {
            var buttons = root.GetComponentsInChildren<Button>(true);
            foreach (var b in buttons)
            {
                if (b.gameObject.name == "TutorialButton")
                {
                    tutBtn = b;
                    break;
                }
            }
            if (tutBtn != null) break;
        }

        if (tutBtn == null)
        {
            Debug.LogError("[SetupTutorial] ไม่พบ TutorialButton ใน MainMenu!");
            return;
        }

        // ล้าง onClick เก่า แล้วผูกใหม่
        tutBtn.onClick.RemoveAllListeners();
        UnityEditor.Events.UnityEventTools.AddPersistentListener(
            tutBtn.onClick,
            stm.GoToTutorial
        );

        // อัปเดตสี/label ของปุ่ม
        var btnImage = tutBtn.GetComponent<Image>();
        if (btnImage != null)
            btnImage.color = new Color(0.15f, 0.45f, 0.65f, 0.9f);

        var lbl = tutBtn.GetComponentInChildren<TMP_Text>();
        if (lbl != null)
            lbl.text = "📖 วิธีเล่น";

        EditorUtility.SetDirty(tutBtn.gameObject);
        Debug.Log($"[SetupTutorial] ✅ ผูก TutorialButton → {stm.name}.GoToTutorial() สำเร็จ");
    }

    // ─────────────────────────────────────────────────────────
    // เพิ่ม TutorialScene เข้า Build Settings
    // ─────────────────────────────────────────────────────────
    private static void AddToBuildSettings()
    {
        var scenes = EditorBuildSettings.scenes;

        // ตรวจว่ามีอยู่แล้วไหม
        foreach (var s in scenes)
        {
            if (s.path.Contains(TutorialSceneName))
            {
                Debug.Log("[SetupTutorial] TutorialScene มีใน Build Settings อยู่แล้ว");
                return;
            }
        }

        var newList = new EditorBuildSettingsScene[scenes.Length + 1];
        scenes.CopyTo(newList, 0);
        newList[scenes.Length] = new EditorBuildSettingsScene(TutorialScenePath, true);
        EditorBuildSettings.scenes = newList;
        Debug.Log("[SetupTutorial] ✅ เพิ่ม TutorialScene เข้า Build Settings สำเร็จ");
    }
}
