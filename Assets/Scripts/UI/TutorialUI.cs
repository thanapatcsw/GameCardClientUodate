using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// TutorialUI — ระบบหน้าจอสอนเล่นที่สามารถแก้ไขผ่าน Hierarchy ได้อย่างสมบูรณ์
/// </summary>
public class TutorialUI : MonoBehaviour
{
    [Header("การตั้งค่าทั่วไป")]
    public TMP_FontAsset customFont;
    public string mainMenuSceneName = "MainMenu 1";

    [System.Serializable]
    public struct SlideData
    {
        public string title;
        [TextArea(3, 10)]
        public string body;
        public string icon;
        public Sprite illustrationSprite; // เพิ่มตัวแปรสำหรับรูปภาพ PNG
        public Color accentColor;
    }

    [Header("สไลด์สอนเล่น (แก้ไขผ่าน Inspector ได้)")]
    public SlideData[] slides = new SlideData[]
    {
        new SlideData
        {
            title       = "[ การเล่นในแต่ละเทิร์น ]",
            body        = "เมื่อถึงเทิร์นของคุณ คุณสามารถเลือกทำได้ <b>เพียง 1 อย่าง</b>:\n\n" +
                          "  1. <b>หยิบเหรียญทรัพยากร</b> (หยิบ 3 เหรียญต่างสี หรือ 2 เหรียญสีเดียวกัน)\n" +
                          "  2. <b>ซื้อการ์ด</b> เพื่อสะสมคะแนนชัยชนะ (VP) และโบนัสส่วนลดในตาถัดไป\n" +
                          "  3. <b>จองการ์ด</b> เก็บไว้บนมือ เพื่อไม่ให้คนอื่นแย่งซื้อ",
            icon        = "ACTION",
            illustrationSprite = null,
            accentColor = new Color(0.2f, 0.85f, 0.4f)
        },
        new SlideData
        {
            title       = "[ การจองการ์ด & เหรียญดำ ]",
            body        = "คุณสามารถจองการ์ดได้ง่ายๆ โดยการ <b>กดค้างที่การ์ด</b> ที่ต้องการ\n\n" +
                          "เมื่อจองการ์ดสำเร็จ คุณจะได้รับ <b>เหรียญดำ (Black Coin)</b> จำนวน 1 เหรียญ\n\n" +
                          "เหรียญดำมีความพิเศษมาก เพราะสามารถนำไปใช้ <b>แทนเหรียญสีใดก็ได้</b> 1 เหรียญในการซื้อการ์ด!",
            icon        = "COIN",
            illustrationSprite = null,
            accentColor = new Color(0.8f, 0.8f, 0.8f)
        },
        new SlideData
        {
            title       = "[ การตอบคำถาม (Quiz) ]",
            body        = "เกมนี้ไม่ได้วัดแค่ดวง! <b>ทุกๆ 5 เทิร์น</b> จะมีช่วงเวลาตอบคำถาม\n\n" +
                          "ผลการตอบคำถามมีผลอย่างมาก เพราะผู้ที่ทำคะแนนได้ดีที่สุด จะได้รับสิทธิ์ในการ <b>กำหนดลำดับการเล่นใหม่</b> ในรอบถัดไป\n\n" +
                          "จงใช้ความรู้ชิงความได้เปรียบ เพื่อมุ่งสู่ชัยชนะ!",
            icon        = "QUIZ",
            illustrationSprite = null,
            accentColor = new Color(0.9f, 0.4f, 0.8f)
        }
    };

    [Header("UI References (เชื่อมต่อแล้ว - แก้ไขใน Hierarchy ได้)")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI bodyText;
    public TextMeshProUGUI pageIndicator;
    public TextMeshProUGUI illIcon;
    public Image illBg;
    public Image illustrationImage; // ตัวแปร Image สำหรับแสดงรูป PNG
    public Button prevBtn;
    public Button nextBtn;
    public Button backBtn;
    public TextMeshProUGUI nextBtnLbl;

    private int _currentSlide = 0;

    private void Start()
    {
        if (prevBtn != null) prevBtn.onClick.AddListener(PrevSlide);
        if (nextBtn != null) nextBtn.onClick.AddListener(NextSlide);
        if (backBtn != null) backBtn.onClick.AddListener(GoBack);

        ShowSlide(0);
    }

    private void ShowSlide(int index)
    {
        if (slides == null || slides.Length == 0) return;
        index = Mathf.Clamp(index, 0, slides.Length - 1);
        _currentSlide = index;
        var s = slides[index];

        if (titleText != null) titleText.text = s.title;
        if (bodyText != null) bodyText.text = s.body;
        if (pageIndicator != null) pageIndicator.text = $"หน้า {index + 1} / {slides.Length}";

        // จัดการแสดงรูปภาพ PNG หรือไอคอนข้อความ
        if (illustrationImage != null)
        {
            if (s.illustrationSprite != null)
            {
                illustrationImage.gameObject.SetActive(true);
                illustrationImage.sprite = s.illustrationSprite;
                if (illIcon != null) illIcon.gameObject.SetActive(false);
            }
            else
            {
                illustrationImage.gameObject.SetActive(false);
                if (illIcon != null)
                {
                    illIcon.gameObject.SetActive(true);
                    illIcon.text = s.icon;
                }
            }
        }
        else
        {
            if (illIcon != null)
            {
                illIcon.gameObject.SetActive(true);
                illIcon.text = s.icon;
            }
        }

        if (illBg != null)
            illBg.color = new Color(s.accentColor.r, s.accentColor.g, s.accentColor.b, 0.12f);

        if (prevBtn != null) prevBtn.interactable = index > 0;
        if (nextBtn != null) nextBtn.interactable = true;

        if (nextBtnLbl != null)
            nextBtnLbl.text = (index == slides.Length - 1) ? "เสร็จสิ้น" : "ถัดไป >";
    }

    public void NextSlide()
    {
        if (_currentSlide >= slides.Length - 1) GoBack();
        else ShowSlide(_currentSlide + 1);
    }

    public void PrevSlide() => ShowSlide(_currentSlide - 1);

    public void GoBack()
    {
        SceneManager.LoadScene(mainMenuSceneName);
    }

#if UNITY_EDITOR
    [ContextMenu("Generate And Link UI")]
    public void GenerateAndLinkUI()
    {
        // ลบของเก่าที่เคยมี (ถ้ามี)
        var managedNames = new System.Collections.Generic.HashSet<string>
            { "TutBG", "TutCard", "BackButton", "PrevButton", "NextButton" };
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var c = transform.GetChild(i).gameObject;
            if (managedNames.Contains(c.name))
                DestroyImmediate(c);
        }

        if (customFont == null)
        {
            customFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/LayijiMahaniyom-Bao-1.asset");
        }

        // ── Dark background ──
        var bg = NewPanel("TutBG", transform, Vector2.zero, Vector2.one);
        bg.AddComponent<Image>().color = new Color(0.04f, 0.07f, 0.12f, 0.98f);
        bg.transform.SetAsFirstSibling(); // ให้อยู่ล่างสุด

        // ── Slide card ──
        var card = NewPanel("TutCard", transform, new Vector2(0.05f, 0.12f), new Vector2(0.95f, 0.91f));
        var cardImg = card.AddComponent<Image>();
        cardImg.color = new Color(0.08f, 0.14f, 0.22f, 0.96f);
        AddOutline(card, new Color(0.2f, 0.7f, 1f, 0.45f));
        var cardT = card.transform;

        // ── Title ──
        titleText = NewText("Title", cardT, new Vector2(0.04f, 0.82f), new Vector2(0.96f, 0.97f), "Title", 26, FontStyles.Bold, TextAlignmentOptions.Center);

        // ── Illustration box ──
        var illGo = NewPanel("Illustration", cardT, new Vector2(0.25f, 0.56f), new Vector2(0.75f, 0.80f));
        illBg = illGo.AddComponent<Image>();
        illBg.color = new Color(0.15f, 0.6f, 1f, 0.12f);
        AddOutline(illGo, new Color(0.3f, 0.75f, 1f, 0.3f));

        // ── PNG Illustration Image Content ──
        var imgContentGo = NewPanel("ImageContent", illGo.transform, new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.95f));
        illustrationImage = imgContentGo.AddComponent<Image>();
        illustrationImage.preserveAspect = true;
        illustrationImage.color = Color.white;
        illustrationImage.gameObject.SetActive(false);

        illIcon = NewText("Icon", illGo.transform, Vector2.zero, Vector2.one, "ICON", 44, FontStyles.Normal, TextAlignmentOptions.Center);

        // ── Body text ──
        bodyText = NewText("Body", cardT, new Vector2(0.04f, 0.06f), new Vector2(0.96f, 0.54f), "Body", 14, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        bodyText.enableWordWrapping = true;
        bodyText.lineSpacing = 5f;
        bodyText.color = new Color(0.88f, 0.93f, 1f, 1f);

        // ── Page dots / indicator ──
        pageIndicator = NewText("PageDots", cardT, new Vector2(0.3f, 0.01f), new Vector2(0.7f, 0.07f), "Page 1 / 1", 13, FontStyles.Normal, TextAlignmentOptions.Center);
        pageIndicator.color = new Color(0.5f, 0.75f, 1f, 0.7f);

        // ── BACK button (bottom-left) ──
        var backGo = NewPanel("BackButton", transform, new Vector2(0.03f, 0.01f), new Vector2(0.3f, 0.09f));
        backBtn = backGo.AddComponent<Button>();
        backGo.AddComponent<Image>().color = new Color(0.12f, 0.2f, 0.35f, 0.92f);
        AddOutline(backGo, new Color(0.3f, 0.6f, 1f, 0.5f));
        NewText("Lbl", backGo.transform, Vector2.zero, Vector2.one, "< เมนูหลัก", 14, FontStyles.Normal, TextAlignmentOptions.Center);

        // ── PREV button ──
        var prevGo = NewPanel("PrevButton", transform, new Vector2(0.03f, 0.11f), new Vector2(0.22f, 0.19f));
        prevBtn = prevGo.AddComponent<Button>();
        prevGo.AddComponent<Image>().color = new Color(0.1f, 0.18f, 0.32f, 0.9f);
        AddOutline(prevGo, new Color(0.2f, 0.6f, 1f, 0.4f));
        NewText("Lbl", prevGo.transform, Vector2.zero, Vector2.one, "< ก่อนหน้า", 13, FontStyles.Normal, TextAlignmentOptions.Center);

        // ── NEXT button ──
        var nextGo = NewPanel("NextButton", transform, new Vector2(0.78f, 0.11f), new Vector2(0.97f, 0.19f));
        nextBtn = nextGo.AddComponent<Button>();
        nextGo.AddComponent<Image>().color = new Color(0.1f, 0.35f, 0.55f, 0.9f);
        AddOutline(nextGo, new Color(0.2f, 0.75f, 1f, 0.65f));
        nextBtnLbl = NewText("Lbl", nextGo.transform, Vector2.zero, Vector2.one, "ถัดไป >", 13, FontStyles.Normal, TextAlignmentOptions.Center);

        EditorUtility.SetDirty(this);
        Debug.Log("[TutorialUI] สร้างและเชื่อมต่อช่องภาพ Illustration (PNG) สำเร็จ!");
    }

    private GameObject NewPanel(string n, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(n);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return go;
    }

    private TextMeshProUGUI NewText(string n, Transform parent, Vector2 anchorMin, Vector2 anchorMax, string txt, float size, FontStyles style, TextAlignmentOptions align)
    {
        var go = new GameObject(n);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = new Vector2(6, 4);
        rt.offsetMax = new Vector2(-6, -4);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = txt;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.color = Color.white;
        tmp.enableWordWrapping = true;
        if (customFont != null) tmp.font = customFont;
        return tmp;
    }

    private void AddOutline(GameObject go, Color color)
    {
        var o = go.AddComponent<Outline>();
        o.effectColor = color;
        o.effectDistance = new Vector2(1f, -1f);
    }
#endif
}
