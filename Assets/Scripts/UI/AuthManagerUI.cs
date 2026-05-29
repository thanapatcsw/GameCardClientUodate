using System;
using System.IO;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using StartupCity.Audio;

/// <summary>
/// Login UI Manager — แก้บัค "พิมพ์ไม่ได้หลังล็อกอินผิด"
/// Strategy: ไม่แตะ interactable เลย, ใช้แค่ blocker canvas
/// </summary>
public class AuthManagerUI : MonoBehaviour
{
    [Header("UI Panels")]
    public GameObject loginPanel;

    [Header("Input Fields")]
    public TMP_InputField emailInput;
    public TMP_InputField passwordInput;

    [Header("Status Text")]
    public TextMeshProUGUI statusText;

    [Header("Buttons")]
    public UnityEngine.UI.Button loginButton;

    [Header("Scene")]
    public string nextSceneName = "MainMenu 1";

    [Header("Register Page")]
    public string registerUrl = "";

    // ───── state ─────
    private bool _isProcessing = false;
    private Canvas _blockerCanvas;        // Canvas บน top เพื่อดัก input ทั้งหมด
    // [FIX-ANDROID] capture main thread context ใน Awake เพื่อรับประกันว่า UI call
    // จะ resume บน main thread เสมอ ไม่ว่า async continuation จะ resume บน thread ไหน
    private System.Threading.SynchronizationContext _mainThreadContext;

    // ───── lifecycle ─────

    private void Awake()
    {
        // [FIX-ANDROID] จับ Main Thread Context ตั้งแต่ตอน Awake (ซึ่งรับประกันว่าอยู่บน main thread)
        // เพื่อใช้ post UI operations กลับมาจาก async continuation ที่อาจ resume บน background thread
        _mainThreadContext = System.Threading.SynchronizationContext.Current;

        // ป้องกัน StatusText บัง Input
        if (statusText != null)
            statusText.raycastTarget = false;
    }

    private void Start()
    {
        ShowLoginPanel();

        if (loginButton != null)
        {
            loginButton.onClick.RemoveAllListeners();
            loginButton.onClick.AddListener(OnLoginButtonClicked);
        }

        GameLog.Log("[Auth] Ready");
    }

    // ───── public UI callbacks ─────

    public void ShowLoginPanel()
    {
        if (loginPanel != null) loginPanel.SetActive(true);
        SetStatus("กรุณาเข้าสู่ระบบ", Color.white);
    }

    public void OnLoginButtonClicked()
    {
        AudioManager.Instance?.PlayButtonClick();
        if (_isProcessing)
        {
            Debug.LogWarning("[Auth] Blocked — still processing");
            return;
        }

        string email = emailInput != null ? emailInput.text.Trim() : "";
        string pass  = passwordInput != null ? passwordInput.text : "";

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(pass))
        {
            SetStatus("กรุณากรอกข้อมูลให้ครบถ้วน", Color.red);
            AudioManager.Instance?.PlayWarningText();
            return;
        }

        _isProcessing = true;
        SetStatus("กำลังตรวจสอบ...", Color.yellow);
        SetBlocker(true);

        StartCoroutine(LoginCoroutine(email, pass));
    }

    public void OnRegisterButtonClicked()
    {
        AudioManager.Instance?.PlayButtonClick();
        GameLog.Log("[Auth] OnRegisterButtonClicked called!");

        // ถ้าเป็น http/https → เปิด browser ตรงๆ
        if (!string.IsNullOrWhiteSpace(registerUrl) &&
            (registerUrl.StartsWith("http://") || registerUrl.StartsWith("https://")))
        {
            GameLog.Log("[Auth] Opening web URL: " + registerUrl);
            try { Application.OpenURL(registerUrl); }
            catch (Exception ex) { Debug.LogError("[Auth] OpenURL error: " + ex.Message); }
            return;
        }

        // fallback → local StreamingAssets/Web/index.html
        string fallback = System.IO.Path.Combine(
            Application.streamingAssetsPath, "Web", "index.html");

        GameLog.Log("[Auth] Opening local file: " + fallback);

        if (System.IO.File.Exists(fallback))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fallback,
                    UseShellExecute = true
                });
            }
            catch (Exception ex) { Debug.LogError("[Auth] Process.Start error: " + ex.Message); }
        }
        else
        {
            Debug.LogError("[Auth] ไม่พบ StreamingAssets/Web/index.html และไม่ได้ตั้ง registerUrl ใน Inspector");
        }
    }

    private System.Collections.IEnumerator LoginCoroutine(string email, string password)
    {
        bool goToScene = false;

        if (SupabaseManager.Instance == null)
        {
            SetStatus("Error: SupabaseManager missing", Color.red);
            UnlockUI();
            yield break;
        }

        // เริ่ม Task แบบ async
        var loginTask = SupabaseManager.Instance.SignInUser(email, password);
        
        // รอจนกว่า Task จะเสร็จ หรือหมดเวลา (Timeout) ใน Coroutine (รับประกันว่าอยู่บน Main Thread)
        float timeout = 15f;
        float elapsed = 0f;
        while (!loginTask.IsCompleted && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (!loginTask.IsCompleted)
        {
            SetStatus("การเชื่อมต่อล่าช้า กรุณาลองใหม่", Color.red);
            UnlockUI();
            yield break;
        }

        if (loginTask.IsFaulted || loginTask.IsCanceled)
        {
            SetStatus("เกิดข้อผิดพลาด", Color.red);
            Debug.LogError($"[Auth] Exception: {loginTask.Exception}");
            UnlockUI();
            yield break;
        }

        // ได้ผลลัพธ์แล้ว
        var (ok, errMsg) = loginTask.Result;

        if (ok)
        {
            SetStatus("สำเร็จ!", Color.green);
            AudioManager.Instance?.PlayCorrectAnswer();
            goToScene = true;
        }
        else
        {
            string reason = errMsg?.Contains("Invalid login credentials") == true
                ? "อีเมลหรือรหัสผ่านผิด"
                : errMsg;
            SetStatus("ล็อกอินไม่สำเร็จ: " + reason, Color.red);
            AudioManager.Instance?.PlayWarningText();
        }

        UnlockUI();

        if (goToScene)
        {
            yield return new WaitForSeconds(0.6f);
            UnityEngine.SceneManagement.SceneManager.LoadScene(nextSceneName);
        }
    }

    private void UnlockUI()
    {
        _isProcessing = false;
        SetBlocker(false);

        if (passwordInput != null)
            passwordInput.text = "";

        GameLog.Log("[Auth] >>> UI UNLOCKED <<<");
    }


    // ───── blocker — ใช้ Canvas sortingOrder สูงสุดดัก input แทน ─────
    //
    //  ทำไมใช้ Canvas แยกแทน Image ลูกใน LoginPanel?
    //  → Canvas.sortingOrder สูงกว่าทุกอย่าง + overrideSorting
    //  → ไม่มีปัญหาเรื่อง parent/child hierarchy ของ RectTransform
    //  → Destroy จาก finally บน main thread ได้ชัวร์

    private void SetBlocker(bool active)
    {
        if (active)
        {
            if (_blockerCanvas != null) return;

            var go = new GameObject("__LoginBlocker__",
                typeof(Canvas),
                typeof(UnityEngine.UI.GraphicRaycaster),
                typeof(UnityEngine.UI.Image));

            DontDestroyOnLoad(go);   // ป้องกัน scene reload ทำลาย blocker ก่อนเวลา

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode       = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder     = 9999;   // อยู่บนสุดของทุกอย่าง

            var img = go.GetComponent<UnityEngine.UI.Image>();
            img.color           = new Color(0, 0, 0, 0);   // โปร่งใส แต่ดัก raycast
            img.raycastTarget   = true;

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin  = Vector2.zero;
            rect.anchorMax  = Vector2.one;
            rect.sizeDelta  = Vector2.zero;
            rect.offsetMin  = Vector2.zero;
            rect.offsetMax  = Vector2.zero;

            _blockerCanvas = canvas;
            GameLog.Log("[Auth] Blocker ON");
        }
        else
        {
            if (_blockerCanvas != null)
            {
                Destroy(_blockerCanvas.gameObject);
                _blockerCanvas = null;
                GameLog.Log("[Auth] Blocker OFF");
            }
        }
    }

    // ───── helpers ─────

    private void SetStatus(string msg, Color color)
    {
        if (statusText == null) return;
        statusText.text  = msg;
        statusText.color = color;
    }
}
