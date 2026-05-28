using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ยึดองค์ประกอบ UI ให้เคารพ Safe Area (รอยบาก/รูกล้อง)
/// โดยจะดัน UI กลับเข้ามาในจออัตโนมัติหากมีส่วนใดล้นออกไปนอก Safe Area
/// ปลอดภัยกับทุก Anchor ไม่ทำให้ UI กระโดดไปกลางจอ
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class EdgeAnchor : MonoBehaviour
{
    [Tooltip("เคารพ Safe Area (รอยบาก/ติ่งกล้อง) — ปกติเปิดไว้")]
    public bool respectSafeArea = true;

    [Tooltip("ระยะเผื่อเพิ่มจาก Safe Area (พิกเซลหน้าจอ)")]
    public float extraMargin = 0f;

    private RectTransform _rt;
    private Canvas _canvas;
    
    private Rect _lastSafeArea;
    private Vector2Int _lastScreen;
    private int _lastChildCount = -1;
    
    private Vector2 _originalAnchoredPos;
    private bool _hasOriginalPos = false;

    private void Awake() => Init();
    private void OnEnable() { Init(); Apply(); }

    private void Init()
    {
        _rt = GetComponent<RectTransform>();
        _canvas = GetComponentInParent<Canvas>();
        
        if (!_hasOriginalPos && _rt != null)
        {
            _originalAnchoredPos = _rt.anchoredPosition;
            _hasOriginalPos = true;
        }
    }

    private void Update()
    {
        if (Screen.safeArea != _lastSafeArea ||
            Screen.width != _lastScreen.x || Screen.height != _lastScreen.y ||
            transform.childCount != _lastChildCount)
        {
            Apply();
        }
    }

    public void Apply()
    {
        if (_rt == null) Init();
        if (_rt == null || _canvas == null || !respectSafeArea) return;

        _lastSafeArea = Screen.safeArea;
        _lastScreen = new Vector2Int(Screen.width, Screen.height);
        _lastChildCount = transform.childCount;

        float scale = _canvas.scaleFactor;
        if (scale <= 0f) return;

        // Reset กลับไปจุดเดิมก่อน (ตามที่ตั้งไว้ใน Editor) เพื่อคำนวณใหม่
        _rt.anchoredPosition = _originalAnchoredPos;

        ClampSubtreeInsideSafeArea(scale);
    }

    private static readonly Vector3[] _worldCorners = new Vector3[4];

    /// <summary>
    /// วัดขอบเขตรวมของ element + ลูกทุกตัว (พิกัดหน้าจอจริง) แล้วเลื่อนกลับเข้า Safe Area ถ้ามีส่วนเลยขอบ
    /// </summary>
    private void ClampSubtreeInsideSafeArea(float scale)
    {
        // บังคับให้ layout อัปเดตก่อนวัด (เผื่อ layout group ยังไม่จัดเสร็จ)
        Canvas.ForceUpdateCanvases();

        Camera cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        // นับเฉพาะ RectTransform ที่ active อยู่
        RectTransform[] subtree = GetComponentsInChildren<RectTransform>();
        foreach (RectTransform child in subtree)
        {
            child.GetWorldCorners(_worldCorners);
            for (int i = 0; i < 4; i++)
            {
                Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(cam, _worldCorners[i]);
                if (screenPoint.x < minX) minX = screenPoint.x;
                if (screenPoint.y < minY) minY = screenPoint.y;
                if (screenPoint.x > maxX) maxX = screenPoint.x;
                if (screenPoint.y > maxY) maxY = screenPoint.y;
            }
        }

        Rect safe = Screen.safeArea;
        
        // เพิ่ม extra margin เข้าไปใน safe area (ทำให้พื้นที่ปลอดภัยเล็กลง)
        safe.xMin += extraMargin;
        safe.yMin += extraMargin;
        safe.xMax -= extraMargin;
        safe.yMax -= extraMargin;

        float dx = 0f, dy = 0f;
        if (minX < safe.xMin) dx = safe.xMin - minX;        // เลยซ้าย → ดันขวา
        else if (maxX > safe.xMax) dx = safe.xMax - maxX;   // เลยขวา → ดันซ้าย
        if (minY < safe.yMin) dy = safe.yMin - minY;        // เลยล่าง → ดันขึ้น
        else if (maxY > safe.yMax) dy = safe.yMax - maxY;   // เลยบน → ดันลง

        if (Mathf.Abs(dx) > 0.5f || Mathf.Abs(dy) > 0.5f)
        {
            Vector2 pos = _rt.anchoredPosition;
            pos.x += dx / scale; // แปลง pixelหน้าจอ → logical unit ของ Canvas
            pos.y += dy / scale;
            _rt.anchoredPosition = pos;
        }
    }
}
