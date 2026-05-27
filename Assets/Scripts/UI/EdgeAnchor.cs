using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ยึดองค์ประกอบ UI ให้ชิด "ขอบจอจริง" (เคารพ Safe Area / รอยบาก) โดยรักษาระยะห่างจากขอบ
/// ตามที่ดีไซน์ไว้ในกรอบอ้างอิง (CanvasScaler reference resolution) เอาไว้
///
/// ออกแบบให้ใช้คู่กับ CanvasScaler แบบ ScaleWithScreenSize + Match = 1 (ล็อกความสูง)
/// ซึ่งเป็นค่าที่ตั้งไว้ใน MainCanvas ของ SampleScene แล้ว
///
/// จุดเด่น: ไม่แตะ anchorMin/anchorMax เดิมของชิ้นงาน (จึงปลอดภัยกับ prefab instance)
/// แค่ปรับ anchoredPosition ทุกครั้งที่ขนาดจอ/Safe Area เปลี่ยน
///
/// วิธีใช้: ลากไปแปะบน RectTransform ของชิ้นที่อยู่ริมจอ (แผงผู้เล่น, การ์ด noble ซ้าย/ขวา,
/// ปุ่ม End Turn/Clear) แล้วเลือก Edge ใน Inspector
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class EdgeAnchor : MonoBehaviour
{
    public enum HorizontalPin { None, Left, Right }
    public enum VerticalPin { None, Bottom, Top }

    [Header("ยึดขอบไหน")]
    [Tooltip("ยึดขอบซ้าย/ขวา ตามแกน X (None = ปล่อยตามเดิม)")]
    public HorizontalPin horizontal = HorizontalPin.None;
    [Tooltip("ยึดขอบบน/ล่าง ตามแกน Y (None = ปล่อยตามเดิม)")]
    public VerticalPin vertical = VerticalPin.None;

    [Header("ตัวเลือก")]
    [Tooltip("เคารพ Safe Area (รอยบาก/ติ่งกล้อง) — ปกติเปิดไว้")]
    public bool respectSafeArea = true;
    [Tooltip("ระยะเผื่อเพิ่มจากขอบ (หน่วยตาม reference resolution)")]
    public float extraMargin = 0f;
    [Tooltip("ดึงทั้งก้อน (รวมลูกทุกตัว เช่น แถวเหรียญ/โบนัส) ให้อยู่ในจอเสมอ ถ้ามีส่วนเลยขอบ — กันลูกยื่นพ้นจอ")]
    public bool keepInsideSafeArea = true;

    private RectTransform _rt;
    private Canvas _canvas;
    private CanvasScaler _scaler;

    // ระยะห่างจากขอบที่ดีไซน์ไว้ (จับครั้งแรก จากตำแหน่ง authored ในกรอบอ้างอิง)
    private float _designInsetH; // ระยะจากขอบซ้ายหรือขวา
    private float _designInsetV; // ระยะจากขอบล่างหรือบน
    private bool _captured;

    // ค่าจอครั้งล่าสุด ใช้เช็คว่าต้องคำนวณใหม่ไหม
    private Rect _lastSafeArea;
    private Vector2Int _lastScreen;
    private int _lastChildCount = -1; // เช็คว่ามีลูกเพิ่ม/ลด (เช่น การ์ด noble ที่ spawn ตอนรันไทม์)

    private void Awake() => Init();
    private void OnEnable() { Init(); Apply(); }

    private void Init()
    {
        _rt = GetComponent<RectTransform>();
        _canvas = GetComponentInParent<Canvas>();
        if (_canvas != null) _scaler = _canvas.GetComponentInParent<CanvasScaler>();
        CaptureDesignInsets();
    }

    /// <summary>จับระยะห่างจากขอบ "กรอบอ้างอิง" ไว้ครั้งเดียว — ใช้เป็นระยะเป้าหมายตลอด</summary>
    private void CaptureDesignInsets()
    {
        if (_rt == null || _captured) return;

        Vector2 refRes = GetReferenceResolution();
        Vector2 pos = _rt.anchoredPosition;

        // สมมติ anchor เป็นจุดกึ่งกลาง (0.5,0.5) หรือกึ่งกลาง-ขอบ ตามดีไซน์ปัจจุบัน
        // ระยะจากขอบซ้าย = ครึ่งความกว้าง + ตำแหน่ง x ; จากขอบขวา = ครึ่งความกว้าง - ตำแหน่ง x
        _designInsetH = (horizontal == HorizontalPin.Left)
            ? refRes.x * 0.5f + pos.x
            : refRes.x * 0.5f - pos.x;

        _designInsetV = (vertical == VerticalPin.Bottom)
            ? refRes.y * 0.5f + pos.y
            : refRes.y * 0.5f - pos.y;

        _captured = true;
    }

    private Vector2 GetReferenceResolution()
    {
        if (_scaler != null && _scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
            return _scaler.referenceResolution;
        return new Vector2(1920f, 1080f); // ค่าเริ่มต้นของโปรเจค
    }

    private void Update()
    {
        // คำนวณใหม่เมื่อจอ/Safe Area เปลี่ยน หรือมีลูกเพิ่ม/ลด (เช่น การ์ด noble ที่ spawn ตอนรันไทม์)
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
        if (_rt == null || _canvas == null) return;
        if (horizontal == HorizontalPin.None && vertical == VerticalPin.None && !keepInsideSafeArea) return;

        if (!_captured) CaptureDesignInsets();

        _lastSafeArea = Screen.safeArea;
        _lastScreen = new Vector2Int(Screen.width, Screen.height);
        _lastChildCount = transform.childCount;

        float scale = _canvas.scaleFactor;
        if (scale <= 0f) return;

        // ขนาด canvas เชิงตรรกะ (logical units)
        float logicalW = Screen.width / scale;
        float logicalH = Screen.height / scale;

        // Safe Area แปลงเป็น logical units (ถ้าไม่เคารพ ก็ใช้เต็มจอ)
        Rect sa = respectSafeArea ? Screen.safeArea : new Rect(0, 0, Screen.width, Screen.height);
        float insetLeft = sa.xMin / scale;
        float insetRight = (Screen.width - sa.xMax) / scale;
        float insetBottom = sa.yMin / scale;
        float insetTop = (Screen.height - sa.yMax) / scale;

        Vector2 pos = _rt.anchoredPosition;

        // ---- แกน X ----
        if (horizontal == HorizontalPin.Left)
        {
            float leftEdge = -logicalW * 0.5f + insetLeft;            // ขอบซ้ายปลอดภัย (พิกัดกึ่งกลาง)
            pos.x = leftEdge + _designInsetH + extraMargin;
        }
        else if (horizontal == HorizontalPin.Right)
        {
            float rightEdge = logicalW * 0.5f - insetRight;           // ขอบขวาปลอดภัย
            pos.x = rightEdge - _designInsetH - extraMargin;
        }

        // ---- แกน Y ----
        if (vertical == VerticalPin.Bottom)
        {
            float bottomEdge = -logicalH * 0.5f + insetBottom;
            pos.y = bottomEdge + _designInsetV + extraMargin;
        }
        else if (vertical == VerticalPin.Top)
        {
            float topEdge = logicalH * 0.5f - insetTop;
            pos.y = topEdge - _designInsetV - extraMargin;
        }

        _rt.anchoredPosition = pos;

        // ดึงทั้งก้อนเข้าจอ ถ้าลูกตัวไหนยังเลยขอบ (เช่น แถวเหรียญที่ยื่นออกมา)
        if (keepInsideSafeArea)
        {
            ClampSubtreeInsideSafeArea(scale);
        }
    }

    private static readonly Vector3[] _worldCorners = new Vector3[4];

    /// <summary>
    /// วัดขอบเขตรวมของ element + ลูกทุกตัว (พิกัดหน้าจอจริง) แล้วเลื่อนกลับเข้า Safe Area ถ้ามีส่วนเลยขอบ
    /// ทำงานทีเดียวจบเพราะการเลื่อนเป็น translation ตรงๆ
    /// </summary>
    private void ClampSubtreeInsideSafeArea(float scale)
    {
        // บังคับให้ layout อัปเดตก่อนวัด (เผื่อ layout group ยังไม่จัดเสร็จ)
        Canvas.ForceUpdateCanvases();

        Camera cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        // นับเฉพาะ RectTransform ที่ active อยู่ (ช่องที่ปิดไว้ไม่ต้องวัด)
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

        Rect safe = respectSafeArea ? Screen.safeArea : new Rect(0, 0, Screen.width, Screen.height);

        float dx = 0f, dy = 0f;
        if (minX < safe.xMin) dx = safe.xMin - minX;        // เลยซ้าย → ดันขวา
        else if (maxX > safe.xMax) dx = safe.xMax - maxX;   // เลยขวา → ดันซ้าย
        if (minY < safe.yMin) dy = safe.yMin - minY;        // เลยล่าง → ดันขึ้น
        else if (maxY > safe.yMax) dy = safe.yMax - maxY;   // เลยบน → ดันลง

        if (Mathf.Abs(dx) > 0.5f || Mathf.Abs(dy) > 0.5f)
        {
            Vector2 pos = _rt.anchoredPosition;
            pos.x += dx / scale; // แปลง pixel → logical unit
            pos.y += dy / scale;
            _rt.anchoredPosition = pos;
        }
    }
}
