using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.UI;
using StartupCity.Audio;

public class ModeSelectUI : MonoBehaviour
{
    [Header("---- UI Panels ----")]
    public GameObject mainMenuPanel;
    public GameObject modeSelectPanel;
    public GameObject lobbyPanel;
    public GameObject autoMatchPlayerCountPanel;

    public TextMeshProUGUI totalPointsText;
    public TextMeshProUGUI totalGemsText; // แสดง Gem หน้า Main Menu
    public TextMeshProUGUI usernameText;  // แสดงชื่อผู้เล่น
    public Button logoutButton;          // ปุ่ม Logout

    [Header("---- Character Selection ----")]
    public CharacterData[] availableCharacters;
    public Image characterPreviewImage;
    public TextMeshProUGUI characterNameText;
    private int currentCharacterIndex = 0;

    [Header("---- Matchmaking ----")]
    public MatchmakingClient matchmakingClient;

    [Header("---- Main Menu Buttons ----")]
    public Button dailyQuizButton;
    public Button shopButton;
    public Button leaderboardButton;

    private void OnEnable()
    {
        RefreshStatsDisplay();
        // โหลดตัวละครล่าสุดจาก Prefs (ซึ่งอาจถูกอัปเดตจากการโหลด Profile)
        currentCharacterIndex = PlayerPrefs.GetInt("SelectedCharacter", 0);
        UpdateCharacterPreview();
    }

    private void Start()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        if (modeSelectPanel != null) modeSelectPanel.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        BuildAutoMatchPlayerCountPanelIfNeeded();
        WireAutoMatchPlayerCountPanelButtons();
        if (autoMatchPlayerCountPanel != null) autoMatchPlayerCountPanel.SetActive(false);

        RefreshStatsDisplay();

        // Subscribe ถ้า CurrencyManager มี (DontDestroyOnLoad)
        if (CurrencyManager.Instance != null)
            CurrencyManager.Instance.OnGemsChanged += RefreshGemsDisplay;

        currentCharacterIndex = PlayerPrefs.GetInt("SelectedCharacter", 0);
        UpdateCharacterPreview();

        // [FIX] Auto-setup button listeners
        if (dailyQuizButton != null)
        {
            dailyQuizButton.onClick.RemoveAllListeners();
            dailyQuizButton.onClick.AddListener(OnClickDailyQuiz);
        }
        if (shopButton != null)
        {
            shopButton.onClick.RemoveAllListeners();
            shopButton.onClick.AddListener(OnClickShop);
        }
        if (leaderboardButton != null)
        {
            leaderboardButton.onClick.RemoveAllListeners();
            leaderboardButton.onClick.AddListener(OnClickLeaderboard);
        }
        if (logoutButton != null)
        {
            logoutButton.onClick.RemoveAllListeners();
            logoutButton.onClick.AddListener(OnClickLogout);
        }
    }

    public void OnClickMainMenuPlay()
    {
        AudioManager.Instance?.PlayButtonClick();
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (modeSelectPanel != null) modeSelectPanel.SetActive(true);
    }

    public void OnClickBackToMain()
    {
        AudioManager.Instance?.PlayButtonClick();
        if (modeSelectPanel != null) modeSelectPanel.SetActive(false);
        if (autoMatchPlayerCountPanel != null) autoMatchPlayerCountPanel.SetActive(false);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
    }

    private void OnDestroy()
    {
        if (CurrencyManager.Instance != null)
            CurrencyManager.Instance.OnGemsChanged -= RefreshGemsDisplay;
    }

    private void RefreshStatsDisplay()
    {
        int points = PlayerPrefs.GetInt("TotalPoints", 0);
        int gems = CurrencyManager.Instance != null ? CurrencyManager.Instance.Gems : PlayerPrefs.GetInt("TotalGems", 0);

        if (totalPointsText != null) totalPointsText.text = points.ToString();
        if (totalGemsText != null) totalGemsText.text = gems.ToString();
        
        // อัปเดตชื่อผู้เล่น
        if (usernameText != null)
        {
            usernameText.text = GetConfiguredLocalPlayerName();
        }
    }
    
    public async void OnClickLogout()
    {
        AudioManager.Instance?.PlayButtonClick();
        if (SupabaseManager.Instance != null)
        {
            await SupabaseManager.Instance.SignOut();
            SceneManager.LoadScene("LoginScece");
        }
    }

    private void RefreshGemsDisplay(int newGems)
    {
        if (totalGemsText != null)
            totalGemsText.text = newGems.ToString();
    }

    public void OnClickShop()
    {
        AudioManager.Instance?.PlayButtonClick();
        UnityEngine.SceneManagement.SceneManager.LoadScene("StoreScene");
    }

    public void OnClickLeaderboard()
    {
        // สร้าง RankUI เป็น overlay ทับ scene ปัจจุบัน
        // RankUI ตรวจชื่อ "LeaderboardOverlay" → กด Back จะ Destroy ตัวเองแทน LoadScene
        if (UnityEngine.Object.FindFirstObjectByType<RankUI>() != null) return; // ป้องกันเปิดซ้อน
        var go = new UnityEngine.GameObject("LeaderboardOverlay");
        go.AddComponent<RankUI>();
    }

    public void OnClickDailyQuiz()
    {
        AudioManager.Instance?.PlayButtonClick();
        UnityEngine.SceneManagement.SceneManager.LoadScene("DailyQuizScene");
    }

    public void OpenPanel()
    {
        if (modeSelectPanel != null) modeSelectPanel.SetActive(true);
    }

    public void ClosePanel()
    {
        if (modeSelectPanel != null) modeSelectPanel.SetActive(false);
    }

    public void OnClickBotMode()
    {
        AudioManager.Instance?.PlayButtonClick();
        GameLog.Log("[Mode] Selected bot mode.");
        PlayerPrefs.SetString("GameMode", "Bot");
        PlayerPrefs.DeleteKey("MatchmakingRoomCode");
        PlayerPrefs.Save();
        SceneManager.LoadScene("SampleScene");
    }

    public void OnClickRoomMode()
    {
        AudioManager.Instance?.PlayButtonClick();
        GameLog.Log("[Mode] Selected private room mode.");
        if (modeSelectPanel != null) modeSelectPanel.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(true);
    }

    public void OnClickAutoMatch()
    {
        AudioManager.Instance?.PlayButtonClick();
        GameLog.Log("[Mode] Selected auto match mode.");

        if (autoMatchPlayerCountPanel != null)
        {
            autoMatchPlayerCountPanel.SetActive(true);
            return;
        }

        StartAutoMatchWithPlayerCount(2);
    }

    public void OnClickAutoMatch2Players()
    {
        StartAutoMatchWithPlayerCount(2);
    }

    public void OnClickAutoMatch3Players()
    {
        StartAutoMatchWithPlayerCount(3);
    }

    public void OnClickAutoMatch4Players()
    {
        StartAutoMatchWithPlayerCount(4);
    }

    public void OnClickCloseAutoMatchPlayerCountPanel()
    {
        if (autoMatchPlayerCountPanel != null)
        {
            autoMatchPlayerCountPanel.SetActive(false);
        }
    }

    private void StartAutoMatchWithPlayerCount(int playerCount)
    {
        int safePlayerCount = Mathf.Clamp(playerCount, 2, 4);
        if (autoMatchPlayerCountPanel != null)
        {
            autoMatchPlayerCountPanel.SetActive(false);
        }

        if (matchmakingClient != null)
        {
            matchmakingClient.FindMatch(safePlayerCount);
            return;
        }

        Debug.LogWarning("[Mode] MatchmakingClient is not assigned on ModeSelectUI.");
    }

    public void OnClickNextCharacter()
    {
        if (availableCharacters == null || availableCharacters.Length == 0) return;

        currentCharacterIndex++;
        if (currentCharacterIndex >= availableCharacters.Length) currentCharacterIndex = 0;

        UpdateCharacterPreview();
        _ = PlayerDataService.SaveCharacterAsync(currentCharacterIndex);
    }

    public void OnClickPrevCharacter()
    {
        if (availableCharacters == null || availableCharacters.Length == 0) return;

        currentCharacterIndex--;
        if (currentCharacterIndex < 0) currentCharacterIndex = availableCharacters.Length - 1;

        UpdateCharacterPreview();
        _ = PlayerDataService.SaveCharacterAsync(currentCharacterIndex);
    }

    private void UpdateCharacterPreview()
    {
        if (availableCharacters == null || availableCharacters.Length == 0) return;

        if (currentCharacterIndex < 0 || currentCharacterIndex >= availableCharacters.Length)
        {
            currentCharacterIndex = 0;
        }

        CharacterData charData = availableCharacters[currentCharacterIndex];

        if (characterPreviewImage != null) characterPreviewImage.sprite = charData.portraitSprite;
        if (characterNameText != null) characterNameText.text = charData.characterName;
    }

    private void BuildAutoMatchPlayerCountPanelIfNeeded()
    {
        if (autoMatchPlayerCountPanel != null || modeSelectPanel == null)
        {
            return;
        }

        autoMatchPlayerCountPanel = new GameObject("AutoMatchPlayerCountPanel", typeof(RectTransform), typeof(Image));
        autoMatchPlayerCountPanel.transform.SetParent(modeSelectPanel.transform, false);

        RectTransform panelRect = autoMatchPlayerCountPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = new Vector2(0f, -10f);
        panelRect.sizeDelta = new Vector2(460f, 360f);

        Image panelImage = autoMatchPlayerCountPanel.GetComponent<Image>();
        panelImage.color = new Color(0.08f, 0.14f, 0.19f, 0.96f);
        panelImage.raycastTarget = true;

        TMP_FontAsset sharedFont = characterNameText != null ? characterNameText.font : TMP_Settings.defaultFontAsset;
        CreatePanelLabel("Select Player Count", new Vector2(0f, 126f), new Vector2(360f, 42f), 26, Color.white, sharedFont);
        CreatePlayerCountButton("2 Players", new Vector2(0f, 52f), () => OnClickAutoMatch2Players(), sharedFont);
        CreatePlayerCountButton("3 Players", new Vector2(0f, -16f), () => OnClickAutoMatch3Players(), sharedFont);
        CreatePlayerCountButton("4 Players", new Vector2(0f, -84f), () => OnClickAutoMatch4Players(), sharedFont);
        CreateCloseButton(sharedFont);
    }

    private void WireAutoMatchPlayerCountPanelButtons()
    {
        if (autoMatchPlayerCountPanel == null)
        {
            return;
        }

        BindAutoMatchButton("2PlayersButton", OnClickAutoMatch2Players);
        BindAutoMatchButton("3PlayersButton", OnClickAutoMatch3Players);
        BindAutoMatchButton("4PlayersButton", OnClickAutoMatch4Players);
        BindAutoMatchButton("AutoMatchCloseButton", OnClickCloseAutoMatchPlayerCountPanel);
    }

    private void BindAutoMatchButton(string objectName, UnityEngine.Events.UnityAction onClick)
    {
        Transform buttonTransform = autoMatchPlayerCountPanel.transform.Find(objectName);
        if (buttonTransform == null)
        {
            return;
        }

        Button button = buttonTransform.GetComponent<Button>();
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(onClick);
    }

    private void CreatePlayerCountButton(string label, Vector2 anchoredPosition, UnityEngine.Events.UnityAction onClick, TMP_FontAsset font)
    {
        GameObject buttonObject = new GameObject(label.Replace(" ", string.Empty) + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(autoMatchPlayerCountPanel.transform, false);

        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = anchoredPosition;
        buttonRect.sizeDelta = new Vector2(300f, 50f);

        Image buttonImage = buttonObject.GetComponent<Image>();
        buttonImage.color = Color.white;

        Button button = buttonObject.GetComponent<Button>();
        button.onClick.AddListener(onClick);

        CreateButtonLabel(buttonObject.transform, label, font, new Color(0.196f, 0.196f, 0.196f, 1f));
    }

    private void CreateCloseButton(TMP_FontAsset font)
    {
        GameObject buttonObject = new GameObject("AutoMatchCloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(autoMatchPlayerCountPanel.transform, false);

        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = new Vector2(0f, -144f);
        buttonRect.sizeDelta = new Vector2(160f, 36f);

        Image buttonImage = buttonObject.GetComponent<Image>();
        buttonImage.color = Color.black;

        Button button = buttonObject.GetComponent<Button>();
        button.onClick.AddListener(OnClickCloseAutoMatchPlayerCountPanel);

        CreateButtonLabel(buttonObject.transform, "Close", font, Color.white);
    }

    private void CreatePanelLabel(string text, Vector2 anchoredPosition, Vector2 size, float fontSize, Color color, TMP_FontAsset font)
    {
        GameObject labelObject = new GameObject("PanelLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(autoMatchPlayerCountPanel.transform, false);

        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.anchoredPosition = anchoredPosition;
        labelRect.sizeDelta = size;

        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.font = font;
        label.fontSize = fontSize;
        label.fontStyle = FontStyles.Bold;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
    }

    private void CreateButtonLabel(Transform parent, string text, TMP_FontAsset font, Color color)
    {
        GameObject labelObject = new GameObject("Text (TMP)", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(parent, false);

        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.font = font;
        label.fontSize = 24f;
        label.fontStyle = FontStyles.Bold;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
    }
    private string GetConfiguredLocalPlayerName()
    {
        if (SupabaseManager.Instance != null)
        {
            string realName = SupabaseManager.Instance.GetCurrentUsername();
            if (!string.IsNullOrWhiteSpace(realName))
            {
                return realName;
            }
        }

        string humanName = PlayerPrefs.GetString("Username", string.Empty);
        return string.IsNullOrWhiteSpace(humanName) ? "Player 1" : humanName;
    }
}
