
using System;
using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MatchmakingClient : MonoBehaviour
{
    private const string WaitingStatus = "waiting";
    private const string MatchedStatus = "matched";
    private const string CancelledStatus = "cancelled";

    private const string PlayerIdPrefsKey = "MatchmakingPlayerId";
    private const string RoomIdPrefsKey = "MatchmakingRoomId";
    private const string RoomCodePrefsKey = "MatchmakingRoomCode";
    private const string TargetPlayerCountPrefsKey = "MatchmakingTargetPlayerCount";

    [Serializable]
    private class MatchmakingRequestPayload
    {
        public string action;
        public string playerId;
        public int targetPlayerCount;
        public string searchRequestId;
        public string sceneName;
        public string displayName;
        public int staleTimeoutSeconds;
    }

    [Serializable]
    private class MatchmakingResponsePayload
    {
        public string status;
        public string playerId;
        public int targetPlayerCount;
        public string roomCode;
        public string roomId;
        public string[] players;
        public string message;
        public string searchRequestId;
    }

    [Header("Scene")]
    [SerializeField] private string gameSceneName = "SampleScene";
    [SerializeField] private float pollIntervalSeconds = 1.5f;

    [Header("Server Matchmaking")]
    [SerializeField] private string matchmakingFunctionName = "matchmaking";
    [SerializeField] private string matchmakingFunctionUrlOverride = string.Empty;
    [SerializeField] private float requestTimeoutSeconds = 10f;
    [SerializeField] private int staleWaitingTimeoutSeconds = 300;

    [Header("UI")]
    [SerializeField] private GameObject searchPanel;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private Button cancelSearchButton;

    private Coroutine _pollCoroutine;
    private Coroutine _searchTimerCoroutine;
    private string _currentPlayerId;
    private string _searchRequestId;
    private bool _isConnectingToFusion;
    private bool _isRequestInFlight;
    private int _targetPlayerCount = 2;
    private float _searchStartedAtRealtime;

    public string CurrentRoomId => PlayerPrefs.GetString(RoomIdPrefsKey, string.Empty);
    public string CurrentRoomCode => PlayerPrefs.GetString(RoomCodePrefsKey, string.Empty);

    private void Awake()
    {
        _currentPlayerId = GetOrCreatePlayerId();
        _targetPlayerCount = GetSavedTargetPlayerCount();
        BuildSearchPanelIfNeeded();
        ResolveSearchPanelReferences();
        WireSearchPanelButtons();
    }

    private void Start()
    {
        HideSearchPanel(resetTimer: true);
    }

    public void FindMatch()
    {
        FindMatch(GetSavedTargetPlayerCount());
    }

    public void FindMatch(int targetPlayerCount)
    {
        _currentPlayerId = GetOrCreatePlayerId();
        _targetPlayerCount = Mathf.Clamp(targetPlayerCount, 2, 4);
        _searchRequestId = CreateUuidString();
        _isConnectingToFusion = false;

        PlayerPrefs.SetInt(TargetPlayerCountPrefsKey, _targetPlayerCount);
        PlayerPrefs.Save();

        ClearSavedMatch();
        StopPolling();
        ShowSearchPanel();
        StartSearchTimer();
        SetCancelButtonInteractable(true);
        SetStatus($"Searching {_targetPlayerCount} players...");

        GameLog.Log($"[Matchmaking] FindMatch pressed. PlayerId={_currentPlayerId}, TargetPlayers={_targetPlayerCount}, SearchRequestId={_searchRequestId}");
        RestartPolling();
    }

    public void CancelMatchmaking()
    {
        string cancelledSearchRequestId = _searchRequestId;

        _currentPlayerId = GetOrCreatePlayerId();
        _searchRequestId = string.Empty;
        _isConnectingToFusion = false;
        StopPolling();
        ClearSavedMatch();
        HideSearchPanel(resetTimer: true);
        SetStatus("Cancelled");

        GameLog.Log($"[Matchmaking] CancelMatchmaking pressed. PlayerId={_currentPlayerId}, SearchRequestId={cancelledSearchRequestId}");

        if (!string.IsNullOrWhiteSpace(cancelledSearchRequestId))
        {
            StartCoroutine(SendCancelRequestCoroutine(cancelledSearchRequestId));
        }
    }

    public void PollMatchStatus()
    {
        if (_isRequestInFlight || string.IsNullOrWhiteSpace(_searchRequestId))
        {
            return;
        }

        StartCoroutine(PollMatchStatusOnceCoroutine());
    }

    private IEnumerator PollLoopCoroutine()
    {
        while (true)
        {
            yield return PollMatchStatusOnceCoroutine();

            if (string.IsNullOrEmpty(_searchRequestId) || !string.IsNullOrEmpty(CurrentRoomCode) || _isConnectingToFusion)
            {
                _pollCoroutine = null;
                yield break;
            }

            yield return new WaitForSecondsRealtime(Mathf.Max(0.25f, pollIntervalSeconds));
        }
    }

    private void RestartPolling()
    {
        StopPolling();
        _pollCoroutine = StartCoroutine(PollLoopCoroutine());
    }

    private void StopPolling()
    {
        if (_pollCoroutine != null)
        {
            StopCoroutine(_pollCoroutine);
            _pollCoroutine = null;
        }
    }

    private IEnumerator PollMatchStatusOnceCoroutine()
    {
        if (_isRequestInFlight || string.IsNullOrWhiteSpace(_searchRequestId))
        {
            yield break;
        }

        // Auto Match is now server-authoritative. The client only polls the Edge Function
        // and reacts to waiting/matched results instead of selecting players locally.
        MatchmakingRequestPayload payload = new MatchmakingRequestPayload
        {
            action = "find",
            playerId = _currentPlayerId,
            targetPlayerCount = _targetPlayerCount,
            searchRequestId = _searchRequestId,
            sceneName = gameSceneName,
            displayName = GetDisplayNameForRoomMetadata(),
            staleTimeoutSeconds = Mathf.Max(30, staleWaitingTimeoutSeconds)
        };

        yield return SendMatchmakingRequestCoroutine(payload, "find matchmaking", HandleMatchmakingResponse);
    }

    private IEnumerator SendCancelRequestCoroutine(string searchRequestId)
    {
        MatchmakingRequestPayload payload = new MatchmakingRequestPayload
        {
            action = "cancel",
            playerId = _currentPlayerId,
            targetPlayerCount = _targetPlayerCount,
            searchRequestId = searchRequestId,
            sceneName = gameSceneName,
            displayName = GetDisplayNameForRoomMetadata(),
            staleTimeoutSeconds = Mathf.Max(30, staleWaitingTimeoutSeconds)
        };

        yield return SendMatchmakingRequestCoroutine(payload, "cancel matchmaking", null);
    }

    private IEnumerator SendMatchmakingRequestCoroutine(MatchmakingRequestPayload payload, string actionName, Action<MatchmakingResponsePayload> onComplete)
    {
        string functionUrl = ResolveMatchmakingFunctionUrl();
        if (string.IsNullOrWhiteSpace(functionUrl))
        {
            SetErrorStatus("Matchmaking function URL is not configured.");
            Debug.LogError("[Matchmaking] Matchmaking function URL is missing.");
            yield break;
        }

        string anonKey = SupabaseManager.Instance != null ? SupabaseManager.Instance.SupabaseAnonKey : string.Empty;
        // ส่ง JWT ของผู้ใช้ (ถ้ามี session) เพื่อให้ฝั่ง server ระบุ playerId จาก auth.uid() เอง
        string userToken = SupabaseManager.Instance?.Client?.Auth?.CurrentSession?.AccessToken;
        string jsonPayload = JsonUtility.ToJson(payload);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);

        using (UnityWebRequest request = new UnityWebRequest(functionUrl, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = Mathf.Clamp(Mathf.CeilToInt(requestTimeoutSeconds), 1, 60);
            request.SetRequestHeader("Content-Type", "application/json");

            if (!string.IsNullOrWhiteSpace(anonKey))
            {
                request.SetRequestHeader("apikey", anonKey);
                // ใช้ user token เป็น Bearer; ถ้าไม่มี session ใช้ anon (server จะตอบ 401 ให้เข้าสู่ระบบ)
                string bearer = string.IsNullOrWhiteSpace(userToken) ? anonKey : userToken;
                request.SetRequestHeader("Authorization", $"Bearer {bearer}");
            }

            _isRequestInFlight = true;
            yield return request.SendWebRequest();
            _isRequestInFlight = false;

            string responseText = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
            MatchmakingResponsePayload responsePayload = TryParseResponsePayload(responseText);

            if (request.result != UnityWebRequest.Result.Success)
            {
                string errorMessage = responsePayload != null && !string.IsNullOrWhiteSpace(responsePayload.message)
                    ? responsePayload.message
                    : string.IsNullOrWhiteSpace(request.error)
                        ? $"Unable to {actionName}."
                        : request.error;

                Debug.LogWarning($"[Matchmaking] HTTP {actionName} failed. Url={functionUrl}, Error={errorMessage}, Body={responseText}");
                SetErrorStatus(errorMessage);
                yield break;
            }

            if (responsePayload == null)
            {
                Debug.LogWarning($"[Matchmaking] {actionName} returned invalid JSON: {responseText}");
                SetErrorStatus("Matchmaking returned an invalid response.");
                yield break;
            }

            GameLog.Log($"[Matchmaking] {actionName} response => {responsePayload.status} / roomCode={responsePayload.roomCode} / roomId={responsePayload.roomId}");
            onComplete?.Invoke(responsePayload);
        }
    }

    private void HandleMatchmakingResponse(MatchmakingResponsePayload response)
    {
        if (response == null)
        {
            SetErrorStatus("Matchmaking returned an empty response.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(response.playerId))
        {
            if (TryNormalizeUuid(response.playerId, out string normalizedPlayerId))
            {
                _currentPlayerId = normalizedPlayerId;
                PlayerPrefs.SetString(PlayerIdPrefsKey, _currentPlayerId);
                PlayerPrefs.Save();
            }
            else
            {
                Debug.LogWarning($"[Matchmaking] Ignoring non-UUID playerId from server: {response.playerId}");
            }
        }

        if (response.targetPlayerCount >= 2 && response.targetPlayerCount <= 4)
        {
            _targetPlayerCount = response.targetPlayerCount;
        }

        string normalizedStatus = string.IsNullOrWhiteSpace(response.status)
            ? string.Empty
            : response.status.Trim().ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(response.searchRequestId))
        {
            bool isStaleSearchResponse = string.IsNullOrWhiteSpace(_searchRequestId) ||
                                         !string.Equals(_searchRequestId, response.searchRequestId, StringComparison.Ordinal);

            if (isStaleSearchResponse)
            {
                GameLog.Log($"[Matchmaking] Ignoring stale response for SearchRequestId={response.searchRequestId}");
                return;
            }
        }

        switch (normalizedStatus)
        {
            case WaitingStatus:
                ShowSearchPanel();
                SetCancelButtonInteractable(true);
                SetStatus($"Searching {_targetPlayerCount} players...");
                GameLog.Log($"[Matchmaking] Waiting snapshot. TargetPlayers={_targetPlayerCount}, VisiblePlayers={FormatPlayersForLog(response.players)}, Message={response.message}, SearchRequestId={response.searchRequestId}");
                return;

            case MatchedStatus:
                if (string.IsNullOrWhiteSpace(response.roomCode))
                {
                    SetErrorStatus("Matched response did not include a roomCode.");
                    return;
                }

                StopPolling();
                _searchRequestId = string.Empty;
                _isConnectingToFusion = true;
                StopSearchTimer();
                SetCancelButtonInteractable(false);

                PlayerPrefs.SetString(RoomIdPrefsKey, response.roomId ?? string.Empty);
                PlayerPrefs.SetString(RoomCodePrefsKey, response.roomCode);
                PlayerPrefs.Save();

                SetStatus("Match found");
                GameLog.Log($"[Matchmaking] Match found. RoomCode={response.roomCode}, RoomId={response.roomId}, Players={FormatPlayersForLog(response.players)}");

                if (FusionManager.Instance != null)
                {
                    FusionManager.Instance.StartMatchedGame(response.roomCode, gameSceneName);
                    return;
                }

                Debug.LogWarning($"[Matchmaking] FusionManager not found. Loading scene {gameSceneName} only.");
                SceneManager.LoadScene(gameSceneName);
                return;

            case CancelledStatus:
                HideSearchPanel(resetTimer: true);
                SetStatus("Cancelled");
                return;

            case "error":
                SetErrorStatus(response.message);
                return;

            default:
                SetErrorStatus(string.IsNullOrWhiteSpace(response.message)
                    ? $"Unexpected matchmaking status: {response.status}"
                    : response.message);
                return;
        }
    }

    private static MatchmakingResponsePayload TryParseResponsePayload(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonUtility.FromJson<MatchmakingResponsePayload>(json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Matchmaking] Failed to parse JSON response. {ex.Message}");
            return null;
        }
    }

    private string ResolveMatchmakingFunctionUrl()
    {
        if (!string.IsNullOrWhiteSpace(matchmakingFunctionUrlOverride))
        {
            return matchmakingFunctionUrlOverride.TrimEnd('/');
        }

        if (SupabaseManager.Instance == null || string.IsNullOrWhiteSpace(SupabaseManager.Instance.SupabaseUrl))
        {
            return null;
        }

        return $"{SupabaseManager.Instance.SupabaseUrl.TrimEnd('/')}/functions/v1/{matchmakingFunctionName.Trim('/')}";
    }

    private static string FormatPlayersForLog(string[] players)
    {
        return players == null || players.Length == 0 ? "(none)" : string.Join(", ", players);
    }

    private void ClearSavedMatch()
    {
        PlayerPrefs.DeleteKey(RoomIdPrefsKey);
        PlayerPrefs.DeleteKey(RoomCodePrefsKey);
        PlayerPrefs.Save();
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }

        GameLog.Log($"[Matchmaking] UI Status => {message}");
    }

    private void SetErrorStatus(string errorMessage)
    {
        ShowSearchPanel();
        SetCancelButtonInteractable(true);

        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            SetStatus("Error");
            return;
        }

        SetStatus($"Error: {errorMessage}");
    }

    private string GetOrCreatePlayerId()
    {
        if (TryGetAuthenticatedPlayerId(out string authenticatedPlayerId))
        {
            string cachedPlayerId = PlayerPrefs.GetString(PlayerIdPrefsKey, string.Empty);
            if (!string.Equals(cachedPlayerId, authenticatedPlayerId, StringComparison.Ordinal))
            {
                GameLog.Log($"[Matchmaking] Syncing cached playerId to authenticated user. Cached={cachedPlayerId}, Authenticated={authenticatedPlayerId}");
                PlayerPrefs.SetString(PlayerIdPrefsKey, authenticatedPlayerId);
                PlayerPrefs.Save();
            }

            return authenticatedPlayerId;
        }

        string savedPlayerId = PlayerPrefs.GetString(PlayerIdPrefsKey, string.Empty);
        if (TryNormalizeUuid(savedPlayerId, out string normalizedSavedPlayerId))
        {
            if (!string.Equals(savedPlayerId, normalizedSavedPlayerId, StringComparison.Ordinal))
            {
                PlayerPrefs.SetString(PlayerIdPrefsKey, normalizedSavedPlayerId);
                PlayerPrefs.Save();
            }

            return normalizedSavedPlayerId;
        }

        if (string.IsNullOrWhiteSpace(savedPlayerId))
        {
            if (!string.IsNullOrWhiteSpace(PlayerPrefs.GetString(PlayerIdPrefsKey, string.Empty)))
            {
                Debug.LogWarning($"[Matchmaking] Replacing invalid cached playerId with a generated UUID. CachedValue={PlayerPrefs.GetString(PlayerIdPrefsKey, string.Empty)}");
            }

            savedPlayerId = CreateUuidString();
        }

        PlayerPrefs.SetString(PlayerIdPrefsKey, savedPlayerId);
        PlayerPrefs.Save();
        return savedPlayerId;
    }

    private static bool TryGetAuthenticatedPlayerId(out string authenticatedPlayerId)
    {
        authenticatedPlayerId = string.Empty;

        var currentUser = SupabaseManager.Instance?.Client?.Auth?.CurrentUser;
        return currentUser != null && TryNormalizeUuid(currentUser.Id, out authenticatedPlayerId);
    }

    private static string CreateUuidString()
    {
        return Guid.NewGuid().ToString("D");
    }

    private static bool TryNormalizeUuid(string value, out string normalizedUuid)
    {
        normalizedUuid = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!Guid.TryParse(value.Trim(), out Guid parsedGuid))
        {
            return false;
        }

        normalizedUuid = parsedGuid.ToString("D");
        return true;
    }

    private int GetSavedTargetPlayerCount()
    {
        return Mathf.Clamp(PlayerPrefs.GetInt(TargetPlayerCountPrefsKey, 2), 2, 4);
    }

    private string GetDisplayNameForRoomMetadata()
    {
        if (SupabaseManager.Instance != null)
        {
            string username = SupabaseManager.Instance.GetCurrentUsername();
            if (!string.IsNullOrWhiteSpace(username))
            {
                return username;
            }
        }

        string localUsername = PlayerPrefs.GetString("Username", string.Empty);
        return string.IsNullOrWhiteSpace(localUsername) ? _currentPlayerId : localUsername;
    }

    private void ShowSearchPanel()
    {
        BuildSearchPanelIfNeeded();
        ResolveSearchPanelReferences();
        WireSearchPanelButtons();
        SetCancelButtonInteractable(true);

        if (searchPanel != null)
        {
            searchPanel.SetActive(true);
        }
    }

    private void HideSearchPanel(bool resetTimer)
    {
        if (searchPanel != null)
        {
            searchPanel.SetActive(false);
        }

        StopSearchTimer(resetTimer);
        SetCancelButtonInteractable(true);
    }

    private void StartSearchTimer()
    {
        _searchStartedAtRealtime = Time.realtimeSinceStartup;
        StopSearchTimer(resetTimer: true);
        UpdateTimerText(0f);
        _searchTimerCoroutine = StartCoroutine(UpdateSearchTimerCoroutine());
    }

    private void StopSearchTimer(bool resetTimer = false)
    {
        if (_searchTimerCoroutine != null)
        {
            StopCoroutine(_searchTimerCoroutine);
            _searchTimerCoroutine = null;
        }

        if (resetTimer)
        {
            UpdateTimerText(0f);
        }
    }

    private IEnumerator UpdateSearchTimerCoroutine()
    {
        while (true)
        {
            UpdateTimerText(Time.realtimeSinceStartup - _searchStartedAtRealtime);
            yield return new WaitForSecondsRealtime(0.2f);
        }
    }

    private void UpdateTimerText(float elapsedSeconds)
    {
        if (timerText == null)
        {
            return;
        }

        int totalSeconds = Mathf.Max(0, Mathf.FloorToInt(elapsedSeconds));
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        timerText.text = $"{minutes:00}:{seconds:00}";
    }

    private void SetCancelButtonInteractable(bool interactable)
    {
        if (cancelSearchButton != null)
        {
            cancelSearchButton.interactable = interactable;
        }
    }

    private void ResolveSearchPanelReferences()
    {
        if (searchPanel == null)
        {
            Transform existingPanelTransform = transform.Find("MatchmakingSearchPanel");
            if (existingPanelTransform != null)
            {
                searchPanel = existingPanelTransform.gameObject;
            }
        }

        if (searchPanel == null)
        {
            return;
        }

        if (statusText == null)
        {
            Transform statusTransform = searchPanel.transform.Find("StatusText");
            if (statusTransform != null)
            {
                statusText = statusTransform.GetComponent<TextMeshProUGUI>();
            }
        }

        if (timerText == null)
        {
            Transform timerTransform = searchPanel.transform.Find("TimerText");
            if (timerTransform != null)
            {
                timerText = timerTransform.GetComponent<TextMeshProUGUI>();
            }
        }

        if (cancelSearchButton == null)
        {
            Transform cancelTransform = searchPanel.transform.Find("CancelSearchButton");
            if (cancelTransform != null)
            {
                cancelSearchButton = cancelTransform.GetComponent<Button>();
            }
        }
    }

    private void WireSearchPanelButtons()
    {
        if (cancelSearchButton == null)
        {
            return;
        }

        cancelSearchButton.onClick.RemoveAllListeners();
        cancelSearchButton.onClick.AddListener(CancelMatchmaking);
    }

    private void BuildSearchPanelIfNeeded()
    {
        ResolveSearchPanelReferences();
        if (searchPanel != null)
        {
            return;
        }

        searchPanel = new GameObject("MatchmakingSearchPanel", typeof(RectTransform), typeof(Image));
        searchPanel.transform.SetParent(transform, false);

        RectTransform panelRect = searchPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = new Vector2(0f, -24f);
        panelRect.sizeDelta = new Vector2(430f, 210f);

        Image panelImage = searchPanel.GetComponent<Image>();
        panelImage.color = new Color(0.06f, 0.11f, 0.16f, 0.96f);
        panelImage.raycastTarget = true;

        TMP_FontAsset sharedFont = FindSharedFont();
        CreatePanelText("SearchTitleText", "Finding Match", new Vector2(0f, 64f), new Vector2(280f, 34f), 28f, Color.white, sharedFont, FontStyles.Bold);
        statusText = CreatePanelText("StatusText", "Searching...", new Vector2(0f, 16f), new Vector2(320f, 34f), 22f, new Color(0.92f, 0.96f, 0.98f, 1f), sharedFont, FontStyles.Normal);
        timerText = CreatePanelText("TimerText", "00:00", new Vector2(0f, -36f), new Vector2(220f, 40f), 30f, new Color(1f, 0.9f, 0.58f, 1f), sharedFont, FontStyles.Bold);
        cancelSearchButton = CreateIconButton("CancelSearchButton", "X", new Vector2(176f, 76f), new Vector2(42f, 42f), sharedFont);
        CreatePanelText("CancelHintText", "Tap X to stop searching", new Vector2(0f, -86f), new Vector2(260f, 28f), 18f, new Color(0.78f, 0.84f, 0.89f, 1f), sharedFont, FontStyles.Italic);
    }

    private TMP_FontAsset FindSharedFont()
    {
        if (statusText != null && statusText.font != null)
        {
            return statusText.font;
        }

        TextMeshProUGUI existingText = GetComponentInChildren<TextMeshProUGUI>(true);
        if (existingText != null && existingText.font != null)
        {
            return existingText.font;
        }

        return TMP_Settings.defaultFontAsset;
    }

    private TextMeshProUGUI CreatePanelText(string objectName, string text, Vector2 anchoredPosition, Vector2 size, float fontSize, Color color, TMP_FontAsset font, FontStyles fontStyle)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(searchPanel.transform, false);

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.5f, 0.5f);
        textRect.anchorMax = new Vector2(0.5f, 0.5f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = anchoredPosition;
        textRect.sizeDelta = size;

        TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.font = font;
        label.fontSize = fontSize;
        label.fontStyle = fontStyle;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        return label;
    }

    private Button CreateIconButton(string objectName, string buttonLabel, Vector2 anchoredPosition, Vector2 size, TMP_FontAsset font)
    {
        GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(searchPanel.transform, false);

        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = anchoredPosition;
        buttonRect.sizeDelta = size;

        Image buttonImage = buttonObject.GetComponent<Image>();
        buttonImage.color = new Color(0.79f, 0.24f, 0.2f, 1f);

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = buttonImage;

        CreateButtonLabel(buttonObject.transform, buttonLabel, font);
        return button;
    }

    private void CreateButtonLabel(Transform parent, string text, TMP_FontAsset font)
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
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
    }
}
