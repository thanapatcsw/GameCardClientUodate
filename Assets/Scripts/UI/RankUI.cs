using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Leaderboard UI — โทนเขียวมินต์ + ดาร์กเนวี
/// สร้าง UI ด้วยโค้ดทั้งหมด ไม่ต้องใช้ prefab
/// [ExecuteAlways] ทำให้ Editor มอง preview ใน Scene/Game view โดยไม่ต้อง Play
/// </summary>
[ExecuteAlways]
public class RankUI : MonoBehaviour
{
    [Header("Navigation")]
    public string backSceneName = "MainMenu 1";
    public Button backButton;

    [Header("Fonts")]
    public TMP_FontAsset customFont;

    private bool _isOverlay = false;
    private string _myUsername = "";

    // ─── colour palette (mint/teal theme) ───
    private static Color Hex(string h)
    {
        ColorUtility.TryParseHtmlString(h, out var c); return c;
    }
    private static readonly Color C_BG       = Hex("#1a1d23");   // outer bg (เทาเข้ม)
    private static readonly Color C_CARD     = Hex("#0d1424");   // panel หลัก
    private static readonly Color C_CARD_IN  = Hex("#131a2c");   // panel ย่อย/player card
    private static readonly Color C_BORDER   = Hex("#1f2842");   // เส้นขอบ
    private static readonly Color C_MINT     = Hex("#5eedb3");   // เขียวมินต์ (accent หลัก)
    private static readonly Color C_MINT_SOFT= Hex("#3aa881");   // เขียวเข้ม
    private static readonly Color C_TEXT     = Hex("#e6ecff");   // text ขาวอมฟ้า
    private static readonly Color C_TEXT_DIM = Hex("#7a8aa6");   // text เทา
    private static readonly Color C_GREEN    = Hex("#44ff77");
    private static readonly Color C_RED      = Hex("#ff4455");
    private static readonly Color C_GOLD     = Hex("#ffd700");
    private static readonly Color C_SILVER   = Hex("#c0c8d8");
    private static readonly Color C_BRONZE   = Hex("#cd7f32");
    private static readonly Color C_ROW_ME   = Hex("#1c2d4a");
    private static readonly Color C_ROW_A    = Hex("#141c30");
    private static readonly Color C_ROW_B    = Hex("#10182a");

    // ─── refs ───
    private GameObject _root;
    private Transform  _rowHolder;
    private GameObject _emptyState;
    private GameObject _scrollGo;
    private TextMeshProUGUI _loadingTmp;

    // player card refs
    private TextMeshProUGUI _pcName;
    private TextMeshProUGUI _pcTier;
    private Image           _pcTierBg;
    private TextMeshProUGUI _pcStatus;
    private TextMeshProUGUI _pcMmr;
    private TextMeshProUGUI _pcWL;
    private TextMeshProUGUI _pcWinPct;
    private TextMeshProUGUI _pcAvatar;
    private Image           _pcAvatarBg;

    // ════════════════════════════════════════════
    // OnEnable: สร้าง UI ทั้งในโหมด Edit และ Play (ด้วย [ExecuteAlways])
    // → ทำให้ Scene/Game view โชว์ Leaderboard ก่อนกด Play
    // ════════════════════════════════════════════
    private void OnEnable()
    {
        // เคลียร์ children เดิม (ป้องกัน UI ซ้อนเมื่อ OnEnable ถูกเรียกหลายครั้ง เช่น compile script หรือเปิด scene)
        // แก้ไขให้ลบเฉพาะชิ้นส่วน UI ที่โค้ดนี้สร้างขึ้น ("BG" และ "Card") 
        // เพื่อป้องกันการลบ Object อื่นๆ ที่ผู้ใช้ลากไปวางเอง เช่น Sparkles Effect
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i).gameObject;
            if (child.name == "BG" || child.name == "Card")
            {
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }

        _isOverlay  = name == "LeaderboardOverlay";
        _myUsername = SupabaseManager.Instance != null
            ? SupabaseManager.Instance.GetCurrentUsername()
            : PlayerPrefs.GetString("Username", "");
        if (string.IsNullOrEmpty(_myUsername)) _myUsername = "Player 1";

#if UNITY_EDITOR
        if (customFont == null || customFont.name == "LayijiMahaniyom-Bao-1")
        {
            customFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/FC Quantum [Non-commercial] SDF.asset");
            if (customFont == null)
            {
                customFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/LayijiMahaniyom-Bao-1.asset");
            }
        }
#endif

        BuildUI();
        UpdateMyCard(null);

        // Editor preview: ซ่อน loading, โชว์ empty state เพื่อ preview สถานะ "ยังไม่มีข้อมูล"
        if (!Application.isPlaying)
        {
            if (_loadingTmp != null) _loadingTmp.gameObject.SetActive(false);
            if (_scrollGo != null)   _scrollGo.SetActive(false);
            if (_emptyState != null) _emptyState.SetActive(true);
        }
    }

    // Start: โหลดข้อมูลจริงจาก Supabase — เฉพาะตอน Play เท่านั้น
    private async void Start()
    {
        if (!Application.isPlaying) return; // Editor preview = ไม่ต้องโหลดข้อมูล

        _loadingTmp.gameObject.SetActive(true);
        _emptyState.SetActive(false);
        _scrollGo.SetActive(false);

        var board = await PlayerDataService.GetLeaderboardAsync(50);

        if (this == null || _loadingTmp == null) return; // ออกจาก scene แล้ว
        _loadingTmp.gameObject.SetActive(false);
        PopulateRows(board);
        UpdateMyCard(board);
    }

    // ════════════════════════════════════════════
    // BUILD
    // ════════════════════════════════════════════
    private void BuildUI()
    {
        // ── canvas ──
        var canvas = GetComponent<Canvas>() ?? gameObject.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;

        if (!GetComponent<CanvasScaler>())
        {
            var cs = gameObject.AddComponent<CanvasScaler>();
            cs.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1080, 1920);
            cs.matchWidthOrHeight  = 0.5f;
        }
        if (!GetComponent<GraphicRaycaster>())
            gameObject.AddComponent<GraphicRaycaster>();

        _root = gameObject;

        // ── full-screen background ──
        MakeImage("BG", transform, C_BG, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // ── main card (centre, มี margin รอบ) ──
        var card = MakePanel("Card", transform, C_CARD,
            new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.95f));

        var cardOutline = card.gameObject.AddComponent<Outline>();
        cardOutline.effectColor    = C_BORDER;
        cardOutline.effectDistance = new Vector2(2, 2);

        // ── 1) HEADER (LEADERBOARD + Season) ──
        BuildHeader(card.transform);

        // separator line ใต้ header
        MakeImage("HdrLine", card.transform, C_BORDER,
            new Vector2(0.03f, 0.905f), new Vector2(0.97f, 0.905f),
            new Vector2(0, -1), new Vector2(0, 1));

        // ── 2) PLAYER CARD (own profile) ──
        BuildPlayerCard(card.transform);

        // ── 3) COLUMN HEADER ──
        BuildColHeader(card.transform);

        // ── 4) CONTENT (rows / empty state / loading) ──
        BuildContent(card.transform);

        // separator line เหนือ footer
        MakeImage("FtrLine", card.transform, C_BORDER,
            new Vector2(0.03f, 0.105f), new Vector2(0.97f, 0.105f),
            new Vector2(0, -1), new Vector2(0, 1));

        // ── 5) FOOTER (BACK + update info) ──
        BuildFooter(card.transform);
    }

    // ════════════════════════════════════════════
    // 1) HEADER
    // ════════════════════════════════════════════
    private void BuildHeader(Transform parent)
    {
        var hdr = MakePanel("Header", parent, new Color(0, 0, 0, 0),
            new Vector2(0, 0.91f), new Vector2(1, 1f));

        // trophy icon (vector แทน emoji เพราะฟอนต์ไทยไม่มี emoji glyph)
        BuildTrophyIcon(hdr.transform,
            new Vector2(0.045f, 0.5f), new Vector2(0.045f, 0.5f),
            new Vector2(-26, -26), new Vector2(26, 26));

        // title
        MakeText("Title", hdr.transform, "LEADERBOARD",
            64, C_MINT, FontStyles.Bold, TextAlignmentOptions.MidlineLeft,
            new Vector2(0.105f, 0), new Vector2(0.7f, 1),
            Vector2.zero, Vector2.zero);

        // Season pill (ขวาบน) — ขยายให้กว้างพอใส่ "Season 1" ในบรรทัดเดียว
        var pill = MakePanel("SeasonPill", hdr.transform, Hex("#0a1f2e"),
            new Vector2(0.79f, 0.20f), new Vector2(0.975f, 0.80f));
        var pillOl = pill.gameObject.AddComponent<Outline>();
        pillOl.effectColor    = new Color(C_MINT.r, C_MINT.g, C_MINT.b, 0.35f);
        pillOl.effectDistance = new Vector2(1, 1);

        MakeText("SeasonLbl", pill.transform, "Season 1",
            26, C_MINT, FontStyles.Bold, TextAlignmentOptions.Center,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
    }

    // วาด trophy icon ด้วย UI Image ง่ายๆ (ไม่ใช้ emoji)
    private void BuildTrophyIcon(Transform parent,
        Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
    {
        var icon = CreateGO("TrophyIcon", typeof(RectTransform));
        icon.transform.SetParent(parent, false);
        var rt = icon.GetComponent<RectTransform>();
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = offMin; rt.offsetMax = offMax;

        // ถ้วยรางวัล (ใช้ TMP glyph "🏆" จะ render ไม่ได้กับฟอนต์ไทย
        //  → ใช้ตัวอักษร Y + cup shape จาก rect แทน)
        // cup body
        var cup = MakeImage("Cup", icon.transform, C_MINT,
            new Vector2(0.15f, 0.35f), new Vector2(0.85f, 0.95f),
            Vector2.zero, Vector2.zero);
        // base/foot
        MakeImage("Foot", icon.transform, C_MINT,
            new Vector2(0.25f, 0f), new Vector2(0.75f, 0.18f),
            Vector2.zero, Vector2.zero);
        // stem
        MakeImage("Stem", icon.transform, C_MINT,
            new Vector2(0.42f, 0.15f), new Vector2(0.58f, 0.4f),
            Vector2.zero, Vector2.zero);
        // handles
        MakeImage("HandleL", icon.transform, new Color(0, 0, 0, 0),
            new Vector2(0.0f, 0.5f), new Vector2(0.18f, 0.85f),
            Vector2.zero, Vector2.zero).color = C_MINT_SOFT;
        MakeImage("HandleR", icon.transform, C_MINT_SOFT,
            new Vector2(0.82f, 0.5f), new Vector2(1f, 0.85f),
            Vector2.zero, Vector2.zero);
    }

    // ════════════════════════════════════════════
    // 2) PLAYER CARD (own profile)
    // ════════════════════════════════════════════
    private void BuildPlayerCard(Transform parent)
    {
        // card frame
        var card = MakePanel("PlayerCard", parent, C_CARD_IN,
            new Vector2(0.04f, 0.755f), new Vector2(0.96f, 0.89f));
        var ol = card.gameObject.AddComponent<Outline>();
        ol.effectColor    = C_BORDER;
        ol.effectDistance = new Vector2(1, 1);

        // avatar (square teal)
        var avatarBg = MakePanel("AvatarBg", card.transform, Hex("#1a3344"),
            new Vector2(0.025f, 0.15f), new Vector2(0.125f, 0.85f));
        var avOl = avatarBg.gameObject.AddComponent<Outline>();
        avOl.effectColor    = new Color(C_MINT.r, C_MINT.g, C_MINT.b, 0.45f);
        avOl.effectDistance = new Vector2(1, 1);
        _pcAvatarBg = avatarBg;
        _pcAvatar = MakeText("AvatarLbl", avatarBg.transform, "P1",
            42, C_MINT, FontStyles.Bold, TextAlignmentOptions.Center,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // name row (Player + tier badge)
        _pcName = MakeText("Name", card.transform, "Player 1",
            46, C_TEXT, FontStyles.Bold, TextAlignmentOptions.MidlineLeft,
            new Vector2(0.145f, 0.55f), new Vector2(0.45f, 0.95f),
            Vector2.zero, Vector2.zero);

        // tier pill (BRONZE)
        _pcTierBg = MakePanel("TierPill", card.transform, Hex("#3a2a1a"),
            new Vector2(0.32f, 0.60f), new Vector2(0.48f, 0.90f));
        var tierOl = _pcTierBg.gameObject.AddComponent<Outline>();
        tierOl.effectColor    = new Color(C_BRONZE.r, C_BRONZE.g, C_BRONZE.b, 0.6f);
        tierOl.effectDistance = new Vector2(1, 1);
        _pcTier = MakeText("TierLbl", _pcTierBg.transform, "BRONZE",
            26, C_BRONZE, FontStyles.Bold, TextAlignmentOptions.Center,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // status text (subtitle)
        _pcStatus = MakeText("Status", card.transform,
            "อันดับยังไม่จัด · เล่นแมตช์แรกเพื่อจัดอันดับ",
            28, C_TEXT_DIM, FontStyles.Normal, TextAlignmentOptions.MidlineLeft,
            new Vector2(0.145f, 0.08f), new Vector2(0.66f, 0.55f),
            Vector2.zero, Vector2.zero);
        _pcStatus.textWrappingMode = TextWrappingModes.Normal;

        // stats columns (right side)
        BuildStatBlock(card.transform, "MMR",  "950", out _pcMmr,
            new Vector2(0.66f, 0.10f), new Vector2(0.79f, 0.90f), C_TEXT);
        BuildStatBlock(card.transform, "W / L", "0 / 0", out _pcWL,
            new Vector2(0.79f, 0.10f), new Vector2(0.90f, 0.90f), C_TEXT);
        BuildStatBlock(card.transform, "Win%", "—", out _pcWinPct,
            new Vector2(0.90f, 0.10f), new Vector2(0.99f, 0.90f), C_TEXT);
    }

    private void BuildStatBlock(Transform parent, string label, string value,
        out TextMeshProUGUI valTmp, Vector2 aMin, Vector2 aMax, Color valCol)
    {
        var holder = CreateGO("Stat_" + label, typeof(RectTransform));
        holder.transform.SetParent(parent, false);
        var rt = holder.GetComponent<RectTransform>();
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        MakeText("Lbl", holder.transform, label,
            26, C_TEXT_DIM, FontStyles.Normal, TextAlignmentOptions.Center,
            new Vector2(0, 0.55f), new Vector2(1, 1f),
            Vector2.zero, Vector2.zero);
        valTmp = MakeText("Val", holder.transform, value,
            38, valCol, FontStyles.Bold, TextAlignmentOptions.Center,
            new Vector2(0, 0f), new Vector2(1, 0.6f),
            Vector2.zero, Vector2.zero);
    }

    // ════════════════════════════════════════════
    // 3) COLUMN HEADER (no heavy bg, just labels)
    // ════════════════════════════════════════════
    // อันดับ | ชื่อผู้เล่น | MMR | W | L | Win%
    private static readonly float[] ColX = { 0.035f, 0.16f, 0.50f, 0.62f, 0.72f, 0.83f };
    private static readonly string[] ColLabels = { "อันดับ", "ชื่อผู้เล่น", "MMR", "W", "L", "WIN%" };

    private void BuildColHeader(Transform parent)
    {
        var hdr = MakePanel("ColHdr", parent, new Color(0, 0, 0, 0),
            new Vector2(0, 0.69f), new Vector2(1, 0.75f));

        for (int i = 0; i < ColLabels.Length; i++)
        {
            float xMax = i + 1 < ColX.Length ? ColX[i + 1] : 0.99f;
            MakeText(ColLabels[i], hdr.transform, ColLabels[i], 28,
                C_TEXT_DIM, FontStyles.Bold,
                i == 1 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center,
                new Vector2(ColX[i], 0), new Vector2(xMax, 1),
                new Vector2(i == 1 ? 6 : 0, 0), Vector2.zero);
        }
    }

    // ════════════════════════════════════════════
    // 4) CONTENT
    // ════════════════════════════════════════════
    private void BuildContent(Transform parent)
    {
        // scroll viewport
        var scrollArea = MakePanel("ScrollArea", parent, new Color(0, 0, 0, 0),
            new Vector2(0.025f, 0.115f), new Vector2(0.975f, 0.685f));
        scrollArea.gameObject.AddComponent<RectMask2D>();
        _scrollGo = scrollArea.gameObject;

        // content
        var contentGo = CreateGO("Content", typeof(RectTransform));
        contentGo.transform.SetParent(scrollArea.transform, false);
        var contentRt = contentGo.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0, 1);
        contentRt.anchorMax = new Vector2(1, 1);
        contentRt.pivot     = new Vector2(0.5f, 1);
        contentRt.sizeDelta = Vector2.zero;
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.offsetMax = new Vector2(-18, contentRt.offsetMax.y);

        var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
        vlg.spacing            = 6;
        vlg.childControlHeight = false;
        vlg.childControlWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth  = true;
        vlg.padding            = new RectOffset(6, 6, 6, 6);

        var csf = contentGo.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scrollbar = BuildScrollbar(parent);

        var sr = scrollArea.gameObject.AddComponent<ScrollRect>();
        sr.content    = contentRt;
        sr.horizontal = false;
        sr.vertical   = true;
        sr.scrollSensitivity = 60;
        sr.movementType      = ScrollRect.MovementType.Elastic;
        sr.elasticity        = 0.12f;
        sr.inertia           = true;
        sr.decelerationRate  = 0.135f;
        sr.viewport          = scrollArea.GetComponent<RectTransform>();
        sr.verticalScrollbar = scrollbar;
        sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        _rowHolder = contentRt;

        // loading label (overlay บน scroll area)
        _loadingTmp = MakeText("Loading", parent, "กำลังโหลดข้อมูล...",
            38, C_MINT, FontStyles.Bold, TextAlignmentOptions.Center,
            new Vector2(0.1f, 0.35f), new Vector2(0.9f, 0.55f),
            Vector2.zero, Vector2.zero);

        // empty state
        BuildEmptyState(parent);
    }

    // ════════════════════════════════════════════
    // EMPTY STATE
    // ════════════════════════════════════════════
    private void BuildEmptyState(Transform parent)
    {
        var es = CreateGO("EmptyState", typeof(RectTransform));
        es.transform.SetParent(parent, false);
        var esRt = es.GetComponent<RectTransform>();
        esRt.anchorMin = new Vector2(0.05f, 0.13f);
        esRt.anchorMax = new Vector2(0.95f, 0.67f);
        esRt.offsetMin = esRt.offsetMax = Vector2.zero;
        _emptyState = es;

        // medal circle (icon container)
        var circle = MakePanel("MedalCircle", es.transform, Hex("#1a3344"),
            new Vector2(0.42f, 0.62f), new Vector2(0.58f, 0.92f));
        var cOl = circle.gameObject.AddComponent<Outline>();
        cOl.effectColor    = new Color(C_MINT.r, C_MINT.g, C_MINT.b, 0.45f);
        cOl.effectDistance = new Vector2(1, 1);

        // medal glyph — ใช้ shape ของ Image แทน emoji
        BuildMedalIcon(circle.transform);

        // title
        MakeText("EmptyTitle", es.transform, "ยังไม่มีข้อมูล Leaderboard",
            42, C_TEXT, FontStyles.Bold, TextAlignmentOptions.Center,
            new Vector2(0.05f, 0.46f), new Vector2(0.95f, 0.62f),
            Vector2.zero, Vector2.zero);

        // subtitle
        var sub = MakeText("EmptySub", es.transform,
            "เริ่มแมตช์แรกเพื่อปลดล็อกการจัดอันดับ และไต่ระดับขึ้นไปสู่ตำแหน่งแชมป์!",
            28, C_TEXT_DIM, FontStyles.Normal, TextAlignmentOptions.Center,
            new Vector2(0.05f, 0.28f), new Vector2(0.95f, 0.46f),
            Vector2.zero, Vector2.zero);
        sub.textWrappingMode = TextWrappingModes.Normal;

        // CTA button "เริ่มแมตช์ >"
        BuildStartMatchBtn(es.transform);
    }

    private void BuildMedalIcon(Transform parent)
    {
        // ใช้ shape combine แทน emoji
        // ribbon (สามเหลี่ยมล่าง)
        MakeImage("RibbonL", parent, C_MINT,
            new Vector2(0.36f, 0.08f), new Vector2(0.48f, 0.45f),
            Vector2.zero, Vector2.zero);
        MakeImage("RibbonR", parent, C_MINT,
            new Vector2(0.52f, 0.08f), new Vector2(0.64f, 0.45f),
            Vector2.zero, Vector2.zero);
        // medal disc
        MakeImage("Disc", parent, C_MINT_SOFT,
            new Vector2(0.30f, 0.40f), new Vector2(0.70f, 0.85f),
            Vector2.zero, Vector2.zero);
        MakeImage("DiscIn", parent, C_MINT,
            new Vector2(0.36f, 0.46f), new Vector2(0.64f, 0.79f),
            Vector2.zero, Vector2.zero);
    }

    private void BuildStartMatchBtn(Transform parent)
    {
        var go = CreateGO("StartMatchBtn",
            typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.32f, 0.10f);
        rt.anchorMax = new Vector2(0.68f, 0.26f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        go.GetComponent<Image>().color = C_MINT;

        MakeText("Lbl", go.transform, "เริ่มแมตช์  ›",
            34, Hex("#0a1424"), FontStyles.Bold,
            TextAlignmentOptions.Center,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var btn = go.GetComponent<Button>();
        var colors = btn.colors;
        colors.normalColor      = Color.white;
        colors.highlightedColor = new Color(0.92f, 1f, 0.96f, 1f);
        colors.pressedColor     = new Color(0.7f, 0.95f, 0.8f, 1f);
        btn.colors = colors;

        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() =>
        {
            // กลับไป Main Menu เพื่อเริ่มแมตช์
            if (_isOverlay) Destroy(gameObject);
            else SceneManager.LoadScene(backSceneName);
        });
    }

    // ════════════════════════════════════════════
    // POPULATE ROWS
    // ════════════════════════════════════════════
    private void PopulateRows(List<PlayerProfile> list)
    {
        foreach (Transform c in _rowHolder) Destroy(c.gameObject);

        if (list == null || list.Count == 0)
        {
            _scrollGo.SetActive(false);
            _emptyState.SetActive(true);
            return;
        }

        _emptyState.SetActive(false);
        _scrollGo.SetActive(true);

        for (int i = 0; i < list.Count; i++)
            BuildRow(i, list[i]);
    }

    private void BuildRow(int i, PlayerProfile p)
    {
        bool isMe = string.Equals(p.Username, _myUsername,
                        System.StringComparison.OrdinalIgnoreCase);
        int total = p.Wins + p.Losses;
        float wr  = total > 0 ? p.Wins * 100f / total : 0f;
        string tier = MmrCalculator.GetRankName(p.Mmr);
        Color tierCol = TierColor(tier);

        var rowGo = CreateGO($"Row{i}", typeof(RectTransform), typeof(Image));
        rowGo.transform.SetParent(_rowHolder, false);
        var rowRt = rowGo.GetComponent<RectTransform>();
        rowRt.sizeDelta = new Vector2(0, 76);
        rowGo.GetComponent<Image>().color = isMe ? C_ROW_ME : (i % 2 == 0 ? C_ROW_A : C_ROW_B);

        if (isMe)
        {
            var ol = rowGo.AddComponent<Outline>();
            ol.effectColor    = C_MINT;
            ol.effectDistance = new Vector2(1, 1);
        }

        // tier strip ซ้ายสุด
        var strip = CreateGO("Strip", typeof(RectTransform), typeof(Image));
        strip.transform.SetParent(rowGo.transform, false);
        var stripRt = strip.GetComponent<RectTransform>();
        stripRt.anchorMin = Vector2.zero;
        stripRt.anchorMax = new Vector2(0, 1);
        stripRt.sizeDelta = new Vector2(4, 0);
        strip.GetComponent<Image>().color = tierCol;

        var ax = ColX;
        string numStr = $"{i + 1}";
        Color numCol  = i == 0 ? C_GOLD : i == 1 ? C_SILVER : i == 2 ? C_BRONZE
                                         : C_TEXT_DIM;
        Color wrCol   = wr >= 60 ? C_GREEN : wr >= 40 ? C_SILVER : C_RED;

        string wrStr  = total > 0 ? $"{wr:F0}%" : "—";
        string[] vals = { numStr, p.Username, $"{p.Mmr}", $"{p.Wins}", $"{p.Losses}", wrStr };
        Color[]  cols = { numCol, isMe ? C_MINT : C_TEXT, tierCol, C_GREEN, C_RED,
                          total > 0 ? wrCol : C_TEXT_DIM };
        FontStyles[] styles = {
            FontStyles.Bold,
            isMe ? FontStyles.Bold : FontStyles.Normal,
            FontStyles.Bold,
            FontStyles.Normal, FontStyles.Normal, FontStyles.Normal
        };

        var rowTr = rowGo.transform;
        for (int j = 0; j < vals.Length; j++)
        {
            float xMax = j + 1 < ax.Length ? ax[j + 1] : 0.99f;
            float sz = j == 0 ? 34 : j == 1 ? 32 : 30;
            MakeText($"C{j}", rowTr, vals[j], sz,
                cols[j], styles[j],
                j == 1 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center,
                new Vector2(ax[j], 0), new Vector2(xMax, 1),
                new Vector2(j == 1 ? 8 : 0, 2), new Vector2(0, -2));
        }
    }

    private void UpdateMyCard(List<PlayerProfile> board)
    {
        // ค้นหาตัวเองใน leaderboard ก่อน (server data = source of truth)
        PlayerProfile myEntry = null;
        int pos = -1;
        if (board != null)
        {
            pos = board.FindIndex(p =>
                string.Equals(p.Username, _myUsername,
                    System.StringComparison.OrdinalIgnoreCase));
            if (pos >= 0) myEntry = board[pos];
        }

        // ใช้ข้อมูลจาก server ก่อน → local cache → PlayerPrefs (fallback chain)
        var profile = PlayerDataService.LocalProfile;
        int myMmr  = myEntry?.Mmr    ?? profile?.Mmr    ?? PlayerPrefs.GetInt("MMR", 1000);
        int wins   = myEntry?.Wins   ?? profile?.Wins   ?? 0;
        int losses = myEntry?.Losses ?? profile?.Losses ?? 0;
        int total  = wins + losses;
        float wr   = total > 0 ? wins * 100f / total : 0f;
        string tier = MmrCalculator.GetRankName(myMmr);
        Color tierCol = TierColor(tier);

        // name + avatar
        if (_pcName != null) _pcName.text = _myUsername;
        if (_pcAvatar != null)
        {
            string init = string.IsNullOrEmpty(_myUsername)
                ? "P1"
                : _myUsername.Substring(0, System.Math.Min(2, _myUsername.Length)).ToUpper();
            _pcAvatar.text = init;
            _pcAvatar.color = C_MINT;
        }

        // tier pill
        if (_pcTier != null)
        {
            _pcTier.text  = tier.ToUpper();
            _pcTier.color = tierCol;
        }
        if (_pcTierBg != null)
        {
            // background = tier color เข้มมาก
            _pcTierBg.color = new Color(tierCol.r * 0.25f, tierCol.g * 0.25f, tierCol.b * 0.25f, 1f);
            var ol = _pcTierBg.GetComponent<Outline>();
            if (ol != null) ol.effectColor = new Color(tierCol.r, tierCol.g, tierCol.b, 0.6f);
        }

        // status
        if (_pcStatus != null)
        {
            if (total == 0)
                _pcStatus.text = "อันดับยังไม่จัด · เล่นแมตช์แรกเพื่อจัดอันดับ";
            else if (pos >= 0)
                _pcStatus.text = $"อันดับปัจจุบัน <color=#5eedb3><b>#{pos + 1}</b></color>";
            else
                _pcStatus.text = "ยังไม่ติดอันดับ Top 50";
        }

        // stats
        if (_pcMmr != null) _pcMmr.text = myMmr.ToString();
        if (_pcWL != null)  _pcWL.text  = $"{wins} / {losses}";
        if (_pcWinPct != null) _pcWinPct.text = total > 0 ? $"{wr:F0}%" : "—";
    }

    // ════════════════════════════════════════════
    // 5) FOOTER
    // ════════════════════════════════════════════
    private void BuildFooter(Transform parent)
    {
        // BACK button (outlined)
        var go = CreateGO("BackBtn",
            typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.04f, 0.025f);
        rt.anchorMax = new Vector2(0.20f, 0.085f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        go.GetComponent<Image>().color = new Color(0, 0, 0, 0); // transparent

        var btnOl = go.AddComponent<Outline>();
        btnOl.effectColor    = C_MINT;
        btnOl.effectDistance = new Vector2(1, 1);

        MakeText("Lbl", go.transform, "<  BACK", 30, C_MINT, FontStyles.Bold,
            TextAlignmentOptions.Center,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var btn = go.GetComponent<Button>();
        if (backButton == null) backButton = btn;

        var colors = btn.colors;
        colors.normalColor      = Color.white;
        colors.highlightedColor = new Color(0.85f, 1f, 0.95f, 1f);
        colors.pressedColor     = new Color(0.6f, 0.95f, 0.85f, 1f);
        btn.colors = colors;

        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() =>
        {
            if (_isOverlay) Destroy(gameObject);
            else SceneManager.LoadScene(backSceneName);
        });

        // update info (right)
        MakeText("UpdateInfo", parent, "อัปเดต: ทุก 5 นาที",
            26, C_TEXT_DIM, FontStyles.Normal, TextAlignmentOptions.MidlineRight,
            new Vector2(0.65f, 0.025f), new Vector2(0.96f, 0.085f),
            Vector2.zero, Vector2.zero);
    }

    // ════════════════════════════════════════════
    // SCROLLBAR
    // ════════════════════════════════════════════
    private Scrollbar BuildScrollbar(Transform panelParent)
    {
        var trackGo = CreateGO("Scrollbar",
            typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        trackGo.transform.SetParent(panelParent, false);
        var trackRt = trackGo.GetComponent<RectTransform>();
        trackRt.anchorMin = new Vector2(0.975f, 0.115f);
        trackRt.anchorMax = new Vector2(0.975f, 0.685f);
        trackRt.pivot     = new Vector2(1, 0.5f);
        trackRt.sizeDelta = new Vector2(14, 0);
        trackRt.anchoredPosition = new Vector2(-2, 0);
        trackGo.GetComponent<Image>().color = new Color(C_MINT.r, C_MINT.g, C_MINT.b, 0.08f);

        var slideGo = CreateGO("SlidingArea", typeof(RectTransform));
        slideGo.transform.SetParent(trackGo.transform, false);
        var slideRt = slideGo.GetComponent<RectTransform>();
        slideRt.anchorMin = Vector2.zero;
        slideRt.anchorMax = Vector2.one;
        slideRt.offsetMin = new Vector2(2, 2);
        slideRt.offsetMax = new Vector2(-2, -2);

        var handleGo = CreateGO("Handle",
            typeof(RectTransform), typeof(Image));
        handleGo.transform.SetParent(slideGo.transform, false);
        var handleRt = handleGo.GetComponent<RectTransform>();
        handleRt.anchorMin = Vector2.zero;
        handleRt.anchorMax = Vector2.one;
        handleRt.offsetMin = Vector2.zero;
        handleRt.offsetMax = Vector2.zero;
        var handleImg = handleGo.GetComponent<Image>();
        handleImg.color = new Color(C_MINT.r, C_MINT.g, C_MINT.b, 0.7f);

        var scrollbar = trackGo.GetComponent<Scrollbar>();
        scrollbar.direction       = Scrollbar.Direction.BottomToTop;
        scrollbar.handleRect      = handleRt;
        scrollbar.targetGraphic   = handleImg;
        var sbColors = scrollbar.colors;
        sbColors.normalColor      = Color.white;
        sbColors.highlightedColor = new Color(1f, 1f, 1f, 1f);
        sbColors.pressedColor     = new Color(0.7f, 1f, 0.85f, 1f);
        scrollbar.colors = sbColors;

        return scrollbar;
    }

    // ════════════════════════════════════════════
    // HELPERS
    // ════════════════════════════════════════════
    private static Color TierColor(string tier) => tier switch
    {
        "Legend"   => Hex("#e033ff"),
        "Diamond"  => Hex("#6699ff"),
        "Platinum" => Hex("#00f2d0"),
        "Gold"     => Hex("#ffd700"),
        "Silver"   => Hex("#c0c8d8"),
        _          => Hex("#cd7f32"),
    };

    // สร้าง GameObject แบบไม่ทำให้ scene dirty ตอน Editor preview
    //   (HideFlags.DontSave → Unity ไม่บันทึก object นี้ลง scene file
    //    และไม่ตั้ง dirty flag เวลาสร้าง/ทำลาย)
    private static GameObject CreateGO(string name, params System.Type[] components)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            return EditorUtility.CreateGameObjectWithHideFlags(
                name, HideFlags.DontSave, components);
#endif
        return new GameObject(name, components);
    }

    private static Image MakeImage(string goName, Transform parent,
        Color color, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
    {
        var go  = CreateGO(goName, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt  = go.GetComponent<RectTransform>();
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = offMin; rt.offsetMax = offMax;
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    private static Image MakePanel(string goName, Transform parent,
        Color color, Vector2 aMin, Vector2 aMax)
        => MakeImage(goName, parent, color, aMin, aMax, Vector2.zero, Vector2.zero);

    private TextMeshProUGUI MakeText(
        string goName, Transform parent,
        string text, float size, Color color,
        FontStyles style, TextAlignmentOptions align,
        Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
    {
        var go  = CreateGO(goName, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt  = go.GetComponent<RectTransform>();
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = offMin; rt.offsetMax = offMax;
        var tmp = go.GetComponent<TextMeshProUGUI>();

        if (customFont != null)
        {
            tmp.font = customFont;
        }

        tmp.text          = text;
        tmp.fontSize      = size;
        tmp.color         = color;
        tmp.fontStyle     = style;
        tmp.alignment     = align;
        tmp.raycastTarget = false;
        tmp.overflowMode  = TextOverflowModes.Ellipsis;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        return tmp;
    }
}
