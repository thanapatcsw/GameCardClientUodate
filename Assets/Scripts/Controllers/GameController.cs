using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro; 

// =============================================================================
// GameController — ผู้ควบคุมหลักของเกมทั้งหมด
// -----------------------------------------------------------------------
// แบ่ง partial class ออกเป็น  5 ไฟล์ เพื่อความสะอาด:
//   • GameController.cs        → ส่วน Core (ตัวแปร, Awake, Update, SetupPlayers)
//   • GameController.Bank.cs   → ระบบธนาคารเหรียญกลาง (6 สี)
//   • GameController.Cards.cs  → การซื้อ/จอง/จั่วการ์ด
//   • GameController.Turns.cs  → การหมุนเทิร์น, เงื่อนไขชนะ
//   • GameController.Bots.cs   → Bot AI (Offline)
//   • GameController.Network.cs→ Online Sync ผ่าน Photon Fusion
// -----------------------------------------------------------------------
// ระบบหลัก:
//   - เล่น 2 โหมด: Online (ผ่าน Photon Fusion) และ Offline (ใช้ Bot)
//   - ควบคุม State Machine: รอเริ่ม → กำลังเล่น → จบเกม
//   - ผู้เล่น 2-4 คน ใน Seats 0-3
//   - คะแนนชนะ: ถึง winningScore (20 แต้ม) ก่อน EndTurn ใด
// =============================================================================

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
    // base seed สุ่มกระดานประจำแมตช์ (ใช้เฉพาะ online): มาจากชื่อห้อง → ทุกเครื่องในแมตช์ได้ค่าเดียวกัน
    // ทำให้สุ่มได้กระดานชุดเดียวกันทุกเครื่อง แต่ต่างกันทุกแมตช์ (offline สุ่มผ่าน Random ที่ re-seed ใน Awake แทน)
    private int boardRandomSeed;

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
    public float currentTurnTime;
    public int winningScore = 20; 
    public int currentRound = 1; 
    public int currentTurnDisplay = 1; 
    public int totalTurnCount = 0;    // ตัวแปรนับตามระบบโปรแกรม
    public int quizInterval = 5;      // ช่วงเวลาเรียกควิซในโหมดออฟไลน์
    public int onlineQuizTurnInterval = 5; // ใช้เป็นช่วง "รอบ" ของควิซในโหมดออนไลน์
    private bool isGameOver = false; 


    [Header("---- UI Alerts & Results ----")]
    public TextMeshProUGUI warningText; 
    public TextMeshProUGUI turnCountText; 
    public ResultScreenUI resultScreen; // หน้าต่างสรุปผลอเนกประสงค์
    [Header("---- Reserve Confirmation UI ----")]
    public GameObject confirmReservePanel;
    public CardDisplay pendingReserveCard;

    [Header("---- Card Preview Popup ----")]
    [Tooltip("popup แสดงการ์ดที่จองแบบใหญ่ (ผูก component CardPreviewPopup)")]
    public CardPreviewPopup cardPreviewPopup;

    [Header("---- Bot Settings ----")]
    [SerializeField] private float botTurnDelayMin = 0.5f;
    [SerializeField] private float botTurnDelayMax = 1.0f;
    [SerializeField] private float tutorialBotTurnDelayMin = 0.5f;
    [SerializeField] private float tutorialBotTurnDelayMax = 1.0f;
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
    private int[] pendingQuizTurnOrder; 

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
        public Vector3 WorldPosition; // ตำแหน่งจริงบนจอ (ใช้หาช่องซ้ายล่างสุดให้เจ้าของเครื่อง)
    }

    public bool IsOnlineMatchMode => isOnlineMatchMode;
    public int ActivePlayerCount => activePlayerCount;
    public int LocalPlayerSeatIndex => GetLocalPlayerUiIndex();

    // =============================================================================
    // Awake — เรียกครั้งแรกเมื่อ Object ถูกสร้าง (= คล้าย Constructor)
    // -----------------------------------------------------------------------
    // ขั้นตอน:
    //   1. เช็คว่าเล่นโหมด Online หรือ Offline (IsMatchedOnlineSession)
    //   2. Subscribe events จาก FusionManager (Network callbacks)
    //   3. SetupPlayers — กำหนดชื่อ/ตัวละคร/สล็อตผู้เล่น
    //   4. ConfigureBankCoins — กำหนดจำนวนเหรียญตามจำนวนผู้เล่น
    //   5. SpawnResourceBank — สร้าง UI ปุ่มเหรียญสีต่างๆ 6 ปุ่ม
    //   6. LoadCardDatabase — โหลด CardData จาก JSON
    //   7. PopulateBoard — แจกการ์ดลงกระดาน Tier 1/2/3 (4 ใบต่อ tier)
    //   8. Setup ขุนนาง (Noble) ผ่าน NobleManager
    // =============================================================================
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
            FusionManager.Instance.PlayerCharacterReceived += ApplyRemoteCharacterPortrait;
            FusionManager.Instance.PlayerFrameReceived += ApplyRemoteNameFrame;
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

        // สุ่มกระดานให้ไม่ซ้ำเดิมทุกเกม:
        //   • online : base seed มาจากชื่อห้อง (Photon session) → ทุกเครื่องในแมตช์ได้ค่าเดียวกันตั้งแต่ Awake
        //              จึง pre-populate การ์ดตรงกัน (ไม่กระพริบ) แต่ต่างกันทุกแมตช์
        //   • offline: re-seed Random ใหม่ทุกเกม → กระดานต่างกันทุกเกม (แม้เล่นหลายเกมในรันเดียว)
        if (isOnlineMatchMode)
        {
            boardRandomSeed = GetOnlineBoardSeed();
        }
        else
        {
            Random.InitState(System.Environment.TickCount);
        }

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
    public void LeaveToMainMenu()
    {
        GameLog.Log("[GameController] Leaving match and returning to main menu...");

        // 0. [Reconnect] ถ้าเราเป็นคนสุดท้ายในห้อง → ปิด session + ห้องทันที (ยิง REST ก่อนตัดเน็ต/เปลี่ยนฉาก)
        //    ถ้ายังมีคนอื่นเหลือ → ไม่ปิด เขาเล่นต่อได้ ; เคส crash ยังมี cron เป็น safety net
        AbandonMatchSessionIfLastLeaver();
        if (FusionManager.Instance != null && FusionManager.Instance.ActivePlayerCount <= 1)
        {
            FusionManager.Instance.SetRoomStatus("finished");
        }

        // 1. ล้างสถานะเกมทั้งหมด
        PlayerPrefs.DeleteKey("GameMode");
        PlayerPrefs.DeleteKey("MatchmakingRoomCode");
        PlayerPrefs.Save();

        // 2. ปิดระบบเน็ตเวิร์ก (ถ้ามี)
        if (FusionManager.Instance != null)
        {
            FusionManager.Instance.IsGameInProgress = false; 
            FusionManager.Instance.Disconnect();
        }

        // 3. กลับหน้าเมนูหลัก (แก้ชื่อฉากเป็น "MainMenu 1" ตามที่เห็นใน Project)
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu 1");
    }

    // =============================================================================
    // Start — เรียกหลัง Awake เมื่อทุก Script ระหว่าง Awake เสร็จหมด
    // — ตรวจว่าต้องรอคู่แข่งโหมด Online หรือเริ่มเกมได้เลย
    // =============================================================================
    void Start()
    {
        ApplyNetworkPlayerNamesToUi();
        UpdateTurnCountUI();

        if (isOnlineMatchMode && FusionManager.Instance != null)
        {
            StartCoroutine(DelayedSyncLocalProfile());
        }

        if (ShouldWaitForOnlineOpponent())
        {
            ShowWarning("Waiting for opponent...");
            GameLog.Log("[GameController] Waiting for the second online player to join before starting gameplay.");
            return;
        }

        StartInitialGameplay();
    }

    // =============================================================================
    // StartInitialGameplay — จุดส่งตัวเกม (เรียกครั้งเดียว)
    // -----------------------------------------------------------------------
    // Online Host   → Broadcast สถานะกระดาน/เศรษฐกิจ/เทิร์นให้ทุกคน
    // Online Client → ขอ Full State จาก Host (late-joiner sync)
    // Offline       → เริ่มควิซรอบแรกทันที
    // =============================================================================
    private void StartInitialGameplay()
    {
        if (hasStartedInitialGameplay)
        {
            return;
        }

        hasStartedInitialGameplay = true;
        ClearWarning();

        // [Log→DB] เริ่มเกม — roll match_id ใหม่ (กัน room_id ซ้ำข้ามแมตช์) + ค่าตั้งต้นที่เชื่อถือได้ตั้งแต่ต้น
        //   หมายเหตุ: roster (names/seat) ย้ายไป log ที่ game_end แทน เพราะที่นี่ (StartInitialGameplay)
        //   online ชื่อ/seat ยังไม่ sync เสร็จ → ได้ placeholder + localSeat ผิด (ดู game_end)
        GameLogger.BeginMatch();
        GameLogger.Log("game_start", new GameLogger.Payload()
            .Add("players", activePlayerCount)
            .Add("winScore", winningScore)
            .Add("online", isOnlineMatchMode)
            .Add("build", Application.version));

        // [Reconnect/ข้อ1] authority สร้างแถว match_sessions + เก็บ snapshot กระดานก้อนแรก
        BeginMatchSessionIfAuthority();
        if (FusionManager.Instance != null && isOnlineMatchMode)
        {
            FusionManager.Instance.IsGameInProgress = true;
        }

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

        // [Reconnect] join กลางเกม → อย่าเริ่ม "ควิซรอบแรก" (เกมดำเนินไปแล้ว)
        //   สถานะควิซ/เทิร์นปัจจุบันจะมาจาก full-state sync ที่ขอไปแล้ว (RequestFullState ด้านบน)
        if (ReconnectManager.ConsumeReconnectFlag())
        {
            GameLog.Log("[GameController] Reconnect join → ข้ามควิซรอบแรก (ดึงสถานะจาก host แทน)");
        }
        else if (QuizManager.Instance != null)
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

    // ล้าง subscriptions ทุกตัวเมื่อ Scene ถูกทำลาย — ป้องกัน memory leak
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
            FusionManager.Instance.PlayerCharacterReceived -= ApplyRemoteCharacterPortrait;
            FusionManager.Instance.PlayerFrameReceived -= ApplyRemoteNameFrame;
        }
    }

    // Online callbacks (HandleFullStateRequested, IsMatchedOnlineSession, ShouldWaitForOnlineOpponent,
    // HandleFusionActivePlayersChanged, HandleOnlineTurnStateReceived, HandleOnlineEconomyStateReceived)
    // → moved to GameController.Network.cs

    // SetupNobles → moved to NobleManager.Setup() (Assets/Scripts/Controllers/NobleManager.cs)

    // =============================================================================
    // Update — Loop ที่สำคัญ: นับถอยเวลา Turn Timer + อัปเดต Timebar UI
    // -----------------------------------------------------------------------
    // ถ้าหมดเวลา (currentTurnTime ≤ 0) → บังคับ ForceEndTurn ทันที
    // Guard เงื่อนไข:
    //   - isGameOver        = เกมจบแล้ว
    //   - isGameplayInputLocked = ระหว่างตอบควิซ
    //   - isWaitingForContinue  = รอกด Continue หลังดูผล
    // =============================================================================
    void Update()
    {
        if (isGameOver) return;
        if (playOrder == null || playOrder.Length == 0) return;
        if (isGameplayInputLocked) return;
        if (isWaitingForContinueAfterResult) return;
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
                // [Online] ให้ authority คนเดียวเป็นคนขับ timeout → กันทุกเครื่องแข่งกัน ForceEndTurn
                //   พร้อมกันแล้วเทิร์นเลื่อนซ้อน (ข้าม 2 seat) หรือ noble/turn-count เพี้ยน.
                //   non-authority แค่ค้าง timer ไว้ที่ 0 รอ turn-state broadcast ของ authority มา reset ให้เอง
                if (isOnlineMatchMode && FusionManager.Instance != null && !FusionManager.Instance.IsMasterClient)
                {
                    currentTurnTime = 0f;
                    return;
                }
                GameLog.Log($"[GameController] หมดเวลาในเทิร์นของผู้เล่น {playOrder[currentPlayerIndex] + 1}");
                ShowWarning($"[ผู้เล่น {playOrder[currentPlayerIndex] + 1}] หมดเวลา! บังคับข้ามเทิร์น");
                ClearPendingCoins();
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
                        bool hasKnownCharacter = false;
                        if (isOnlineMatchMode && FusionManager.Instance != null && 
                            FusionManager.Instance.TryGetPlayerCharacterBySeat(i, out int knownCharIndex))
                        {
                            int clampedIndex = Mathf.Clamp(knownCharIndex, 0, availableCharacters.Length - 1);
                            CharacterData remoteData = availableCharacters[clampedIndex];
                            if (players[i].characterPortrait != null && remoteData.portraitSprite != null)
                            {
                                players[i].characterPortrait.sprite = remoteData.portraitSprite;
                                hasKnownCharacter = true;
                                remainingChars.Remove(remoteData);
                            }
                        }

                        // ถ้ายังไม่มีข้อมูล (เพิ่งเข้าห้องยังไม่ได้รับ message CHAR) -> สุ่มไปก่อน
                        if (!hasKnownCharacter && remainingChars.Count > 0) {
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

                // --- ใส่กรอบชื่อจากร้านค้า ---
                bool isLocalSeat = !isOnlineMatchMode
                    ? i == 0
                    : i == localPlayerSlotIndex;
                    
                if (isLocalSeat && !players[i].isBot)
                {
                    Sprite frameSprite = ShopManager.LoadEquippedFrameSprite();
                    players[i].ApplyNameFrame(frameSprite, Color.white);
                }
                else if (isOnlineMatchMode && !players[i].isBot && FusionManager.Instance != null)
                {
                    // ลองดึงกรอบของผู้เล่นอื่นที่เคยโหลดไว้แล้วมาใส่
                    int remotePlayerId = FusionManager.Instance.GetPlayerIdBySeat(i);
                    if (remotePlayerId >= 0 && FusionManager.Instance.TryGetPlayerFrame(remotePlayerId, out string frameId) && !string.IsNullOrEmpty(frameId))
                    {
                        Sprite frameSprite = Resources.Load<Sprite>($"Frames/{frameId}");
                        if (frameSprite != null)
                        {
                            players[i].ApplyNameFrame(frameSprite, Color.white);
                        }
                        else
                        {
                            players[i].HideNameFrame();
                        }
                    }
                    else
                    {
                        // ยังไม่มีข้อมูลจาก network → ใช้ default frame ไปก่อน
                        Sprite defaultFrame = Resources.Load<Sprite>($"Frames/{ShopManager.DEFAULT_FRAME}");
                        if (defaultFrame != null)
                            players[i].ApplyNameFrame(defaultFrame, Color.white);
                        else
                            players[i].HideNameFrame();
                    }
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

    // [CHEAT] ปุ่มโกง (เดิม "เพิ่มคะแนน" → เปลี่ยนเป็น "ลดราคาการ์ด"): กดแล้วการ์ดทุกใบบนกระดานเป็นฟรี
    //   หมายเหตุ: ตัวเลขราคาบนการ์ดฝังในรูป (sprite) จึงไม่เปลี่ยนตามภาพ แต่ "ราคาซื้อจริง" เป็น 0 แล้ว
    //   (มิวเทต CardData ที่แชร์กัน → ถ้าเริ่มเกมใหม่ในรันเดียว การ์ดใบเดิมอาจยังถูกอยู่; รีสตาร์ทแอปคืนค่าปกติ)
    //   ชื่อเมธอดคงเดิมเพื่อให้ OnClick ของปุ่มยังผูกอยู่ (ไม่ต้องแก้ไฟล์ซีน)
    public void TestAddScoreToPlayer1()
    {
        int freedCards = 0;
        FreeBoardCardCosts(tier1Container, ref freedCards);
        FreeBoardCardCosts(tier2Container, ref freedCards);
        FreeBoardCardCosts(tier3Container, ref freedCards);

        ShowWarning($"[โกง] ลดราคาการ์ดบนกระดานเป็นฟรีแล้ว ({freedCards} ใบ)");
        GameLog.Log($"[Cheat] ลดราคาการ์ดบนกระดานเป็นฟรี ({freedCards} ใบ)");
    }

    // ตั้งราคาการ์ดทุกใบใน container เป็น 0 (ใช้โดยปุ่มโกงลดราคา)
    private void FreeBoardCardCosts(Transform container, ref int count)
    {
        if (container == null) return;
        foreach (Transform child in container)
        {
            CardDisplay cd = child.GetComponent<CardDisplay>();
            if (cd == null || cd.data == null || cd.data.costs == null) continue;
            for (int i = 0; i < cd.data.costs.Length; i++) cd.data.costs[i] = 0;
            count++;
        }
    }

    // CheckNobles → moved to NobleManager.CheckClaim() (Assets/Scripts/Controllers/NobleManager.cs)
}

