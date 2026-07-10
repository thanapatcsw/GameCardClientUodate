using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// ============================================================
// ReconnectManager — Popup "กลับเข้าเกม" เด้งกลางจอบนหน้าเมนู (Reconnect/ข้อ 1)
//
//   ตอนเข้าเมนู (หลัง login): เช็คว่ามี match_sessions ที่ status='active' ค้างอยู่ไหม
//     → ถ้ามี เด้ง popup กลางจอถามว่าจะกลับเข้าเล่นต่อไหม
//   กด "กลับเข้าเกม": rejoin ห้อง Photon เดิม (session_name) ด้วย allowSessionCreation=false
//     → ConnectionToken (uid) ทำให้ Fusion คืน PlayerId/seat เดิม
//     → ถ้าคู่แข่งยังอยู่ในห้อง: state ปัจจุบันถูกดึงผ่าน RequestFullState ที่มีอยู่แล้ว
//   กด "ภายหลัง": ปิด popup เล่นเมนูปกติ
//
//   วิธีติดตั้ง (ใน Unity Editor):
//     1) แปะ component นี้บน GameObject ใดก็ได้ในฉากเมนู (เช่นเดียวกับ LobbyUI)
//        → popup สร้างเอง ไม่ต้องผูก UI ใดๆ
//     2) (ออปชัน) ถ้าอยากใช้ปุ่มที่วาดเอง: ลาก Button ลง reconnectButton + TMP_Text ลง statusText
//
//   *** ต้องตั้ง PlayerTTL ใน Photon Dashboard (เช่น 120 วิ) ให้ slot ค้างพอตอนหลุด ***
// ============================================================
public class ReconnectManager : MonoBehaviour
{
    [Header("(ออปชัน) ปล่อยว่างได้ — ระบบจะสร้าง popup ให้เอง")]
    [SerializeField] private Button reconnectButton;
    [SerializeField] private TMP_Text statusText;

    // true ระหว่างกำลัง reconnect (join กลางเกม) → ให้ GameController ข้ามการเริ่ม "ควิซรอบแรก"
    //   (อ่านครั้งเดียวด้วย ConsumeReconnectFlag แล้วเคลียร์เอง)
    private static bool _isReconnecting;
    public static bool ConsumeReconnectFlag()
    {
        bool v = _isReconnecting;
        _isReconnecting = false;
        return v;
    }

    private ReconnectService.ActiveSession _session;

    // popup ที่สร้างเองตอนรันไทม์
    private GameObject _popupRoot;
    private TMP_Text _popupMessage;
    private Button _popupConfirm;
    private Button _popupCancel;
    private TMP_FontAsset _thaiFont;   // ฟอนต์ไทยที่ดึงมาจากฉากเมนู (กันตัวอักษรไทยเป็นกล่อง)

    // ชื่อฉากเมนูที่จะให้ popup ทำงาน (ฉากที่เปิดหลัง login)
    private const string MenuSceneName = "MainMenu 1";

    // bootstrap ตัวเองเมื่อฉากเมนูโหลด — ไม่ต้องแปะ component ใน Editor
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) =>
        {
            if (scene.name == MenuSceneName) SpawnIfNeeded();
        };
        // เผื่อกด Play ค้างอยู่ที่ฉากเมนูอยู่แล้ว (sceneLoaded จะไม่ fire ซ้ำ)
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == MenuSceneName)
            SpawnIfNeeded();
    }

    private static void SpawnIfNeeded()
    {
        if (FindFirstObjectByType<ReconnectManager>() != null) return; // กันสร้างซ้ำ/มีที่แปะเองอยู่แล้ว
        var go = new GameObject("ReconnectManager(Auto)");
        go.AddComponent<ReconnectManager>();
        GameLog.Log("[Reconnect] auto-spawned ReconnectManager ในฉากเมนู");
    }

    private void Start()
    {
        if (reconnectButton != null)
        {
            reconnectButton.gameObject.SetActive(false);
            reconnectButton.onClick.AddListener(DoReconnect);
        }
        _ = CheckAsync();
    }

    private async Task CheckAsync()
    {
        try { _session = await ReconnectService.CheckAsync(); }
        catch (Exception e) { Debug.LogWarning($"[Reconnect] check error: {e.Message}"); _session = null; }

        bool has = _session != null;
        if (reconnectButton != null) reconnectButton.gameObject.SetActive(has);

        if (has)
        {
            GameLog.Log($"[Reconnect] พบเกมค้าง room={_session.roomCode} photon={_session.sessionName}");
            if (statusText != null) statusText.text = "มีเกมค้างอยู่ — กลับเข้าเล่นต่อได้";
            ShowPopup("ต้องการกลับเข้าเกมไหม?");
        }
        else
        {
            GameLog.Log("[Reconnect] ไม่มีเกมค้าง");
        }
    }

    // เรียกจากปุ่ม popup "กลับเข้าเกม" (หรือ reconnectButton ที่ wire ใน Inspector)
    public void DoReconnect()
    {
        if (_session == null || FusionManager.Instance == null) return;

        _isReconnecting = true;                                           // join กลางเกม → ข้ามควิซรอบแรก
        PlayerPrefs.SetString("MatchmakingRoomId", _session.roomCode);    // ให้ logic ในเกมรู้ room เดิม
        PlayerPrefs.Save();

        if (reconnectButton != null) reconnectButton.interactable = false;
        if (statusText != null) statusText.text = "กำลังกลับเข้าเกม...";
        SetPopupBusy("กำลังกลับเข้าเกม...");

        // allowSessionCreation=false → ห้องต้องมีจริง (ไม่งั้น fail) — กันสร้างห้องเปล่าใหม่
        FusionManager.Instance.StartGameCoroutineWithResult(
            Fusion.GameMode.Shared,
            _session.sessionName,
            OnRejoinDone,
            FusionManager.Instance.gameSceneName,
            allowSessionCreation: false);
    }

    private void OnRejoinDone(bool ok)
    {
        if (ok)
        {
            GameLog.Log("[Reconnect] rejoin สำเร็จ → กำลังโหลดเกม (state ดึงผ่าน RequestFullState)");
            ClosePopup();
            return;
        }
        // ห้องปิด/หมด PlayerTTL แล้ว → ไม่ต้องถามอีก: ปิด popup + mark session เป็น abandoned ใน DB
        _isReconnecting = false;   // ไม่ได้เข้าเกม → เคลียร์ flag กันค้างไปข้ามควิซเกมปกติครั้งหน้า
        GameLog.Log("[Reconnect] rejoin ล้มเหลว (ห้องปิด/หมด PlayerTTL) → ปิด session ไม่ถามซ้ำ");
        if (statusText != null) { statusText.text = ""; reconnectButton?.gameObject.SetActive(false); }
        AbandonSession();
        ClosePopup();
    }

    // ปิด session ที่ rejoin ไม่ได้แล้ว เพื่อให้ login ครั้งหน้าไม่เด้ง popup ถามอีก (best-effort)
    private async void AbandonSession()
    {
        if (_session == null || string.IsNullOrEmpty(_session.sessionId)) return;
        await MatchSessionService.SetFinalAsync(_session.sessionId, null, "abandoned");
        _session = null;
    }

    // ============================================================
    //  Popup ที่สร้างเองด้วยโค้ด (ไม่ต้องวาด UI ใน Editor)
    // ============================================================

    private void ShowPopup(string message)
    {
        if (_popupRoot != null) { Destroy(_popupRoot); _popupRoot = null; }
        EnsureEventSystem();
        _thaiFont = ResolveThaiFont();

        // ── Canvas overlay (อยู่บนสุด) ──
        _popupRoot = new GameObject("ReconnectPopup",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = _popupRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;   // ทับ UI เมนูทั้งหมด
        var scaler = _popupRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // ── พื้นหลังมืด (เต็มจอ + บล็อกคลิกข้างหลัง) ──
        var dim = CreateUI("Dim", _popupRoot.transform);
        Stretch(dim.GetComponent<RectTransform>());
        var dimImg = dim.AddComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0f, 0.65f);

        // ── กล่อง popup กลางจอ ──
        var panel = CreateUI("Panel", dim.transform);
        var panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(760, 420);
        panelRt.anchoredPosition = Vector2.zero;
        var panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0.12f, 0.14f, 0.18f, 0.98f);

        // ── หัวข้อ ──
        var title = CreateText("Title", panel.transform, "กลับเข้าเกม", 52, FontStyles.Bold);
        var titleRt = title.rectTransform;
        titleRt.anchorMin = new Vector2(0, 1); titleRt.anchorMax = new Vector2(1, 1);
        titleRt.pivot = new Vector2(0.5f, 1);
        titleRt.anchoredPosition = new Vector2(0, -40);
        titleRt.sizeDelta = new Vector2(-80, 80);
        title.alignment = TextAlignmentOptions.Center;

        // ── ข้อความ ──
        _popupMessage = CreateText("Message", panel.transform, message, 38, FontStyles.Normal);
        var msgRt = _popupMessage.rectTransform;
        msgRt.anchorMin = new Vector2(0, 0); msgRt.anchorMax = new Vector2(1, 1);
        msgRt.offsetMin = new Vector2(50, 130);   // เว้นที่ปุ่มด้านล่าง
        msgRt.offsetMax = new Vector2(-50, -130);  // เว้นหัวข้อด้านบน
        _popupMessage.alignment = TextAlignmentOptions.Center;

        // ── ปุ่ม "กลับเข้าเกม" (ขวา) ──
        _popupConfirm = CreateButton("Confirm", panel.transform, "กลับเข้าเกม",
            new Color(0.16f, 0.55f, 0.30f, 1f), DoReconnect);
        var cRt = _popupConfirm.GetComponent<RectTransform>();
        cRt.anchorMin = cRt.anchorMax = new Vector2(0.5f, 0); cRt.pivot = new Vector2(0.5f, 0);
        cRt.sizeDelta = new Vector2(320, 90);
        cRt.anchoredPosition = new Vector2(180, 40);

        // ── ปุ่ม "ไม่กลับเข้าเกม" (ซ้าย) ──
        _popupCancel = CreateButton("Cancel", panel.transform, "ไม่กลับเข้าเกม",
            new Color(0.35f, 0.35f, 0.40f, 1f), OnCancelClicked);
        var xRt = _popupCancel.GetComponent<RectTransform>();
        xRt.anchorMin = xRt.anchorMax = new Vector2(0.5f, 0); xRt.pivot = new Vector2(0.5f, 0);
        xRt.sizeDelta = new Vector2(320, 90);
        xRt.anchoredPosition = new Vector2(-180, 40);
    }

    private void SetPopupBusy(string msg)
    {
        if (_popupRoot == null) return;
        if (_popupMessage != null) _popupMessage.text = msg;
        if (_popupConfirm != null) _popupConfirm.interactable = false;
        if (_popupCancel != null) _popupCancel.interactable = false;
    }

    private void ClosePopup()
    {
        if (_popupRoot != null) { Destroy(_popupRoot); _popupRoot = null; }
    }

    private void OnCancelClicked()
    {
        GameLog.Log("[Reconnect] ผู้เล่นปฏิเสธการกลับเข้าเกม → ปิด session ไม่ถามซ้ำ");
        AbandonSession();
        ClosePopup();
    }

    // ── UI factory helpers ──
    private static GameObject CreateUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    private TMP_Text CreateText(string name, Transform parent, string text, float size, FontStyles style)
    {
        var go = CreateUI(name, parent);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (_thaiFont != null) t.font = _thaiFont;    // ใช้ฟอนต์ไทยจากฉากเมนู กันตัวอักษรเป็นกล่อง
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = Color.white;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.alignment = TextAlignmentOptions.Center;
        return t;
    }

    private Button CreateButton(string name, Transform parent, string label, Color bg, UnityEngine.Events.UnityAction onClick)
    {
        var go = CreateUI(name, parent);
        var img = go.AddComponent<Image>();
        img.color = bg;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        if (onClick != null) btn.onClick.AddListener(onClick);

        var label_t = CreateText("Label", go.transform, label, 36, FontStyles.Bold);
        Stretch(label_t.rectTransform);
        label_t.alignment = TextAlignmentOptions.Center;
        return btn;
    }

    // หาฟอนต์ไทยจาก TMP_Text ที่มีอยู่ในฉาก (เมนูแสดงไทยได้อยู่แล้ว) → reuse ฟอนต์ตัวนั้น
    private TMP_FontAsset ResolveThaiFont()
    {
        var texts = FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var t in texts)
        {
            if (t == null || t.font == null) continue;
            if (_popupRoot != null && t.transform.IsChildOf(_popupRoot.transform)) continue; // ข้าม popup ตัวเอง
            // เลือกตัวที่รองรับสระไทย (มี glyph ของ "ก") ถ้าไม่เจอค่อยใช้ตัวแรก
            if (t.font.HasCharacter('ก')) return t.font;
        }
        foreach (var t in texts)
            if (t != null && t.font != null) return t.font;   // fallback: ฟอนต์อะไรก็ได้ในฉาก
        return TMP_Settings.defaultFontAsset;
    }

    // ปุ่มจะคลิกไม่ได้ถ้าไม่มี EventSystem ในฉาก — สร้างให้ถ้ายังไม่มี
    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        DontDestroyOnLoad(es);
    }
}
