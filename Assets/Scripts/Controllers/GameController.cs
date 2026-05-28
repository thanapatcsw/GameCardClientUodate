using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro; 

// [Refactor] ใช้ partial class แยกความรับผิดชอบของ GameController เป็นหลายไฟล์
//   - GameController.cs        : core (fields, lifecycle, state)
//   - GameController.Bots.cs   : bot AI execution
//   - GameController.Network.cs: online/Fusion sync (TODO)
//   - GameController.Cards.cs  : card/bank/board interaction (TODO)
public partial class GameController : MonoBehaviour
{
    private const string MatchmakingRoomCodePrefsKey = "MatchmakingRoomCode";
    private const string MatchmakingTargetPlayerCountPrefsKey = "MatchmakingTargetPlayerCount";

    [Header("---- Board & Prefabs ----")]
    public Transform tier3Container; 
    public Transform tier2Container; 
    public Transform tier1Container;
    public GameObject cardPrefab; 
    public GameObject resourcePrefab; 
    public Transform resourceBankContainer;

    [Header("---- Noble Board ----")]
    public GameObject noblePrefab; // หน้าตาการ์ดขุนนาง
    public Transform leftNobleContainer; // จุดวาง 2 ใบซ้าย
    public Transform rightNobleContainer; // จุดวาง 2 ใบขวา

    [Header("---- Noble Database ----")]
    public List<NobleData> masterNobles; // ขุนนางทั้งหมด 8 ใบที่มี

    // ระบบขุนนางถูกแยกออกไปเป็น NobleManager (helper class) — ดู Assets/Scripts/Controllers/NobleManager.cs
    // GameController คงเก็บแค่ Inspector field (noblePrefab/left/rightContainer/masterNobles)
    // แล้วส่งต่อให้ NobleManager ตอน StartInitialGameplay
    private NobleManager nobleManager;

    [Header("---- Card Database (โหลดอัตโนมัติจาก JSON) ----")]
    [HideInInspector] public List<CardData> tier3Cards;
    [HideInInspector] public List<CardData> tier2Cards;
    [HideInInspector] public List<CardData> tier1Cards;
    private HashSet<string> usedCardIds = new HashSet<string>();

    [Header("---- Player Management ----")]
    public PlayerUI[] players;
    public int currentPlayerIndex = 0; // หมายถึงคิวที่ 0, 1, 2, 3
    public int[] playOrder = new int[] { 0, 1, 2, 3 }; // เก็บข้อมูลว่าคิวนั้นๆ คือผู้เล่นคนไหน

    [Header("---- Resources Management (Bank) ----")]
    public int[] pendingCoins = new int[6]; 
    public List<ResourceButton> bankButtons = new List<ResourceButton>();
    
    public int[] bankCoins = new int[6] { 7, 7, 7, 7, 7, 5 }; 

    [Header("---- Turn Timer & Rules ----")]
    public float turnDuration = 30f; 
    private float currentTurnTime;
    public int winningScore = 20; 
    public int currentRound = 1; 
    public int currentTurnDisplay = 1; // [NEW] เลขเทิร์นสำหรับเอาไปติดใน UI (นับ 1, 2, 3...)
    public int totalTurnCount = 0;    // ตัวแปรนับตามระบบโปรแกรม
    public int quizInterval = 5;      // ช่วงเวลาเรียกควิซในโหมดออฟไลน์
    public int onlineQuizTurnInterval = 5; // ใช้เป็นช่วง "รอบ" ของควิซในโหมดออนไลน์
    private bool isGameOver = false; 


    [Header("---- UI Alerts & Results ----")]
    public TextMeshProUGUI warningText; 
    public TextMeshProUGUI turnCountText; // [NEW] ลาก Text บอกลำดับเทิร์นมาใส่ที่นี่
    public ResultScreenUI resultScreen; // หน้าต่างสรุปผลอเนกประสงค์
    [Header("---- Reserve Confirmation UI ----")]
    public GameObject confirmReservePanel; 
    public CardDisplay pendingReserveCard;

    [Header("---- Bot Settings ----")]
    [SerializeField] private float botTurnDelayMin = 0.5f;
    [SerializeField] private float botTurnDelayMax = 1.5f;
    private BotController botController;
    private Coroutine botTurnCoroutine;
    private bool isExecutingBotTurn;
    private bool isGameplayInputLocked;
    private bool isWaitingForContinueAfterResult;
    private bool isOnlineMatchMode;
    private int activePlayerCount = 4;
    private bool hasStartedInitialGameplay;
    private int localPlayerSlotIndex;
    private bool playerPanelLayoutsCaptured;
    private PlayerPanelLayout[] capturedPlayerPanelLayouts;
    private int[] pendingQuizTurnOrder; // [FIX] เก็บ Turn Order จาก Quiz ชั่วคราวก่อน Result ปิด

    private struct PlayerPanelLayout
    {
        public Vector2 AnchoredPosition;
        public Vector2 SizeDelta;
        public Vector2 AnchorMin;
        public Vector2 AnchorMax;
        public Vector2 Pivot;
        public Vector3 LocalScale;
        public Quaternion LocalRotation;
        public int SiblingIndex;
    }

    public bool IsOnlineMatchMode => isOnlineMatchMode;
    public int ActivePlayerCount => activePlayerCount;
    public int LocalPlayerSeatIndex => GetLocalPlayerUiIndex();

    void Awake()
    {
        isOnlineMatchMode = IsMatchedOnlineSession();
        activePlayerCount = isOnlineMatchMode ? GetConfiguredOnlinePlayerCount() : 4;

        if (FusionManager.Instance != null)
        {
            FusionManager.Instance.PlayerNamesUpdated += ApplyNetworkPlayerNamesToUi;
            FusionManager.Instance.ActivePlayersChanged += HandleFusionActivePlayersChanged;
            FusionManager.Instance.TurnStateReceived += HandleOnlineTurnStateReceived;
            FusionManager.Instance.EconomyStateReceived += HandleOnlineEconomyStateReceived;
            FusionManager.Instance.BoardStateReceived += HandleOnlineBoardStateReceived;
            FusionManager.Instance.FullStateRequested += HandleFullStateRequested;
        }

        EnsureBotController();
        // Setup UI ทุกอย่างก่อน เสมอ ไม่ว่าจะมี cardPrefab หรือไม่
        if (confirmReservePanel != null) confirmReservePanel.SetActive(false);
        if (resultScreen != null) resultScreen.onClosed = OnResultScreenClosed;
        ClearWarning();
        SetupPlayers();
        ConfigureBankCoinsByPlayerCount();

        // Setup เหรียญในธนาคาร
        if (resourcePrefab != null && resourceBankContainer != null)
        {
            SpawnResourceBank();
        }
        else if (resourceBankContainer != null)
        {
            // กรณีเหรียญถูกวางมือไว้ใน Scene แล้ว (ไม่มี resourcePrefab)
            // ไปเก็บ ResourceButton ที่อยู่ใน container มาใส่ bankButtons
            GameLog.Log("[GameController] ไม่มี resourcePrefab → ใช้เหรียญที่วางมือไว้ใน ResourceBankPanel");
            bankButtons.Clear();
            foreach (Transform child in resourceBankContainer)
            {
                ResourceButton btn = child.GetComponent<ResourceButton>();
                if (btn != null)
                {
                    bankButtons.Add(btn);
                    GameLog.Log($"[GameController] พบเหรียญที่วางไว้: {btn.resourceType}");
                }
            }
        }
        else
        {
            Debug.LogWarning("[GameController] resourceBankContainer ยังไม่ได้ผูก → ข้ามการสร้างเหรียญ");
        }

        // โหลดข้อมูลการ์ดจาก JSON อัตโนมัติ
        LoadCardDatabase();

        // Setup กระดานไพ่ (ต้องการ cardPrefab)
        // ทุกคนสุ่มกระดานของตัวเองก่อนเสมอ เพื่อให้เห็นการ์ดทันที (ไม่พึ่ง timing ของ IsMasterClient)
        // ในโหมดออนไลน์ Host จะ broadcast BoardStateSnapshot ตามมาเพื่อ reconcile ให้ทุกเครื่องตรงกัน
        if (cardPrefab != null)
            PopulateBoard();
        else
            Debug.LogWarning("[GameController] cardPrefab ยังไม่ได้ผูก → ข้ามการสร้างการ์ดบน Board");

        // Setup ขุนนาง (delegate ไป NobleManager)
        if (noblePrefab != null && masterNobles != null && masterNobles.Count > 0)
        {
            nobleManager = new NobleManager(noblePrefab, leftNobleContainer, rightNobleContainer, masterNobles);
            nobleManager.Setup();
        }
        else
        {
            Debug.LogWarning("[GameController] ยังไม่ได้ผูก noblePrefab หรือ masterNobles → ข้ามการสร้างขุนนาง");
        }

        GameLog.Log($"\n========== เริ่มเกม: รอบที่ {currentRound} ==========\n");
        ResetTimer();
        UpdateTurnVisuals();
        UpdateBankUI();
    }

    // [NEW] ฟังก์ชันสำหรับปุ่ม Exit หรือ Leave Room (สำหรับลากไปใส่ OnClick ของปุ่ม)
    public void LeaveToMainMenu()
    {
        GameLog.Log("[GameController] Leaving match and returning to main menu...");

        // 1. ล้างสถานะเกมทั้งหมด
        PlayerPrefs.DeleteKey("GameMode");
        PlayerPrefs.DeleteKey("MatchmakingRoomCode");
        PlayerPrefs.Save();

        // 2. ปิดระบบเน็ตเวิร์ก (ถ้ามี)
        if (FusionManager.Instance != null)
        {
            FusionManager.Instance.Disconnect();
        }

        // 3. กลับหน้าเมนูหลัก (แก้ชื่อฉากเป็น "MainMenu 1" ตามที่เห็นใน Project)
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu 1");
    }

    void Start()
    {
        ApplyNetworkPlayerNamesToUi();
        UpdateTurnCountUI();

        if (ShouldWaitForOnlineOpponent())
        {
            ShowWarning("Waiting for opponent...");
            GameLog.Log("[GameController] Waiting for the second online player to join before starting gameplay.");
            return;
        }

        StartInitialGameplay();
    }

    private void StartInitialGameplay()
    {
        if (hasStartedInitialGameplay)
        {
            return;
        }

        hasStartedInitialGameplay = true;
        ClearWarning();

        if (isOnlineMatchMode && FusionManager.Instance != null && FusionManager.Instance.IsMasterClient)
        {
            PublishOnlineBoardState();
            PublishOnlineEconomyState();
            PublishOnlineTurnState();
        }
        else if (isOnlineMatchMode && FusionManager.Instance != null)
        {
            // client พร้อมแล้ว (subscribe event ครบ + ฉากโหลดเสร็จ) → ขอ full state ปัจจุบันจาก host
            // กัน late-joiner desync: ดึงเอง แทนการพึ่ง broadcast ของ host ที่อาจมาถึงก่อน client subscribe
            FusionManager.Instance.RequestFullState();
        }

        if (QuizManager.Instance != null)
        {
            if (!isOnlineMatchMode)
            {
                // offline: เริ่มควิซรอบแรกได้ทันที
                GameLog.Log("[GameController] Starting first-round quiz.");
                QuizManager.Instance.StartQuiz();
            }
            else if (FusionManager.Instance != null && FusionManager.Instance.IsMasterClient)
            {
                // Host: ยังไม่เริ่มควิซรอบแรกตอนนี้ เพราะ client อาจโหลดฉากยังไม่เสร็จ (จะ broadcast หลุด)
                // รอจน client ส่ง RequestQuizStart เข้ามา (= พร้อมรับแล้ว) ค่อยเริ่ม
                GameLog.Log("[GameController] Host waiting for a client to request the first-round quiz...");
            }
            else if (FusionManager.Instance != null)
            {
                // Client: เข้าโหมดรอ (เผื่อ Host เคย broadcast มาแล้วจะ consume buffer) + บอก Host ว่าพร้อมเริ่มได้
                GameLog.Log("[GameController] Client ready → requesting first-round quiz from Host.");
                QuizManager.Instance.StartQuiz();
                FusionManager.Instance.RequestQuizStart();
            }
        }
        else
        {
            ScheduleBotTurnIfNeeded();
        }
    }

    void OnDestroy()
    {
        if (FusionManager.Instance != null)
        {
            FusionManager.Instance.PlayerNamesUpdated -= ApplyNetworkPlayerNamesToUi;
            FusionManager.Instance.ActivePlayersChanged -= HandleFusionActivePlayersChanged;
            FusionManager.Instance.TurnStateReceived -= HandleOnlineTurnStateReceived;
            FusionManager.Instance.EconomyStateReceived -= HandleOnlineEconomyStateReceived;
            FusionManager.Instance.BoardStateReceived -= HandleOnlineBoardStateReceived;
            FusionManager.Instance.FullStateRequested -= HandleFullStateRequested;
        }
    }

    // Online callbacks (HandleFullStateRequested, IsMatchedOnlineSession, ShouldWaitForOnlineOpponent,
    // HandleFusionActivePlayersChanged, HandleOnlineTurnStateReceived, HandleOnlineEconomyStateReceived)
    // → moved to GameController.Network.cs

    // SetupNobles → moved to NobleManager.Setup() (Assets/Scripts/Controllers/NobleManager.cs)

    void Update()
    {
        if (isGameOver) return;
        if (playOrder == null || playOrder.Length == 0) return;
        if (isGameplayInputLocked) return;
        if (isWaitingForContinueAfterResult) return;

        // [FIX] ป้องกัน Index Out of Range ถ้าคิวการเล่นผิดพลาด
        if (currentPlayerIndex < 0 || currentPlayerIndex >= playOrder.Length)
        {
            Debug.LogWarning($"[GameController] currentPlayerIndex {currentPlayerIndex} out of bounds (playOrder.Length={playOrder.Length}). Resetting to 0.");
            currentPlayerIndex = 0;
            UpdateTurnVisuals();
        }

        if (currentTurnTime > 0) 
        {
            currentTurnTime -= Time.deltaTime;
            
            // อัปเดต Timebar เฉพาะของคนที่กำลังเล่นอยู่
            int activeIdx = playOrder[currentPlayerIndex];
            if (activeIdx >= 0 && activeIdx < players.Length && players[activeIdx] != null)
            {
                players[activeIdx].UpdateTimerBar(currentTurnTime / turnDuration);
            }
            
            if (currentTurnTime <= 0) 
            {
                GameLog.Log($"[GameController] หมดเวลาในเทิร์นของผู้เล่น {playOrder[currentPlayerIndex] + 1}");
                ShowWarning($"[ผู้เล่น {playOrder[currentPlayerIndex] + 1}] หมดเวลา! บังคับข้ามเทิร์น");
                ClearPendingCoins();
                // [FIX] ใช้ ForceEndTurn แทน EndTurn เพื่อให้ Host สามารถบังคับจบเทิร์นได้แม้เมื่อ:
                // (1) เป็นเทิร์นของ Remote Player หรือ Bot ที่กำลังรอ Delay
                // (2) ผู้เล่นหลุดกลางเทิร์น — ไม่โดนบล็อกจาก IsLocalPlayersTurn()
                ForceEndTurn();
            }
        }
    }

    // Turn order, state setters, and action guards
    // (ApplyNewTurnOrder, SetGameplayInputLocked, SetWaitingForContinueAfterResult,
    // OnResultScreenClosed, SetPendingQuizTurnOrder, IsGameplayInputLocked,
    // BlockActionDuringQuiz, BlockActionUntilContinue, IsLocalPlayersTurn,
    // BlockActionOutsideLocalTurn) → moved to GameController.Turns.cs

    public void ShowWarning(string msg)
    {
        if (warningText != null) warningText.text = msg; 
    }

    public void ClearWarning()
    {
        if (warningText != null) warningText.text = "";
    }

    // Bank/Resource methods (UpdateBankUI, ClearPendingCoins, Get/Spend coin helpers,
    // OnResourceClicked) → moved to GameController.Bank.cs

    // Card interaction (OnCardClicked, PromptReserveCard, ConfirmReserve, CancelReserve,
    // ExecuteReserve, BuyReservedCard) → moved to GameController.Cards.cs

    // EndTurn, EvaluateWinCondition, CheckWinCondition, ResetTimer, UpdateTurnVisuals
    // → moved to GameController.Turns.cs

    // PopulateBoard, DrawNewCard, LoadCardDatabase → moved to GameController.Cards.cs

    // Bot AI execution methods → moved to GameController.Bots.cs

    // GetResourceIndex, GetConfiguredPlayerCount, ConfigureBankCoinsByPlayerCount,
    // SpawnResourceBank → moved to GameController.Bank.cs

    [Header("---- Character System ----")]
    public CharacterData[] availableCharacters; // ใส่ข้อมูลตัวละครที่มีทั้งหมด

    void SetupPlayers() { 
        if (players == null || availableCharacters == null || availableCharacters.Length == 0) {
            Debug.LogWarning("[GameController] SetupPlayers aborted: Missing players array or availableCharacters database.");
            return; 
        }

        string humanName = GetConfiguredLocalPlayerName();

        // อ่านค่าที่ผู้เล่นเลือกมาจากหน้า Main Menu (ค่าตั้งต้นคือ 0)
        int selectedCharIndex = PlayerPrefs.GetInt("SelectedCharacter", 0);

        // ดึงตัวละครที่เลือกมาให้ Player 1
        CharacterData p1Data = availableCharacters[Mathf.Clamp(selectedCharIndex, 0, availableCharacters.Length - 1)];

        // สร้างลิสต์ของตัวละครที่เหลือไว้สุ่มให้บอท
        List<CharacterData> remainingChars = new List<CharacterData>(availableCharacters);
        remainingChars.Remove(p1Data);

        int humanPlayerCount = 1;
        if (isOnlineMatchMode)
        {
            humanPlayerCount = Mathf.Min(GetConfiguredOnlinePlayerCount(), players.Length);
            activePlayerCount = humanPlayerCount;
            playOrder = new int[humanPlayerCount];
            for (int seatIndex = 0; seatIndex < humanPlayerCount; seatIndex++)
            {
                playOrder[seatIndex] = seatIndex;
            }
            currentPlayerIndex = 0;
            localPlayerSlotIndex = GetResolvedLocalPlayerSlotIndex();
            GameLog.Log($"[GameController] Online PvP mode detected. Human player count={humanPlayerCount}, bots disabled.");
        }
        else
        {
            activePlayerCount = Mathf.Clamp(players.Length, 2, 4);
            localPlayerSlotIndex = 0;
        }

        for (int i = 0; i < players.Length; i++) 
        {
            if (players[i] != null) 
            {
                bool isActiveSeat = i < activePlayerCount;
                players[i].gameObject.SetActive(isActiveSeat);

                if (!isActiveSeat)
                {
                    players[i].isBot = false;
                    continue;
                }

                players[i].isBot = !isOnlineMatchMode && (i >= humanPlayerCount); 
                string finalName = "Player " + (i + 1); // บังคับเป็น Player 1, 2, 3, 4 ไว้ก่อน

                if (!players[i].isBot) {
                    bool isLocalHumanSeat = !isOnlineMatchMode || i == localPlayerSlotIndex;
                    if (isLocalHumanSeat)
                    {
                        finalName = humanName;
                        if (players[i].characterPortrait != null) players[i].characterPortrait.sprite = p1Data.portraitSprite;
                        GameLog.Log($"[GameController] Local player setup in slot {i + 1} as: {finalName} with character {p1Data.characterName}");
                    }
                    else
                    {
                        finalName = GetOnlinePlayerDisplayNameForSeat(i);
                        if (remainingChars.Count > 0) {
                            int r = Random.Range(0, remainingChars.Count);
                            CharacterData remoteData = remainingChars[r];
                            if (players[i].characterPortrait != null) players[i].characterPortrait.sprite = remoteData.portraitSprite;
                            remainingChars.RemoveAt(r);
                        }
                        GameLog.Log($"[GameController] Remote player slot {i + 1} configured as human.");
                    }
                } else {
                    // บอท: ลองหาชื่อจากไฟล์ตัวละคร ถ้าไม่มีให้ใช้ "Player X"
                    if (remainingChars.Count > 0) {
                        int r = Random.Range(0, remainingChars.Count);
                        CharacterData botData = remainingChars[r];
                        if (!string.IsNullOrEmpty(botData.characterName)) finalName = botData.characterName;
                        if (players[i].characterPortrait != null) players[i].characterPortrait.sprite = botData.portraitSprite;
                        remainingChars.RemoveAt(r);
                    }
                }
                players[i].SetupPlayer(finalName);

                // --- ใส่กรอบชื่อจากร้านค้า (เฉพาะผู้เล่นจริง local) ---
                bool isLocalSeat = !isOnlineMatchMode
                    ? i == 0
                    : i == localPlayerSlotIndex;
                if (isLocalSeat && !players[i].isBot)
                {
                    Sprite frameSprite = ShopManager.LoadEquippedFrameSprite();
                    // สีกรอบดึงจาก ShopItemData ถ้าหาสีตรงได้ หรือใช้ White เป็น default
                    players[i].ApplyNameFrame(frameSprite, Color.white);
                }
                else
                {
                    players[i].HideNameFrame();
                }
            }
        }

        // เลื่อนการ capture+apply ตำแหน่ง panel ไปหลัง Canvas จัด layout เสร็จ (end of frame)
        // เพื่อให้ Editor และ Build จับตำแหน่งจาก state ที่ settle เหมือนกัน (กัน UI เพี้ยนใน build)
        if (panelLayoutCoroutine != null) StopCoroutine(panelLayoutCoroutine);
        panelLayoutCoroutine = StartCoroutine(ConfigureOnlinePlayerPanelLayoutDeferred());
    }

    // panelLayoutCoroutine field + ConfigureOnlinePlayerPanelLayoutDeferred → moved to GameController.Network.cs

    // ClearContainer → moved to GameController.Cards.cs

    // All online sync methods (ApplyNetworkPlayerNamesToUi, GetOnlinePlayerDisplayNameForSeat,
    // Get/Resolved/LocalPlayerSlotIndex, GetLocalPlayerUiIndex, GetConfiguredLocalPlayerName,
    // PublishOnlineTurnState/Economy/Board, Build/Apply Economy/Board snapshot, RebuildTierIfChanged,
    // FindCardDataById, ConfigureOnlinePlayerPanelLayout, CapturePlayerPanelLayoutsIfNeeded,
    // GetRotatedLayoutIndex, ApplyPlayerPanelLayout, GetConfiguredOnlinePlayerCount)
    // → moved to GameController.Network.cs

    // UpdateTurnCountUI → moved to GameController.Turns.cs

    // ==========================================
    // ส่วนของการทดสอบและระบบขุนนาง (Nobles)
    // ==========================================

    public void TestGiveBonusToPlayer1()
    {
        if (players.Length == 0 || players[0] == null) return;

        PlayerUI p1 = players[0];
        
        // สุ่มโบนัส 3 สี (0=CPU, 1=RAM, 2=Net, 3=Store, 4=Sec)
        for(int i = 0; i < 3; i++)
        {
            int randomColorIndex = Random.Range(0, 5);
            p1.AddBonus(randomColorIndex);
            GameLog.Log($"[Test] ให้โบนัสสีที่ {randomColorIndex} แก่ Player 1");
        }

        // เช็คว่าโบนัสพอที่จะเชิญขุนนางลงมาหาได้หรือยัง
        nobleManager?.CheckClaim(p1);
    }

    // [NEW] ฟังก์ชันสำหรับใช้เทสเพิ่มคะแนนให้ Player 1 ทีละ 10 คะแนน
    public void TestAddScoreToPlayer1()
    {
        if (players.Length == 0 || players[0] == null) return;

        players[0].currentScore += 10;
        if (players[0].scoreText != null) 
        {
            players[0].scoreText.text = players[0].currentScore.ToString();
        }

        GameLog.Log($"[Test] เพิ่มคะแนน 10 แต้มให้ Player 1. คะแนนรวม: {players[0].currentScore}");

        // อัปเดต Economy ให้ Host/Client ตรงกันถ้าเป็นโหมดออนไลน์
        if (isOnlineMatchMode)
        {
            PublishOnlineEconomyState();
        }
    }

    // CheckNobles → moved to NobleManager.CheckClaim() (Assets/Scripts/Controllers/NobleManager.cs)
}
