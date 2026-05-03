using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro; 

public class GameController : MonoBehaviour
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
    public List<NobleDisplay> activeNobles = new List<NobleDisplay>(); // ขุนนาง 4 ใบที่โผล่มาในเกมนี้

    [Header("---- Card Database (Decks) ----")]
    public List<CardData> tier3Cards; 
    public List<CardData> tier2Cards; 
    public List<CardData> tier1Cards;

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
    public int onlineQuizTurnInterval = 4; // ใช้เป็นช่วง "รอบ" ของควิซในโหมดออนไลน์
    private bool isGameOver = false; 


    [Header("---- UI Alerts & Results ----")]
    public TextMeshProUGUI warningText; 
    public TextMeshProUGUI turnCountText; // [NEW] ลาก Text บอกลำดับเทิร์นมาใส่ที่นี่
    public ResultScreenUI resultScreen; // หน้าต่างสรุปผลอเนกประสงค์
    [Header("---- Reserve Confirmation UI ----")]
    public GameObject confirmReservePanel; 
    private CardDisplay pendingReserveCard; 

    [Header("---- Bot Settings ----")]
    [SerializeField] private float botTurnDelay = 0.75f;
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
            Debug.Log("[GameController] ไม่มี resourcePrefab → ใช้เหรียญที่วางมือไว้ใน ResourceBankPanel");
            bankButtons.Clear();
            foreach (Transform child in resourceBankContainer)
            {
                ResourceButton btn = child.GetComponent<ResourceButton>();
                if (btn != null)
                {
                    bankButtons.Add(btn);
                    Debug.Log($"[GameController] พบเหรียญที่วางไว้: {btn.resourceType}");
                }
            }
        }
        else
        {
            Debug.LogWarning("[GameController] resourceBankContainer ยังไม่ได้ผูก → ข้ามการสร้างเหรียญ");
        }

        // Setup กระดานไพ่ (ต้องการ cardPrefab)
        if (cardPrefab != null)
            PopulateBoard();
        else
            Debug.LogWarning("[GameController] cardPrefab ยังไม่ได้ผูก → ข้ามการสร้างการ์ดบน Board");

        // Setup ขุนนาง
        if (noblePrefab != null && masterNobles != null && masterNobles.Count > 0)
        {
            SetupNobles();
        }
        else
        {
            Debug.LogWarning("[GameController] ยังไม่ได้ผูก noblePrefab หรือ masterNobles → ข้ามการสร้างขุนนาง");
        }

        Debug.Log($"\n========== เริ่มเกม: รอบที่ {currentRound} ==========\n");
        ResetTimer();
        UpdateTurnVisuals();
        UpdateBankUI();
    }

    void Start()
    {
        ApplyNetworkPlayerNamesToUi();
        UpdateTurnCountUI();

        if (ShouldWaitForOnlineOpponent())
        {
            ShowWarning("Waiting for opponent...");
            Debug.Log("[GameController] Waiting for the second online player to join before starting gameplay.");
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
            PublishOnlineEconomyState();
            PublishOnlineTurnState();
        }

        if (QuizManager.Instance != null)
        {
            Debug.Log("[GameController] Starting first-round quiz.");
            QuizManager.Instance.StartQuiz();
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
        }
    }

    private bool IsMatchedOnlineSession()
    {
        if (!string.IsNullOrWhiteSpace(PlayerPrefs.GetString(MatchmakingRoomCodePrefsKey, string.Empty)))
        {
            return true;
        }

        return FusionManager.Instance != null && FusionManager.Instance.ActivePlayerCount >= 2;
    }

    private bool ShouldWaitForOnlineOpponent()
    {
        return isOnlineMatchMode && FusionManager.Instance != null && FusionManager.Instance.ActivePlayerCount < GetConfiguredOnlinePlayerCount();
    }

    private void HandleFusionActivePlayersChanged()
    {
        isOnlineMatchMode = IsMatchedOnlineSession();
        activePlayerCount = isOnlineMatchMode ? GetConfiguredOnlinePlayerCount() : 4;

        if (!isOnlineMatchMode)
        {
            return;
        }

        SetupPlayers();
        ConfigureBankCoinsByPlayerCount();
        UpdateBankUI();
        UpdateTurnVisuals();
        ApplyNetworkPlayerNamesToUi();

        if (ShouldWaitForOnlineOpponent())
        {
            ShowWarning("Waiting for opponent...");
            return;
        }

        Debug.Log("[GameController] Online players ready. Refreshing PvP setup.");
        StartInitialGameplay();
    }

    private void HandleOnlineTurnStateReceived(int syncedCurrentPlayerIndex, int syncedRound, int syncedTotalTurnCount, int syncedTurnDisplay)
    {
        if (!isOnlineMatchMode)
        {
            return;
        }

        currentPlayerIndex = Mathf.Clamp(syncedCurrentPlayerIndex, 0, Mathf.Max(0, activePlayerCount - 1));
        currentRound = Mathf.Max(1, syncedRound);
        totalTurnCount = Mathf.Max(0, syncedTotalTurnCount);
        currentTurnDisplay = Mathf.Max(1, syncedTurnDisplay);

        ResetTimer();
        UpdateTurnVisuals();
        UpdateTurnCountUI();
        System.Array.Clear(pendingCoins, 0, pendingCoins.Length);
        foreach (var btn in bankButtons)
        {
            if (btn != null)
            {
                btn.UpdatePendingUI(0);
            }
        }
        ClearWarning();
    }

    private void HandleOnlineEconomyStateReceived(FusionManager.EconomyStateSnapshot snapshot)
    {
        if (!isOnlineMatchMode)
        {
            return;
        }

        ApplyEconomySnapshot(snapshot);
        EvaluateWinCondition();
    }

    void SetupNobles()
    {
        if (masterNobles.Count < 4) 
        {
            Debug.LogWarning("[GameController] มีขุนนางใน Master น้อยกว่า 4 ใบ! กรุณาใส่ให้ครบก่อน");
            return;
        }

        activeNobles.Clear();

        // ก็อปปี้ลิสต์ออกมาสับไพ่
        List<NobleData> tempNobles = new List<NobleData>(masterNobles);
        
        // สลับไพ่ด้วย Fisher-Yates shuffle
        for (int i = 0; i < tempNobles.Count; i++)
        {
            NobleData temp = tempNobles[i];
            int randomIndex = Random.Range(i, tempNobles.Count);
            tempNobles[i] = tempNobles[randomIndex];
            tempNobles[randomIndex] = temp;
        }

        // ดึงมา 4 ใบ และสร้าง UI 
        for (int i = 0; i < 4; i++)
        {
            NobleData selectedNoble = tempNobles[i];

            // 2 ใบแรกไปทางซ้าย, 2 ใบหลังไปทางขวา
            Transform targetContainer = (i < 2) ? leftNobleContainer : rightNobleContainer;

            if (targetContainer == null) 
            {
                Debug.LogWarning("[GameController] ยังไม่ได้ผูก Left/Right Noble Container!");
                continue;
            }

            GameObject nobleObj = Instantiate(noblePrefab, targetContainer);
            NobleDisplay display = nobleObj.GetComponent<NobleDisplay>();

            if (display != null)
            {
                display.SetupNoble(selectedNoble);
                activeNobles.Add(display);
            }
        }

        Debug.Log($"[GameController] สร้างและสุ่มขุนนาง 4 ใบเรียบร้อย");
    }

    void Update()
    {
        if (isGameOver) return;
        if (players == null || players.Length == 0) return;
        if (playOrder == null || playOrder.Length == 0) return;
        if (isGameplayInputLocked) return;
        if (isWaitingForContinueAfterResult) return;

        if (currentTurnTime > 0) 
        {
            currentTurnTime -= Time.deltaTime;
            
            // อัปเดตหลอดเวลาของคนที่กำลังเล่นอยู่ตามคิว
            int activeIdx = playOrder[currentPlayerIndex];
            if (activeIdx >= 0 && activeIdx < players.Length && players[activeIdx] != null)
                players[activeIdx].UpdateTimerBar(currentTurnTime / turnDuration);
            
            if (currentTurnTime <= 0) 
            {
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
        
        Debug.Log($"<color=orange>[GameController] บังคับใช้คิวการเล่นใหม่เรียบร้อย!</color>");
        
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

    private void OnResultScreenClosed()
    {
        SetWaitingForContinueAfterResult(false);
        ClearWarning();
        ScheduleBotTurnIfNeeded();
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
        if (IsCurrentPlayerBot() || isExecutingBotTurn) return false;
        if (IsLocalPlayersTurn()) return false;

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
        if (BlockActionOutsideLocalTurn()) return;
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
            Destroy(card.gameObject); 
            DrawNewCard(tier, parentContainer);
            
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
        Destroy(card.gameObject);
        DrawNewCard(tier, parentContainer);

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
        if (BlockActionOutsideLocalTurn()) return;
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
        CheckNobles(p);

        if (isOnlineMatchMode)
        {
            PublishOnlineEconomyState();
        }

        // ตรวจสอบการชนะเกม "ทันที" ในทุกๆ เทิร์น!
        // (เดิมทีเช็คเฉพาะตอนจบรอบ ทำให้แต้มถึง 20 แล้วยังรันเทิร์นต่อไปหาบอท)
        EvaluateWinCondition();
        if (isGameOver) return; // ปิดจ๊อบ ออกจากฟังก์ชันทันทีถ้ามีคนชนะ

        totalTurnCount++;
        currentPlayerIndex++;
        
        // วนรอบคิวตามจำนวน players จริงๆ ที่ถูกผูกไว้ใน Inspector
        if (players == null || players.Length == 0) return;
        
        bool isNewRound = false;
        bool startedQuizThisTurn = false;
        if (currentPlayerIndex >= activePlayerCount) {
            currentPlayerIndex = 0;
            currentRound++;
            isNewRound = true; // ระบุว่ากำลังขึ้นรอบใหม่
            Debug.Log($"\n========== เริ่มรอบที่ {currentRound} ==========\n");
        }

        // [Phase 2] อัปเดตเลขเทิร์นและเช็คการเรียกควิซ
        currentTurnDisplay = currentRound;
        UpdateTurnCountUI();

        bool shouldStartQuiz = isOnlineMatchMode
            ? isNewRound && currentTurnDisplay > 1 && currentTurnDisplay % Mathf.Max(1, onlineQuizTurnInterval) == 0
            : isNewRound && currentTurnDisplay > 1 && currentTurnDisplay % Mathf.Max(1, quizInterval) == 0;

        if (shouldStartQuiz) {
            if (QuizManager.Instance != null) {
                string quizTriggerLabel = isOnlineMatchMode
                    ? $"รอบที่ {currentTurnDisplay}"
                    : $"รอบที่ {currentTurnDisplay}";
                Debug.Log($"<color=cyan>[Quiz] {quizTriggerLabel} -> ถึงเวลาเรียกควิซแล้ว!</color>");
                startedQuizThisTurn = true;
                QuizManager.Instance.StartQuiz();
            }
        }

        ResetTimer();
        UpdateTurnVisuals();
        PublishOnlineEconomyState();
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
            if (players[i].currentScore >= winningScore && players[i].currentScore > highestScore) {
                highestScore = players[i].currentScore; 
                winner = players[i];
            }
        }
        
        if (winner != null) {
            isGameOver = true;
            
            // --- บันทึกสถิติ Coins และ Points เก็บไว้แสดงที่หน้า Main Menu ---
            int localSeatIndex = GetLocalPlayerUiIndex();
            if (localSeatIndex >= 0 &&
                localSeatIndex < players.Length &&
                players[localSeatIndex] != null &&
                !players[localSeatIndex].isBot)
            {
                int currentTotalCoins = PlayerPrefs.GetInt("TotalCoins", 0);
                int currentTotalPoints = PlayerPrefs.GetInt("TotalPoints", 0);
                
                int earnedCoins = GetTotalPlayerCoins(localSeatIndex);
                int earnedPoints = players[localSeatIndex].currentScore;

                PlayerPrefs.SetInt("TotalCoins", currentTotalCoins + earnedCoins);
                PlayerPrefs.SetInt("TotalPoints", currentTotalPoints + earnedPoints);
                PlayerPrefs.Save();
                Debug.Log($"[GameController] บันทึกสถิติ! เหรียญรวม: {currentTotalCoins + earnedCoins}, คะแนนรวม: {currentTotalPoints + earnedPoints}");
            }

            // โชว์หน้าสรุปผลตอนจบเกม
            if (resultScreen != null)
            {
                List<string> rankings = new List<string>();
                for (int i = 0; i < activePlayerCount; i++)
                {
                    if (players[i] != null)
                    {
                        rankings.Add($"{players[i].nameText.text} : {players[i].currentScore} แต้ม");
                    }
                }
                
                resultScreen.ShowResults("เกมจบแล้ว! ผู้ชนะคือ " + winner.nameText.text, rankings, true);
            }
            else
            {
                ShowWarning($"ผู้ชนะคือ {winner.nameText.text} {highestScore} แต้ม!");
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

    void DrawNewCard(int tier, Transform container) 
    {
        List<CardData> masterDeck = tier == 3 ? tier3Cards : tier == 2 ? tier2Cards : tier1Cards;
        if (masterDeck == null || masterDeck.Count == 0) return;

        List<CardData> cardsOnBoard = new List<CardData>();
        foreach (Transform child in container) {
            CardDisplay d = child.GetComponent<CardDisplay>();
            if (d != null && d.data != null) cardsOnBoard.Add(d.data);
        }

        List<CardData> availableCards = new List<CardData>();
        foreach (var card in masterDeck) if (!cardsOnBoard.Contains(card)) availableCards.Add(card);
        if (availableCards.Count == 0) availableCards.AddRange(masterDeck);

        CardData selectedCard = availableCards[Random.Range(0, availableCards.Count)]; 
        GameObject newCardObj = Instantiate(cardPrefab, container);
        newCardObj.GetComponent<CardDisplay>()?.LoadCardData(selectedCard);
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

    void EnsureBotController()
    {
        if (isOnlineMatchMode) return;
        if (botController == null) botController = GetComponent<BotController>();
        if (botController == null) botController = gameObject.AddComponent<BotController>();
    }

    bool IsCurrentPlayerBot()
    {
        if (isOnlineMatchMode) return false;
        if (players == null || players.Length == 0) return false;
        if (playOrder == null || playOrder.Length == 0) return false;
        if (currentPlayerIndex < 0 || currentPlayerIndex >= playOrder.Length) return false;

        int activePlayerIdx = playOrder[currentPlayerIndex];
        if (activePlayerIdx < 0 || activePlayerIdx >= players.Length) return false;

        return players[activePlayerIdx] != null && players[activePlayerIdx].isBot;
    }

    void ScheduleBotTurnIfNeeded()
    {
        if (isOnlineMatchMode) return;
        if (botTurnCoroutine != null) {
            StopCoroutine(botTurnCoroutine);
            botTurnCoroutine = null;
        }

        if (isGameOver || isWaitingForContinueAfterResult || !IsCurrentPlayerBot()) return;

        botTurnCoroutine = StartCoroutine(RunBotTurnAfterDelay());
    }

    IEnumerator RunBotTurnAfterDelay()
    {
        yield return new WaitForSeconds(botTurnDelay);
        botTurnCoroutine = null;

        if (isGameOver || isWaitingForContinueAfterResult || !IsCurrentPlayerBot()) yield break;

        EnsureBotController();
        isExecutingBotTurn = true;
        botController.ExecuteTurn(playOrder[currentPlayerIndex]);
        isExecutingBotTurn = false;
    }
    
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
            Debug.Log($"[GameController] Online PvP mode detected. Human player count={humanPlayerCount}, bots disabled.");
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
                        Debug.Log($"[GameController] Local player setup in slot {i + 1} as: {finalName} with character {p1Data.characterName}");
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
                        Debug.Log($"[GameController] Remote player slot {i + 1} configured as human.");
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
            }
        }

        ConfigureOnlinePlayerPanelLayout();
    }
    
    void ClearContainer(Transform c) { 
        if (c == null) return; 
        foreach (Transform child in c) Destroy(child.gameObject); 
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

            Debug.Log($"[GameController] Updated player slot {seatIndex + 1} name to {playerName}");
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

    private void ConfigureOnlinePlayerPanelLayout()
    {
        if (!isOnlineMatchMode || GetConfiguredOnlinePlayerCount() != 2 || players == null || players.Length < 2 || players[0] == null || players[1] == null)
        {
            return;
        }

        CapturePlayerPanelLayoutsIfNeeded();

        int localSeat = GetResolvedLocalPlayerSlotIndex();
        ApplyPlayerPanelLayout(players[0], capturedPlayerPanelLayouts[localSeat == 0 ? 0 : 1]);
        ApplyPlayerPanelLayout(players[1], capturedPlayerPanelLayouts[localSeat == 0 ? 1 : 0]);
    }

    private void CapturePlayerPanelLayoutsIfNeeded()
    {
        if (playerPanelLayoutsCaptured || players == null || players.Length < 2)
        {
            return;
        }

        capturedPlayerPanelLayouts = new PlayerPanelLayout[2];
        for (int i = 0; i < 2; i++)
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
            Debug.Log($"[Test] ให้โบนัสสีที่ {randomColorIndex} แก่ Player 1");
        }

        // เช็คว่าโบนัสพอที่จะเชิญขุนนางลงมาหาได้หรือยัง
        CheckNobles(p1);
    }

    public void CheckNobles(PlayerUI player)
    {
        // ต้องเช็คย้อนกลับ เพราะถ้าขุนนางหลุดจากกระดานไปหาผู้เล่น เราจะลบออกจากลิสต์ activeNobles
        for (int i = activeNobles.Count - 1; i >= 0; i--)
        {
            NobleDisplay nobleDisplay = activeNobles[i];
            NobleData data = nobleDisplay.nobleData;

            bool canClaim = true;
            // เช็คว่า Player มีโบนัส >= เงื่อนไขของขุนนางหรือไม่
            for (int b = 0; b < 5; b++)
            {
                if (player.bonuses[b] < data.requiredBonuses[b])
                {
                    canClaim = false;
                    break;
                }
            }

            if (canClaim)
            {
                Debug.Log($"[Noble] {player.nameText.text} ได้รับขุนนาง: {data.nobleName} (+{data.victoryPoints} VP)");
                
                // ให้คะแนน
                player.AddScore(data.victoryPoints);

                // แสดงบนการ์ดว่าถูกคนนี้เอาไปแล้ว (ให้อยู่กับที่ ไม่บินไปหา)
                nobleDisplay.ClaimNoble(player.nameText.text);

                // ลบออกจากกระดาน (ในแง่ของระบบโค้ด จะได้ไม่เอามาเช็คซ้ำ)
                activeNobles.RemoveAt(i);
            }
        }
    }
}
