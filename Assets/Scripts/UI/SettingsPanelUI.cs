using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using StartupCity.Audio;

/// <summary>
/// หน้าต่างตั้งค่า (Settings) — สร้าง UI ทั้งหมดด้วยโค้ด ไม่ต้องลากวางใน Inspector
///   • ปรับระดับเสียงเพลงพื้นหลัง (BGM)
///   • ปรับระดับเสียงเอฟเฟกต์ (SFX)
///   • ปุ่มกลับเมนูหลัก
///
/// วิธีใช้:
///   1. แปะสคริปต์นี้กับ GameObject ใดก็ได้ในฉาก (เช่น GameController หรือสร้าง empty ใหม่ชื่อ "SettingsPanel")
///   2. ที่ปุ่มเฟือง (เดิมคือปุ่ม X) ไปที่ OnClick() แล้วลาก GameObject นั้นเข้ามา เลือกฟังก์ชัน SettingsPanelUI.OpenSettings()
/// </summary>
public class SettingsPanelUI : MonoBehaviour
{
    [Tooltip("ชื่อฉากเมนูหลักที่จะกลับไปเมื่อกดออก")]
    [SerializeField] private string mainMenuSceneName = "MainMenu 1";

    // ชื่อ GameObject ของปุ่มมุมขวาบน (เดิมคือปุ่มกากบาท) ที่จะแปลงเป็นปุ่มตั้งค่า
    private const string LeaveButtonName = "LeaveGame";

    private GameObject panelRoot;   // รากของหน้าต่าง (ใช้เปิด/ปิด)

    // ==========================================================
    //  Auto-bootstrap — ทำงานเองทุกครั้งที่โหลดฉาก ไม่ต้องตั้งค่าใน Editor
    //  จะค้นหาปุ่ม "LeaveGame" แล้วเปลี่ยนให้กดเปิดหน้าตั้งค่า + เปลี่ยนรูปเป็นเฟือง
    // ==========================================================
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoBootstrap()
    {
        HookLeaveButton(SceneManager.GetActiveScene());
        SceneManager.sceneLoaded += (scene, mode) => HookLeaveButton(scene);
    }

    private static void HookLeaveButton(Scene scene)
    {
        GameObject btnGO = FindInScene(scene, LeaveButtonName);
        if (btnGO == null) return;                       // ฉากนี้ไม่มีปุ่ม -> ข้าม

        Button btn = btnGO.GetComponent<Button>();
        if (btn == null) return;

        // ต้องมี SettingsPanelUI หนึ่งตัวในฉากเพื่อเป็นเจ้าของหน้าต่าง
        SettingsPanelUI panel = FindFirstObjectByType<SettingsPanelUI>();
        if (panel == null)
        {
            GameObject go = new GameObject("SettingsPanelUI (auto)");
            panel = go.AddComponent<SettingsPanelUI>();
        }

        // เปลี่ยนการทำงานของปุ่ม: ลบ OnClick เดิม (LeaveToMainMenu) แล้วให้เปิดหน้าตั้งค่าแทน
        btn.onClick = new Button.ButtonClickedEvent();
        btn.onClick.AddListener(panel.OpenSettings);

        // เปลี่ยนรูปกากบาท -> ไอคอนเฟือง
        Image img = btn.GetComponent<Image>();
        if (img != null)
        {
            img.sprite = GetGearSprite();
            img.color = new Color(0.9f, 0.92f, 0.98f, 1f);
        }
    }

    /// <summary>ค้นหา GameObject ตามชื่อภายในฉากที่ระบุ (รวมที่ถูกปิดอยู่)</summary>
    private static GameObject FindInScene(Scene scene, string objectName)
    {
        if (!scene.IsValid() || !scene.isLoaded) return null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == objectName) return t.gameObject;
        }
        return null;
    }

    // ==========================================================
    //  วาดไอคอนเฟืองด้วยโค้ด (ไม่ต้องใช้ไฟล์รูป) — ใช้สีขาวแล้ว tint ด้วย Image.color
    // ==========================================================
    private static Sprite _gearSprite;
    private static Sprite GetGearSprite()
    {
        if (_gearSprite != null) return _gearSprite;

        const int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;

        Color clear = new Color(1f, 1f, 1f, 0f);
        Vector2 c = new Vector2(size / 2f, size / 2f);

        const float teeth = 8f;
        float outerR = size * 0.46f;   // ปลายฟันเฟือง
        float rootR  = size * 0.36f;   // ร่องระหว่างฟัน
        float bodyR  = size * 0.40f;   // ตัวเฟือง (วงตัน)
        float holeR  = size * 0.16f;   // รูตรงกลาง

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - c.x, dy = y - c.y;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx);

                // สลับความยาวรัศมีตามมุม -> เกิดเป็นฟันเฟือง
                float tooth = Mathf.Cos(ang * teeth) > 0f ? outerR : rootR;
                float edge = Mathf.Max(bodyR, tooth);

                bool solid = r <= edge && r >= holeR;
                tex.SetPixel(x, y, solid ? Color.white : clear);
            }
        }
        tex.Apply();

        _gearSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return _gearSprite;
    }

    private void Awake()
    {
        BuildUI();
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    // ==========================================================
    //  Public API — ลากไปใส่ OnClick ของปุ่มได้เลย
    // ==========================================================
    public void OpenSettings()
    {
        if (panelRoot == null) BuildUI();
        panelRoot.SetActive(true);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayPopupOpen();
    }

    public void CloseSettings()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayPopupClose();
    }

    public void BackToMainMenu()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayButtonClick();

        // ใช้ logic เดิมของ GameController ถ้ามี (ล้าง state เกม + ตัดการเชื่อมต่อ network)
        GameController gc = FindFirstObjectByType<GameController>();
        if (gc != null) { gc.LeaveToMainMenu(); return; }

        SceneManager.LoadScene(mainMenuSceneName);
    }

    // ==========================================================
    //  สร้างหน้าตา UI ทั้งหมดด้วยโค้ด
    // ==========================================================
    private void BuildUI()
    {
        TMP_FontAsset font = TMP_Settings.defaultFontAsset;

        // --- Canvas ซ้อนทับด้านบนสุด ---
        panelRoot = new GameObject("SettingsPanel_Canvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        panelRoot.transform.SetParent(transform, false);

        Canvas canvas = panelRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;   // ให้อยู่บนสุดเสมอ

        CanvasScaler scaler = panelRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // --- พื้นหลังมืดโปร่งแสง (กันคลิกทะลุไปโดนของข้างหลัง) ---
        Image dim = CreateImage(panelRoot.transform, "Dim", new Color(0f, 0f, 0f, 0.7f));
        RectTransform dimRT = dim.rectTransform;
        dimRT.anchorMin = Vector2.zero;
        dimRT.anchorMax = Vector2.one;
        dimRT.offsetMin = Vector2.zero;
        dimRT.offsetMax = Vector2.zero;

        // --- กล่องหน้าต่างตรงกลาง ---
        Image panel = CreateImage(dim.transform, "Window", new Color(0.10f, 0.12f, 0.18f, 0.98f));
        RectTransform panelRT = panel.rectTransform;
        panelRT.anchorMin = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.sizeDelta = new Vector2(560f, 460f);
        panelRT.anchoredPosition = Vector2.zero;

        // --- หัวเรื่อง ---
        CreateLabel(panel.transform, "Settings", new Vector2(0f, 180f), new Vector2(500f, 60f), 44, font, TextAlignmentOptions.Center);

        // --- แถวเสียงเพลง (Music / BGM) ---
        CreateLabel(panel.transform, "Music", new Vector2(0f, 100f), new Vector2(460f, 40f), 30, font, TextAlignmentOptions.Left);
        float bgmInit = AudioManager.Instance != null ? AudioManager.Instance.BGMVolume : 1f;
        CreateSlider(panel.transform, new Vector2(0f, 60f), bgmInit, (v) =>
        {
            if (AudioManager.Instance != null) AudioManager.Instance.SetBGMVolume(v);
        });

        // --- แถวเสียงเอฟเฟกต์ (SFX) ---
        CreateLabel(panel.transform, "Sound Effects", new Vector2(0f, 0f), new Vector2(460f, 40f), 30, font, TextAlignmentOptions.Left);
        float sfxInit = AudioManager.Instance != null ? AudioManager.Instance.SFXVolume : 1f;
        CreateSlider(panel.transform, new Vector2(0f, -40f), sfxInit, (v) =>
        {
            if (AudioManager.Instance != null) AudioManager.Instance.SetSFXVolume(v);
        });

        // --- ปุ่มกลับเมนูหลัก ---
        CreateButton(panel.transform, "Back to Main Menu", new Vector2(0f, -140f), new Vector2(300f, 64f),
            new Color(0.75f, 0.2f, 0.25f, 1f), font, BackToMainMenu);

        // --- ปุ่มปิด (X) มุมขวาบนของกล่อง ---
        CreateButton(panel.transform, "X", new Vector2(245f, 200f), new Vector2(48f, 48f),
            new Color(0.3f, 0.3f, 0.35f, 1f), font, CloseSettings);
    }

    // ==========================================================
    //  Helper: สร้าง Image / Label / Button / Slider
    // ==========================================================
    private Image CreateImage(Transform parent, string name, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    private TextMeshProUGUI CreateLabel(Transform parent, string text, Vector2 pos, Vector2 size,
        float fontSize, TMP_FontAsset font, TextAlignmentOptions align)
    {
        GameObject go = new GameObject("Label_" + text, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.text = text;
        label.fontSize = fontSize;
        label.color = Color.white;
        label.alignment = align;
        RectTransform rt = label.rectTransform;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return label;
    }

    private Button CreateButton(Transform parent, string label, Vector2 pos, Vector2 size,
        Color bgColor, TMP_FontAsset font, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject("Button_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = bgColor;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        Button btn = go.GetComponent<Button>();
        btn.onClick.AddListener(onClick);

        TextMeshProUGUI txt = CreateLabel(go.transform, label, Vector2.zero, size,
            label.Length <= 2 ? 30 : 26, font, TextAlignmentOptions.Center);
        // ให้ข้อความเต็มปุ่ม
        RectTransform txtRT = txt.rectTransform;
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = Vector2.zero;
        txtRT.offsetMax = Vector2.zero;

        return btn;
    }

    private Slider CreateSlider(Transform parent, Vector2 pos, float initialValue,
        UnityEngine.Events.UnityAction<float> onChanged)
    {
        GameObject sliderGO = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
        sliderGO.transform.SetParent(parent, false);
        RectTransform sRT = sliderGO.GetComponent<RectTransform>();
        sRT.anchoredPosition = pos;
        sRT.sizeDelta = new Vector2(460f, 28f);

        Slider slider = sliderGO.GetComponent<Slider>();

        // Background
        Image bg = CreateImage(sliderGO.transform, "Background", new Color(0.15f, 0.15f, 0.22f, 1f));
        RectTransform bgRT = bg.rectTransform;
        bgRT.anchorMin = new Vector2(0f, 0.25f);
        bgRT.anchorMax = new Vector2(1f, 0.75f);
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;

        // Fill Area > Fill
        GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderGO.transform, false);
        RectTransform faRT = fillArea.GetComponent<RectTransform>();
        faRT.anchorMin = new Vector2(0f, 0.25f);
        faRT.anchorMax = new Vector2(1f, 0.75f);
        faRT.offsetMin = new Vector2(10f, 0f);
        faRT.offsetMax = new Vector2(-20f, 0f);

        Image fill = CreateImage(fillArea.transform, "Fill", new Color(0.2f, 0.7f, 1f, 1f));
        RectTransform fillRT = fill.rectTransform;
        fillRT.sizeDelta = new Vector2(10f, 0f);

        // Handle Slide Area > Handle
        GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(sliderGO.transform, false);
        RectTransform haRT = handleArea.GetComponent<RectTransform>();
        haRT.anchorMin = new Vector2(0f, 0f);
        haRT.anchorMax = new Vector2(1f, 1f);
        haRT.offsetMin = new Vector2(10f, 0f);
        haRT.offsetMax = new Vector2(-10f, 0f);

        Image handle = CreateImage(handleArea.transform, "Handle", Color.white);
        RectTransform handleRT = handle.rectTransform;
        handleRT.sizeDelta = new Vector2(24f, 24f);

        // ผูกเข้ากับ Slider
        slider.fillRect = fillRT;
        slider.handleRect = handleRT;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.SetValueWithoutNotify(initialValue);
        slider.onValueChanged.AddListener(onChanged);

        return slider;
    }
}
