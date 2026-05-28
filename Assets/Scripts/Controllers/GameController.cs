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
    private CardDisplay pendingReserveCard; 

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
                EndTurn(); 
            }
        }
    }

    // ฟังก์ชันรับคิวการเล่นใหม่จากควิซ
    public void ApplyNewTurnOrder(int[] newOrder)
    {
        playOrder = newOrder; 
        currentPlayerIndex = 0; 
        
        GameLog.Log($"<color=orange>[GameController] บังคับใช้คิวการเล่นใหม่เรียบร้อย!</color>");
        
        ClearPendingCoins();
        UpdateTurnVisuals();
        ResetTimer();
        PublishOnlineTurnState();
        ScheduleBotTurnIfNeeded();
    }

    public void SetGameplayInputLocked(bool locked)
    {
        isGameplayInputLocked = locked;

        if (locked)
        {
            pendingReserveCard = null;
            if (confirmReservePanel != null) confirmReservePanel.SetActive(false);
            System.Array.Clear(pendingCoins, 0, 6);
            foreach (var btn in bankButtons) if (btn != null) btn.UpdatePendingUI(0);
        }
    }

    public void SetWaitingForContinueAfterResult(bool waiting)
    {
        isWaitingForContinueAfterResult = waiting;

        if (waiting)
        {
            pendingReserveCard = null;
            if (confirmReservePanel != null) confirmReservePanel.SetActive(false);
            System.Array.Clear(pendingCoins, 0, 6);
            foreach (var btn in bankButtons) if (btn != null) btn.UpdatePendingUI(0);
        }
    }

    public void OnResultScreenClosed()
    {
        SetWaitingForContinueAfterResult(false);
        SetGameplayInputLocked(false); // [FIX] ปลดล็อกให้แน่ใจว่ากลับมาเล่นต่อได้
        ClearWarning();
        
        // [FIX] ถ้ามี Turn Order ที่ Quiz ส่งมา ให้ Apply ก่อน แล้วค่อย Schedule Bot
        if (pendingQuizTurnOrder != null)
        {
            GameLog.Log("[GameController] Applying pending quiz turn order from OnResultScreenClosed");
            ApplyNewTurnOrder(pendingQuizTurnOrder);
            pendingQuizTurnOrder = null;
        }
        else
        {
            ScheduleBotTurnIfNeeded();
        }
    }

    public void SetPendingQuizTurnOrder(int[] newOrder)
    {
        pendingQuizTurnOrder = newOrder;
        GameLog.Log("[GameController] Stored pending quiz turn order");
    }

    public bool IsGameplayInputLocked()
    {
        return isGameplayInputLocked;
    }

    private bool BlockActionDuringQuiz()
    {
        if (!isGameplayInputLocked) return false;

        ShowWarning("กรุณาตอบคำถามก่อน จึงจะกดปุ่มอื่นได้");
        return true;
    }

    private bool BlockActionUntilContinue()
    {
        if (!isWaitingForContinueAfterResult) return false;

        ShowWarning("กรุณากดเริ่มเกมต่อก่อน");
        return true;
    }

    private bool IsLocalPlayersTurn()
    {
        if (players == null || players.Length == 0) return false;
        if (playOrder == null || playOrder.Length == 0) return false;
        if (currentPlayerIndex < 0 || currentPlayerIndex >= playOrder.Length) return false;

        int localSeatIndex = GetLocalPlayerUiIndex();
        if (localSeatIndex < 0 || localSeatIndex >= players.Length) return false;
        if (players[localSeatIndex] == null || players[localSeatIndex].isBot) return false;

        return playOrder[currentPlayerIndex] == localSeatIndex;
    }

    private bool BlockActionOutsideLocalTurn()
    {
        // 1. ถ้ากำลังรันโค้ดบอทอยู่ (ผ่าน Coroutine) -> ให้ผ่าน (ห้ามบล็อกบอทตัวเอง)
        if (isExecutingBotTurn) return false;

        // 2. ถ้าเป็นตาของบอท (แต่คนเล่นแอบมากด) -> บล็อก
        if (IsCurrentPlayerBot()) 
        {
            return true; 
        }

        // 3. ถ้าเป็นตาของผู้เล่นคนนี้ -> ให้ผ่าน
        if (IsLocalPlayersTurn()) return false;

        // 4. อื่นๆ (เช่น ตาของคนอื่นในโหมด Online หรือตาบอทที่ไม่ได้รันโค้ด) -> บล็อก + แจ้งเตือน
        ShowWarning("กดปุ่มได้เฉพาะในเทิร์นของคุณ");
        return true;
    }

    public void ShowWarning(string msg)
    {
        if (warningText != null) warningText.text = msg; 
    }

    public void ClearWarning()
    {
        if (warningText != null) warningText.text = "";
    }

    public void UpdateBankUI()
    {
        foreach (ResourceButton btn in bankButtons)
        {
            if (btn != null) {
                int index = GetResourceIndex(btn.resourceType);
                btn.UpdateRemainingUI(bankCoins[index]); 
            }
        }
    }

    public void ClearPendingCoins() 
    {
        if (BlockActionDuringQuiz()) return;
        if (BlockActionUntilContinue()) return;
        // [FIX] ไม่ block บอท — บอทเรียก ClearPendingCoins ภายในเทิร์นของตัวเอง
        System.Array.Clear(pendingCoins, 0, 6); 
        foreach (var btn in bankButtons) if (btn != null) btn.UpdatePendingUI(0);
        ClearWarning(); 
    }

    int GetTotalPendingCoins() 
    {
        int total = 0;
        for (int i = 0; i < 5; i++) total += pendingCoins[i];
        return total;
    }

    int GetTotalPlayerCoins(int playerIndex) 
    {
        int total = 0;
        for (int i = 0; i < 6; i++) total += players[playerIndex].coins[i];
        return total;
    }

    int SpendWildcardCoinsWithoutReturningQuizBlack(PlayerUI player, int amount)
    {
        if (player == null || amount <= 0)
        {
            return 0;
        }

        return player.SpendWildcardCoins(amount);
    }

    public void OnResourceClicked(ResourceButton clickedBtn)
    {
        if (BlockActionDuringQuiz()) return;
        if (BlockActionUntilContinue()) return;
        if (BlockActionOutsideLocalTurn()) return;
        if (isGameOver) return; 
        if (IsCurrentPlayerBot() && !isExecutingBotTurn) {
            ShowWarning("กำลังเป็นเทิร์นของบอท");
            return;
        }

        int index = GetResourceIndex(clickedBtn.resourceType);
        
        if (index == 5) {
            ShowWarning("หยิบเหรียญทองโดยตรงไม่ได้! ต้องใช้แอคชั่น 'จองการ์ด' เท่านั้น");
            return; 
        }

        if (bankCoins[index] - pendingCoins[index] <= 0) {
            ShowWarning("หยิบไม่ได้! เหรียญสีนี้ในกองกลางหมดแล้ว");
            return;
        }

        int totalPending = GetTotalPendingCoins();
        int totalPlayerCoins = GetTotalPlayerCoins(playOrder[currentPlayerIndex]); // เปลี่ยนเป็นเช็คคนเล่นตามคิว

        if (totalPlayerCoins + totalPending >= 10) {
            ShowWarning("หยิบไม่ได้! คุณถือเหรียญเกิน 10 อันไม่ได้ (ต้องกดซื้อการ์ดเพื่อใช้เหรียญก่อน)");
            return; 
        }

        bool hasDoublePick = false;
        for (int i = 0; i < 5; i++) if (pendingCoins[i] >= 2) hasDoublePick = true;
        
        if (hasDoublePick) {
            ShowWarning("หยิบเพิ่มไม่ได้! คุณเลือกแอคชั่น 'หยิบ 2 เหรียญสีเดียวกัน' ไปแล้ว");
            return;
        }

        if (pendingCoins[index] >= 2) {
            ShowWarning("ผิดกติกา! ไม่สามารถหยิบเหรียญสีเดียวกันเกิน 2 อันได้");
            return;
        }

        if (pendingCoins[index] == 1) {
            if (totalPending > 1) {
                ShowWarning("หยิบซ้ำสีไม่ได้! คุณเริ่มแอคชั่น 'หยิบ 3 สีต่างกัน' ไปแล้ว");
                return;
            }
            if (bankCoins[index] < 4) {
                ShowWarning("หยิบ 2 อันไม่ได้! กองกลางมีเหรียญสีนี้เหลือไม่ถึง 4 อัน");
                return;
            }
            pendingCoins[index]++;
        } else if (pendingCoins[index] == 0) {
            if (totalPending >= 3) {
                ShowWarning("คุณหยิบเหรียญครบ 3 สีแล้ว!");
                return;
            }
            pendingCoins[index]++;
        }

        ClearWarning(); 
        clickedBtn.UpdatePendingUI(pendingCoins[index]);
    }

    public void OnCardClicked(CardDisplay card)
    {
        if (BlockActionDuringQuiz()) return;
        if (BlockActionUntilContinue()) return;
        if (BlockActionOutsideLocalTurn()) return;
        if (isGameOver) return; 
        if (IsCurrentPlayerBot() && !isExecutingBotTurn) {
            ShowWarning("กำลังเป็นเทิร์นของบอท");
            return;
        }

        if (GetTotalPendingCoins() > 0) {
            ShowWarning("คุณกำลังทำ 2 แอคชั่น! กรุณากดปุ่ม Clear เหรียญออกก่อนกดซื้อการ์ด");
            return;
        }

        PlayerUI p = players[playOrder[currentPlayerIndex]]; // เปลี่ยนเป็นเช็คคนเล่นตามคิว
        int missingCoins = 0; 
        
        for (int i = 0; i < 5; i++) { 
            int actualCost = Mathf.Max(0, card.data.costs[i] - p.bonuses[i]);
            if (p.coins[i] < actualCost) {
                missingCoins += (actualCost - p.coins[i]);
            }
        }

        bool canAfford = (missingCoins <= p.coins[5]);

        if (canAfford) {
            for (int i = 0; i < 5; i++) {
                int actualCost = Mathf.Max(0, card.data.costs[i] - p.bonuses[i]);
                if (p.coins[i] < actualCost) {
                    int diff = actualCost - p.coins[i];
                    bankCoins[i] += p.coins[i]; 
                    p.coins[i] = 0; 
                    
                    int goldCoinsReturned = SpendWildcardCoinsWithoutReturningQuizBlack(p, diff);
                    bankCoins[5] += goldCoinsReturned; 
                } else {
                    p.coins[i] -= actualCost; 
                    bankCoins[i] += actualCost; 
                }
            }

            p.AddScore(card.data.victoryPoints); 
            p.AddBonus(card.data.bonusType); 
            p.UpdateUI();

            Transform parentContainer = card.transform.parent;
            int tier = (parentContainer == tier3Container) ? 3 : (parentContainer == tier2Container) ? 2 : 1;
            int slotIndex = card.transform.GetSiblingIndex(); // จำช่องเดิมไว้ก่อนดึงการ์ดออก
            // ดึงออกจาก container ก่อน Destroy (deferred) ไม่งั้น BuildBoardSnapshot จะนับใบที่กำลังถูกลบติดไปด้วย
            card.transform.SetParent(null);
            Destroy(card.gameObject);
            DrawNewCard(tier, parentContainer, slotIndex);

            ClearWarning();
            UpdateBankUI(); 
            EndTurn(); 
        } else {
            ShowWarning("ซื้อการ์ดไม่ได้! เหรียญของคุณไม่พอ (รวมส่วนลดและทองแล้ว)");
        }
    }

    public void PromptReserveCard(CardDisplay card)
    {
        if (BlockActionDuringQuiz()) return;
        if (BlockActionUntilContinue()) return;
        if (BlockActionOutsideLocalTurn()) return;
        if (isGameOver) return;
        if (IsCurrentPlayerBot() && !isExecutingBotTurn) {
            ShowWarning("กำลังเป็นเทิร์นของบอท");
            return;
        }
        if (GetTotalPendingCoins() > 0) {
            ShowWarning("ทำ 2 แอคชั่นไม่ได้! กรุณา Clear เหรียญก่อนจองการ์ด");
            return;
        }

        PlayerUI p = players[playOrder[currentPlayerIndex]]; // เปลี่ยนเป็นเช็คคนเล่นตามคิว
        if (p.reservedCards.Count >= 3) {
            ShowWarning("จองเพิ่มไม่ได้! คุณมีการ์ดจองในมือเต็ม 3 ใบแล้ว");
            return;
        }

        pendingReserveCard = card;
        if (confirmReservePanel != null) {
            confirmReservePanel.SetActive(true); 
        }
    }

    public void ConfirmReserve()
    {
        if (BlockActionDuringQuiz()) return;
        if (BlockActionUntilContinue()) return;
        if (BlockActionOutsideLocalTurn()) return;
        if (IsCurrentPlayerBot() && !isExecutingBotTurn) {
            ShowWarning("กำลังเป็นเทิร์นของบอท");
            return;
        }
        if (confirmReservePanel != null) confirmReservePanel.SetActive(false);
        if (pendingReserveCard != null) {
            ExecuteReserve(pendingReserveCard);
            pendingReserveCard = null;
        }
    }

    public void CancelReserve()
    {
        if (BlockActionDuringQuiz()) return;
        if (BlockActionUntilContinue()) return;
        if (BlockActionOutsideLocalTurn()) return;
        if (IsCurrentPlayerBot() && !isExecutingBotTurn) {
            ShowWarning("กำลังเป็นเทิร์นของบอท");
            return;
        }
        if (confirmReservePanel != null) confirmReservePanel.SetActive(false);
        pendingReserveCard = null;
    }

    private void ExecuteReserve(CardDisplay card)
    {
        PlayerUI p = players[playOrder[currentPlayerIndex]]; // เปลี่ยนเป็นเช็คคนเล่นตามคิว
        p.reservedCards.Add(card.data); 

        int goldIndex = 5;
        int totalPlayerCoins = GetTotalPlayerCoins(playOrder[currentPlayerIndex]); // เปลี่ยนเป็นเช็คคนเล่นตามคิว
        if (bankCoins[goldIndex] > 0 && totalPlayerCoins < 10) {
            bankCoins[goldIndex]--; p.coins[goldIndex]++; p.UpdateUI();
        } else if (bankCoins[goldIndex] <= 0) {
            ShowWarning("จองสำเร็จ! แต่ไม่ได้เหรียญทอง (กองกลางหมด)");
        } else if (totalPlayerCoins >= 10) {
            ShowWarning("จองสำเร็จ! แต่ไม่ได้เหรียญทอง (คุณถือเหรียญเต็ม 10 อันแล้ว)");
        }

        if (p.reservedAreaTransform != null) {
            GameObject resCard = Instantiate(cardPrefab, p.reservedAreaTransform);
            resCard.transform.localScale = new Vector3(0.6f, 0.6f, 1f); 
            
            CardDisplay resDisplay = resCard.GetComponent<CardDisplay>();
            resDisplay.LoadCardData(card.data);
            resDisplay.isReserved = true; 
            resDisplay.ownerUI = p; 
        }

        Transform parentContainer = card.transform.parent;
        int tier = (parentContainer == tier3Container) ? 3 : (parentContainer == tier2Container) ? 2 : 1;
        int slotIndex = card.transform.GetSiblingIndex(); // จำช่องเดิมไว้ก่อนดึงการ์ดออก
        // ดึงออกจาก container ก่อน Destroy (deferred) ไม่งั้น BuildBoardSnapshot จะนับใบที่กำลังถูกลบติดไปด้วย
        card.transform.SetParent(null);
        Destroy(card.gameObject);
        DrawNewCard(tier, parentContainer, slotIndex);

        ClearWarning();
        UpdateBankUI();
        EndTurn();
    }

    public void BuyReservedCard(CardDisplay card)
    {
        if (BlockActionDuringQuiz()) return;
        if (BlockActionUntilContinue()) return;
        if (BlockActionOutsideLocalTurn()) return;
        if (isGameOver) return;
        
        PlayerUI p = players[playOrder[currentPlayerIndex]]; // เปลี่ยนเป็นเช็คคนเล่นตามคิว

        if (IsCurrentPlayerBot() && !isExecutingBotTurn) {
            ShowWarning("กำลังเป็นเทิร์นของบอท");
            return;
        }

        if (card.ownerUI != p) {
            ShowWarning("ไม่สามารถซื้อการ์ดจองของผู้เล่นอื่นได้!");
            return;
        }

        if (GetTotalPendingCoins() > 0) {
            ShowWarning("กรุณา Clear เหรียญก่อนกดซื้อการ์ด!");
            return;
        }

        int missingCoins = 0; 
        for (int i = 0; i < 5; i++) { 
            int actualCost = Mathf.Max(0, card.data.costs[i] - p.bonuses[i]);
            if (p.coins[i] < actualCost) {
                missingCoins += (actualCost - p.coins[i]);
            }
        }

        bool canAfford = (missingCoins <= p.coins[5]);

        if (canAfford) {
            for (int i = 0; i < 5; i++) {
                int actualCost = Mathf.Max(0, card.data.costs[i] - p.bonuses[i]);
                if (p.coins[i] < actualCost) {
                    int diff = actualCost - p.coins[i];
                    bankCoins[i] += p.coins[i]; p.coins[i] = 0; 
                    int goldCoinsReturned = SpendWildcardCoinsWithoutReturningQuizBlack(p, diff);
                    bankCoins[5] += goldCoinsReturned; 
                } else {
                    p.coins[i] -= actualCost; bankCoins[i] += actualCost; 
                }
            }

            p.AddScore(card.data.victoryPoints); 
            p.AddBonus(card.data.bonusType); 
            p.reservedCards.Remove(card.data); 
            p.UpdateUI();

            Destroy(card.gameObject); 
            
            ClearWarning(); 
            UpdateBankUI(); 
            EndTurn(); 
        } else {
            ShowWarning("การ์ดที่คุณจองไว้ยังไม่สามารถซื้อได้ เพราะเหรียญไม่พอ!");
        }
    }

    public void EndTurn()
    {
        if (BlockActionDuringQuiz()) return;
        if (BlockActionUntilContinue()) return;
        // [FIX] ไม่ block บอท — BlockActionOutsideLocalTurn ใช้สำหรับ input จาก UI เท่านั้น
        // บอทเรียก EndTurn() โดยตรงจาก BotController ซึ่งถูกต้องแล้ว
        if (IsCurrentPlayerBot() && !isExecutingBotTurn) {
            ShowWarning("กำลังเป็นเทิร์นของบอท");
            return;
        }
        if (!IsCurrentPlayerBot() && !isExecutingBotTurn && !IsLocalPlayersTurn()) {
            ShowWarning("กดปุ่มได้เฉพาะในเทิร์นของคุณ");
            return;
        }
        if (isGameOver) return;

        if (GetTotalPendingCoins() > 0) {
            for (int i = 0; i < 6; i++) bankCoins[i] -= pendingCoins[i];
            players[playOrder[currentPlayerIndex]].ReceiveCoins(pendingCoins); // เปลี่ยนเป็นแจกให้คนเล่นตามคิว
            ClearPendingCoins(); 
        }
        
        UpdateBankUI(); 
        ClearWarning(); 

        // [Phase 1] เช็คขุนนางอัตโนมัติก่อนจบเทิร์นของคนปัจจุบัน
        PlayerUI p = players[playOrder[currentPlayerIndex]];
        nobleManager?.CheckClaim(p);

        if (isOnlineMatchMode)
        {
            PublishOnlineBoardState();
            PublishOnlineEconomyState();
        }

        // ตรวจสอบการชนะเกม "ทันที" ในทุกๆ เทิร์น!
        // (เดิมทีเช็คเฉพาะตอนจบรอบ ทำให้แต้มถึง 20 แล้วยังรันเทิร์นต่อไปหาบอท)
        EvaluateWinCondition();
        if (isGameOver) return; // ปิดจ๊อบ ออกจากฟังก์ชันทันทีถ้ามีคนชนะ

        totalTurnCount++;
        currentPlayerIndex++;
        
        // วนรอบคิวตามจำนวน players จริงๆ (ใช้ playOrder.Length เพื่อความแน่นอน)
        if (playOrder == null || playOrder.Length == 0) return;
        
        bool isNewRound = false;
        bool startedQuizThisTurn = false;
        if (currentPlayerIndex >= playOrder.Length) {
            currentPlayerIndex = 0;
            currentRound++;
            isNewRound = true; // ระบุว่ากำลังขึ้นรอบใหม่
            GameLog.Log($"\n========== เริ่มรอบที่ {currentRound} ==========\n");
        }

        // [Phase 2] อัปเดตเลขเทิร์นและเช็คการเรียกควิซ
        currentTurnDisplay = currentRound;
        UpdateTurnCountUI();

        bool shouldStartQuiz = isOnlineMatchMode
            ? isNewRound && (currentTurnDisplay - 1) > 0 && (currentTurnDisplay - 1) % Mathf.Max(1, onlineQuizTurnInterval) == 0
            : isNewRound && (currentTurnDisplay - 1) > 0 && (currentTurnDisplay - 1) % Mathf.Max(1, quizInterval) == 0;

        if (shouldStartQuiz) {
            if (QuizManager.Instance != null) {
                string quizTriggerLabel = $"รอบที่ {currentTurnDisplay}";
                GameLog.Log($"<color=cyan>[Quiz] {quizTriggerLabel} -> ถึงเวลาเรียกควิซแล้ว!</color>");
                startedQuizThisTurn = true;

                bool canStartQuizLocally = !isOnlineMatchMode
                    || (FusionManager.Instance != null && FusionManager.Instance.IsMasterClient);

                if (canStartQuizLocally)
                {
                    // offline หรือเราเป็น host → เลือกคำถามแล้ว broadcast ให้ทุกคนได้เลย
                    QuizManager.Instance.StartQuiz();
                }
                else if (FusionManager.Instance != null)
                {
                    // ออนไลน์และเราไม่ใช่ host → ขอให้ host เริ่มควิซ แล้ว host จะ broadcast มาให้ทุกคน
                    GameLog.Log("[Quiz] Client ถึงรอบควิซ → ส่งคำขอให้ Host เริ่มควิซ");
                    FusionManager.Instance.RequestQuizStart();
                }
            }
        }

        ResetTimer();
        UpdateTurnVisuals();
        // board + economy ถูก publish ไปแล้วที่ต้นฟังก์ชัน (บรรทัด ~931) และไม่เปลี่ยนอีกหลังจากนั้น
        // จึงส่งซ้ำเฉพาะ turn state ที่เพิ่ง advance เท่านั้น (ลด network traffic ครึ่งนึงตอนจบเทิร์น)
        PublishOnlineTurnState();
        if (!startedQuizThisTurn) {
            ScheduleBotTurnIfNeeded();
        }
    }

    public void EvaluateWinCondition()
    {
        CheckWinCondition();
    }

    void CheckWinCondition() 
    {
        if (isGameOver)
        {
            return;
        }

        PlayerUI winner = null; 
        int highestScore = 0;
        
        for (int i = 0; i < activePlayerCount; i++) {
            // [FIX] null check ก่อน access เพื่อป้องกัน NullReferenceException
            if (players[i] == null) continue;
            if (players[i].currentScore >= winningScore && players[i].currentScore > highestScore) {
                highestScore = players[i].currentScore;
                winner = players[i];
            }
        }
        
        if (winner != null) {
            isGameOver = true;

            // อัปเดตสถานะห้องใน Supabase เป็น 'finished' (host เท่านั้น — เมธอด check เอง)
            FusionManager.Instance?.SetRoomStatus("finished");

            // --- บันทึกสถิติ Coins และ Points เก็บไว้แสดงที่หน้า Main Menu ---
            int localSeatIndex = GetLocalPlayerUiIndex();
            if (localSeatIndex >= 0 &&
                localSeatIndex < players.Length &&
                players[localSeatIndex] != null &&
                !players[localSeatIndex].isBot)
            {
                int earnedCoins  = GetTotalPlayerCoins(localSeatIndex);
                int earnedPoints = players[localSeatIndex].currentScore;

                // --- คำนวณ Gem reward ตามลำดับที่จบ ---
                int finishRank = 1;
                for (int r = 0; r < activePlayerCount; r++)
                {
                    if (r != localSeatIndex && players[r] != null &&
                        players[r].currentScore > players[localSeatIndex].currentScore)
                        finishRank++;
                }
                int gemReward = finishRank == 1 ? 5 :
                                finishRank == 2 ? 3 :
                                finishRank == 3 ? 2 : 1;

                // --- บันทึก Gem reward (local-only เพื่อโชว์ผลทันที; DB เขียนโดย server ด้านล่าง) ---
                PlayerPrefs.SetInt("TotalGems", PlayerPrefs.GetInt("TotalGems", 0) + gemReward);
                PlayerPrefs.Save();
                CurrencyManager.Instance?.RefreshFromLocalCache();

                // --- บันทึก Points ---
                int currentTotalPoints = PlayerPrefs.GetInt("TotalPoints", 0);
                PlayerPrefs.SetInt("TotalPoints", currentTotalPoints + earnedPoints);
                PlayerPrefs.Save();

                // --- คำนวณ MMR ฝั่ง local สำหรับโชว์ผลทันที (ResultScreen) ---
                int currentMmr = PlayerDataService.LocalProfile?.Mmr ?? PlayerPrefs.GetInt("MMR", 1000);
                int mmrDelta = MmrCalculator.Calculate(finishRank, activePlayerCount);
                int newMmr = MmrCalculator.Clamp(currentMmr + mmrDelta);
                PlayerPrefs.SetInt("LastMmrDelta", mmrDelta);
                PlayerPrefs.SetInt("MMR", newMmr);
                PlayerPrefs.Save();

                // --- เขียนลง DB แบบ server-authoritative: server คำนวณ MMR/gems เอง ---
                // client ส่งแค่ "อันดับ + จำนวนผู้เล่น" จึงปลอมค่า MMR/gems ลง DB ไม่ได้
                // (ค่าที่ server คืนจะตรงกับ local เพราะใช้สูตรเดียวกัน — MmrCalculator)
                _ = PlayerDataService.SubmitMatchResultAsync(finishRank, activePlayerCount);
                GameLog.Log($"[GameController] MMR (local preview): {currentMmr} + {mmrDelta} = {newMmr}");

                GameLog.Log($"[GameController] จบเกมอันดับ {finishRank} | +{earnedCoins} Coins, +{gemReward} Gems, +{earnedPoints} Points");
            }

            // โชว์หน้าสรุปผลตอนจบเกม
            if (resultScreen != null)
            {
                List<string> rankings = new List<string>();
                for (int i = 0; i < activePlayerCount; i++)
                {
                    // [FIX] null-safe nameText เพื่อป้องกัน crash ถ้า prefab ไม่ได้ลาก text ใส่
                    if (players[i] != null)
                    {
                        string pName = players[i].nameText != null ? players[i].nameText.text : $"Player {i + 1}";
                        rankings.Add($"{pName} : {players[i].currentScore} แต้ม");
                    }
                }
                
                string winnerName = winner.nameText != null ? winner.nameText.text : "ผู้เล่น";
                resultScreen.ShowResults("เกมจบแล้ว! ผู้ชนะคือ " + winnerName, rankings, true);
            }
            else
            {
                string winnerNameFallback = winner.nameText != null ? winner.nameText.text : "ผู้เล่น";
                ShowWarning($"ผู้ชนะคือ {winnerNameFallback} {highestScore} แต้ม!");
            }
            
            for (int i = 0; i < activePlayerCount; i++)
            {
                if (players[i] != null) players[i].UpdateTimerBar(0);
            }
        }
    }

    void PopulateBoard() 
    {
        ClearContainer(tier3Container); 
        ClearContainer(tier2Container); 
        ClearContainer(tier1Container);
        for(int i = 0; i < 4; i++) { 
            DrawNewCard(3, tier3Container); 
            DrawNewCard(2, tier2Container); 
            DrawNewCard(1, tier1Container); 
        }
    }

    void DrawNewCard(int tier, Transform container, int slotIndex = -1)
    {
        List<CardData> masterDeck = tier == 3 ? tier3Cards : tier == 2 ? tier2Cards : tier1Cards;
        if (masterDeck == null || masterDeck.Count == 0) return;

        // สร้าง list การ์ดที่ยังไม่เคยถูกใช้
        List<CardData> availableCards = new List<CardData>();
        foreach (var card in masterDeck)
        {
            if (!usedCardIds.Contains(card.cardId)) availableCards.Add(card);
        }

        // ถ้าหมดกองแล้ว ไม่สุ่มใบใหม่ขึ้นมา
        if (availableCards.Count == 0)
        {
            GameLog.Log($"[GameController] กอง Tier {tier} หมดแล้ว ไม่มีการ์ดให้สุ่มเพิ่ม");
            return;
        }

        CardData selectedCard = availableCards[Random.Range(0, availableCards.Count)];
        usedCardIds.Add(selectedCard.cardId); // บันทึกว่าใบนี้ถูกใช้แล้ว
        GameObject newCardObj = Instantiate(cardPrefab, container);
        newCardObj.GetComponent<CardDisplay>()?.LoadCardData(selectedCard);

        // วางการ์ดใหม่ที่ช่องเดิมของใบที่หายไป (ถ้าระบุมา) แทนการต่อท้าย
        if (slotIndex >= 0)
        {
            newCardObj.transform.SetSiblingIndex(slotIndex);
        }
    }

    /// <summary>
    /// โหลดข้อมูลการ์ดจาก cards_database.json อัตโนมัติ
    /// </summary>
    void LoadCardDatabase()
    {
        CardDatabaseLoader.EnsureLoaded();
        tier1Cards = CardDatabaseLoader.Tier1Cards;
        tier2Cards = CardDatabaseLoader.Tier2Cards;
        tier3Cards = CardDatabaseLoader.Tier3Cards;
        GameLog.Log($"[GameController] โหลดการ์ดจาก JSON สำเร็จ! T1:{tier1Cards.Count} T2:{tier2Cards.Count} T3:{tier3Cards.Count}");
    }

    void ResetTimer() { currentTurnTime = turnDuration; }
    
    void UpdateTurnVisuals() {
        if (players == null || players.Length == 0) return;
        if (playOrder == null || playOrder.Length == 0) return;
        if (currentPlayerIndex >= playOrder.Length) currentPlayerIndex = 0;
        
        int activePlayerIdx = playOrder[currentPlayerIndex];
        for (int i = 0; i < players.Length; i++) 
            if (players[i] != null) players[i].SetActiveTurn(i == activePlayerIdx);
    }

    // Bot AI execution methods → moved to GameController.Bots.cs

    public int GetResourceIndex(string type) {
        if (type == "CPU") return 0; if (type == "RAM") return 1; if (type == "Network") return 2;
        if (type == "Storage") return 3; if (type == "Security") return 4; return 5;
    }

    int GetConfiguredPlayerCount()
    {
        if (isOnlineMatchMode)
        {
            return GetConfiguredOnlinePlayerCount();
        }

        if (players == null || players.Length == 0) return 4;

        int count = 0;
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null) count++;
        }

        return Mathf.Clamp(count, 2, 4);
    }

    void ConfigureBankCoinsByPlayerCount()
    {
        int playerCount = GetConfiguredPlayerCount();
        int coloredCoins = playerCount == 2 ? 4 : playerCount == 3 ? 5 : 7;

        for (int i = 0; i < 5; i++)
        {
            bankCoins[i] = coloredCoins;
        }

        bankCoins[5] = 5;
    }
    
    void SpawnResourceBank() {
        ClearContainer(resourceBankContainer); 
        bankButtons.Clear(); 
        string[] resNames = { "CPU", "RAM", "Network", "Storage", "Security", "Wildcard" };
        foreach (string res in resNames) {
            GameObject obj = Instantiate(resourcePrefab, resourceBankContainer);
            ResourceButton btn = obj.GetComponent<ResourceButton>();
            if (btn != null) { btn.Setup(this, res); bankButtons.Add(btn); }
        }
    }
    
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

    private Coroutine panelLayoutCoroutine;

    private IEnumerator ConfigureOnlinePlayerPanelLayoutDeferred()
    {
        // รอให้ผ่าน layout pass + render ของเฟรมนี้ก่อน แล้วบังคับอัปเดต canvas ให้ตำแหน่งนิ่ง
        yield return new WaitForEndOfFrame();
        Canvas.ForceUpdateCanvases();
        ConfigureOnlinePlayerPanelLayout();
        panelLayoutCoroutine = null;
    }

    void ClearContainer(Transform c) {
        if (c == null) return;
        // เก็บลูกทั้งหมดก่อน แล้วค่อย detach+Destroy เพื่อไม่ให้ใบที่กำลังถูกลบ (deferred)
        // ยังถูกนับว่าอยู่ใน container ตอน BuildBoardSnapshot/rebuild ในเฟรมเดียวกัน
        List<Transform> children = new List<Transform>();
        foreach (Transform child in c) children.Add(child);
        foreach (Transform child in children) {
            child.SetParent(null);
            Destroy(child.gameObject);
        }
    }

    private void ApplyNetworkPlayerNamesToUi()
    {
        if (players == null || FusionManager.Instance == null)
        {
            return;
        }

        for (int seatIndex = 0; seatIndex < activePlayerCount && seatIndex < players.Length; seatIndex++)
        {
            if (players[seatIndex] == null || players[seatIndex].isBot)
            {
                continue;
            }

            string playerName = FusionManager.Instance.GetPlayerNameBySeat(seatIndex);
            if (string.IsNullOrWhiteSpace(playerName))
            {
                continue;
            }

            if (players[seatIndex].nameText != null)
            {
                players[seatIndex].nameText.text = playerName;
            }

            GameLog.Log($"[GameController] Updated player slot {seatIndex + 1} name to {playerName}");
        }
    }

    private string GetOnlinePlayerDisplayNameForSeat(int seatIndex)
    {
        if (FusionManager.Instance == null)
        {
            return "Online Player " + (seatIndex + 1);
        }

        string remoteName = FusionManager.Instance.GetPlayerNameBySeat(seatIndex);
        return string.IsNullOrWhiteSpace(remoteName) ? "Online Player " + (seatIndex + 1) : remoteName;
    }

    private int GetResolvedLocalPlayerSlotIndex()
    {
        if (!isOnlineMatchMode || FusionManager.Instance == null)
        {
            return 0;
        }

        return Mathf.Clamp(FusionManager.Instance.GetLocalPlayerSeatIndex(), 0, Mathf.Max(0, GetConfiguredOnlinePlayerCount() - 1));
    }

    private int GetLocalPlayerUiIndex()
    {
        return isOnlineMatchMode ? GetResolvedLocalPlayerSlotIndex() : 0;
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

    private void PublishOnlineTurnState()
    {
        if (!isOnlineMatchMode || FusionManager.Instance == null)
        {
            return;
        }

        FusionManager.Instance.SendTurnState(currentPlayerIndex, currentRound, totalTurnCount, currentTurnDisplay);
    }

    public void PublishOnlineEconomyState()
    {
        if (!isOnlineMatchMode || FusionManager.Instance == null)
        {
            return;
        }

        FusionManager.Instance.SendEconomyState(BuildEconomySnapshot());
    }

    private FusionManager.EconomyStateSnapshot BuildEconomySnapshot()
    {
        var snapshot = new FusionManager.EconomyStateSnapshot
        {
            BankCoins = (int[])bankCoins.Clone(),
            Players = new FusionManager.EconomyPlayerSnapshot[activePlayerCount]
        };

        for (int i = 0; i < activePlayerCount; i++)
        {
            PlayerUI player = players[i];
            snapshot.Players[i] = new FusionManager.EconomyPlayerSnapshot
            {
                Score = player != null ? player.currentScore : 0,
                Coins = player != null ? (int[])player.coins.Clone() : new int[6],
                Bonuses = player != null ? (int[])player.bonuses.Clone() : new int[5],
                QuizBlackCoins = player != null ? player.quizBlackCoins : 0
            };
        }

        return snapshot;
    }

    private void ApplyEconomySnapshot(FusionManager.EconomyStateSnapshot snapshot)
    {
        if (snapshot.BankCoins != null)
        {
            for (int i = 0; i < bankCoins.Length && i < snapshot.BankCoins.Length; i++)
            {
                bankCoins[i] = snapshot.BankCoins[i];
            }
        }

        if (snapshot.Players != null)
        {
            for (int i = 0; i < activePlayerCount && i < snapshot.Players.Length && i < players.Length; i++)
            {
                PlayerUI player = players[i];
                if (player == null)
                {
                    continue;
                }

                player.currentScore = snapshot.Players[i].Score;
                player.quizBlackCoins = Mathf.Max(0, snapshot.Players[i].QuizBlackCoins);

                if (snapshot.Players[i].Coins != null)
                {
                    for (int coinIndex = 0; coinIndex < player.coins.Length && coinIndex < snapshot.Players[i].Coins.Length; coinIndex++)
                    {
                        player.coins[coinIndex] = snapshot.Players[i].Coins[coinIndex];
                    }
                }

                if (snapshot.Players[i].Bonuses != null)
                {
                    for (int bonusIndex = 0; bonusIndex < player.bonuses.Length && bonusIndex < snapshot.Players[i].Bonuses.Length; bonusIndex++)
                    {
                        player.bonuses[bonusIndex] = snapshot.Players[i].Bonuses[bonusIndex];
                    }
                }

                if (player.scoreText != null)
                {
                    player.scoreText.text = player.currentScore.ToString();
                }

                player.UpdateUI();
            }
        }

        UpdateBankUI();
    }

    // ===== Online Board (face-up market) Sync =====

    public void PublishOnlineBoardState()
    {
        if (!isOnlineMatchMode || FusionManager.Instance == null)
        {
            return;
        }

        FusionManager.Instance.SendBoardState(BuildBoardSnapshot());
    }

    private FusionManager.BoardStateSnapshot BuildBoardSnapshot()
    {
        return new FusionManager.BoardStateSnapshot
        {
            Tier1CardIds = GetBoardTierCardIds(tier1Container),
            Tier2CardIds = GetBoardTierCardIds(tier2Container),
            Tier3CardIds = GetBoardTierCardIds(tier3Container),
            UsedCardIds = new List<string>(usedCardIds).ToArray()
        };
    }

    private string[] GetBoardTierCardIds(Transform container)
    {
        if (container == null)
        {
            return System.Array.Empty<string>();
        }

        List<string> ids = new List<string>();
        foreach (Transform child in container)
        {
            CardDisplay display = child.GetComponent<CardDisplay>();
            ids.Add(display != null && display.data != null ? display.data.cardId : string.Empty);
        }

        return ids.ToArray();
    }

    private void HandleOnlineBoardStateReceived(FusionManager.BoardStateSnapshot snapshot)
    {
        if (!isOnlineMatchMode)
        {
            return;
        }

        ApplyBoardSnapshot(snapshot);
    }

    private void ApplyBoardSnapshot(FusionManager.BoardStateSnapshot snapshot)
    {
        if (cardPrefab == null)
        {
            return;
        }

        // sync รายการการ์ดที่ถูกใช้แล้ว เพื่อให้ตอนถึงตาเราจั่วการ์ดได้ตรงกับ Host (กันการ์ดซ้ำ)
        if (snapshot.UsedCardIds != null)
        {
            usedCardIds.Clear();
            foreach (string id in snapshot.UsedCardIds)
            {
                if (!string.IsNullOrEmpty(id))
                {
                    usedCardIds.Add(id);
                }
            }
        }

        RebuildTierIfChanged(tier3Container, snapshot.Tier3CardIds);
        RebuildTierIfChanged(tier2Container, snapshot.Tier2CardIds);
        RebuildTierIfChanged(tier1Container, snapshot.Tier1CardIds);
    }

    private void RebuildTierIfChanged(Transform container, string[] cardIds)
    {
        if (container == null || cardIds == null)
        {
            return;
        }

        // เทียบสถานะปัจจุบันกับที่รับมา — ถ้าเหมือนกันแล้วไม่ rebuild (กันการ์ดกระพริบทุกเทิร์น)
        List<string> incoming = new List<string>();
        foreach (string id in cardIds)
        {
            if (!string.IsNullOrEmpty(id)) incoming.Add(id);
        }

        List<string> current = new List<string>();
        foreach (Transform child in container)
        {
            CardDisplay display = child.GetComponent<CardDisplay>();
            if (display != null && display.data != null) current.Add(display.data.cardId);
        }

        if (incoming.Count == current.Count)
        {
            bool identical = true;
            for (int i = 0; i < incoming.Count; i++)
            {
                if (incoming[i] != current[i]) { identical = false; break; }
            }

            if (identical) return;
        }

        ClearContainer(container);
        foreach (string id in incoming)
        {
            CardData data = FindCardDataById(id);
            if (data == null)
            {
                Debug.LogWarning($"[GameController] ไม่พบ CardData สำหรับ cardId '{id}' ตอน sync กระดาน");
                continue;
            }

            GameObject obj = Instantiate(cardPrefab, container);
            obj.GetComponent<CardDisplay>()?.LoadCardData(data);
        }
    }

    private CardData FindCardDataById(string cardId)
    {
        if (string.IsNullOrEmpty(cardId))
        {
            return null;
        }

        foreach (CardData card in CardDatabaseLoader.AllCards)
        {
            if (card != null && card.cardId == cardId) return card;
        }

        return null;
    }

    private void ConfigureOnlinePlayerPanelLayout()
    {
        if (!isOnlineMatchMode || players == null || players.Length == 0)
        {
            return;
        }

        CapturePlayerPanelLayoutsIfNeeded();
        if (capturedPlayerPanelLayouts == null || capturedPlayerPanelLayouts.Length == 0)
        {
            return;
        }

        int localSeat = GetResolvedLocalPlayerSlotIndex();
        int layoutCount = Mathf.Min(activePlayerCount, Mathf.Min(players.Length, capturedPlayerPanelLayouts.Length));

        for (int seatIndex = 0; seatIndex < layoutCount; seatIndex++)
        {
            if (players[seatIndex] == null)
            {
                continue;
            }

            int rotatedLayoutIndex = GetRotatedLayoutIndex(seatIndex, localSeat, layoutCount);
            ApplyPlayerPanelLayout(players[seatIndex], capturedPlayerPanelLayouts[rotatedLayoutIndex]);
        }
    }

    private void CapturePlayerPanelLayoutsIfNeeded()
    {
        if (playerPanelLayoutsCaptured || players == null || players.Length == 0)
        {
            return;
        }

        capturedPlayerPanelLayouts = new PlayerPanelLayout[players.Length];
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] == null || !players[i].TryGetComponent(out RectTransform rectTransform))
            {
                continue;
            }

            capturedPlayerPanelLayouts[i] = new PlayerPanelLayout
            {
                AnchoredPosition = rectTransform.anchoredPosition,
                SizeDelta = rectTransform.sizeDelta,
                AnchorMin = rectTransform.anchorMin,
                AnchorMax = rectTransform.anchorMax,
                Pivot = rectTransform.pivot,
                LocalScale = rectTransform.localScale,
                LocalRotation = rectTransform.localRotation,
                SiblingIndex = rectTransform.GetSiblingIndex()
            };
        }

        playerPanelLayoutsCaptured = true;
    }

    private static int GetRotatedLayoutIndex(int seatIndex, int localSeatIndex, int layoutCount)
    {
        if (layoutCount <= 0)
        {
            return 0;
        }

        int normalizedSeatIndex = ((seatIndex % layoutCount) + layoutCount) % layoutCount;
        int normalizedLocalSeatIndex = ((localSeatIndex % layoutCount) + layoutCount) % layoutCount;
        return (normalizedSeatIndex - normalizedLocalSeatIndex + layoutCount) % layoutCount;
    }

    private static void ApplyPlayerPanelLayout(PlayerUI player, PlayerPanelLayout layout)
    {
        if (player == null || !player.TryGetComponent(out RectTransform rectTransform))
        {
            return;
        }

        rectTransform.anchorMin = layout.AnchorMin;
        rectTransform.anchorMax = layout.AnchorMax;
        rectTransform.pivot = layout.Pivot;
        rectTransform.anchoredPosition = layout.AnchoredPosition;
        rectTransform.sizeDelta = layout.SizeDelta;
        rectTransform.localScale = layout.LocalScale;
        rectTransform.localRotation = layout.LocalRotation;
        rectTransform.SetSiblingIndex(layout.SiblingIndex);
    }

    private int GetConfiguredOnlinePlayerCount()
    {
        // ถ้าเป็นห้องที่สร้างเอง (ไม่มีรหัส AutoMatchmaking) ให้นับจำนวนคนในห้องของ FusionManager เลย
        if (FusionManager.Instance != null && string.IsNullOrWhiteSpace(PlayerPrefs.GetString(MatchmakingRoomCodePrefsKey, string.Empty)))
        {
            return Mathf.Clamp(FusionManager.Instance.ActivePlayerCount, 2, 4);
        }

        return Mathf.Clamp(PlayerPrefs.GetInt(MatchmakingTargetPlayerCountPrefsKey, 2), 2, Mathf.Min(4, players != null && players.Length > 0 ? players.Length : 4));
    }

    // [NEW] อัปเดตตัวเลขเทิร์นบน UI
    public void UpdateTurnCountUI()
    {
        if (turnCountText != null)
        {
            // เปลี่ยนมาโชว์ เลขรอบ (Round) แทนเลขลำดับเทิร์นย่อย
            turnCountText.text = "ROUND: " + currentRound;
        }
    }

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

    // CheckNobles → moved to NobleManager.CheckClaim() (Assets/Scripts/Controllers/NobleManager.cs)
}
