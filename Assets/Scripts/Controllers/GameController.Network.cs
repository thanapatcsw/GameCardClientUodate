using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// ============================================================
// GameController — ส่วน Fusion online networking
//
//   จัดการทั้งหมดที่เกี่ยวกับการ sync state ระหว่างผู้เล่นใน mode ออนไลน์:
//   • Callbacks จาก FusionManager (turn/economy/board state, active players)
//   • Snapshot building & application (BuildEconomy/Apply, BuildBoard/Apply)
//   • Late-joiner support (HandleFullStateRequested)
//   • Player panel layout rotation (host ของห้อง ≠ คนแรกใน scene เสมอ)
//   • Online player-count resolution + display name helpers
//
//   หมายเหตุ: ClearContainer + PlayerPanelLayout struct ยังอยู่ในไฟล์หลัก
//   GameController.cs เพราะถูกใช้นอก network ด้วย (board spawn, layout capture)
// ============================================================
public partial class GameController
{
    // ───────── Top-level callbacks (รับจาก FusionManager events) ─────────

    // =============================================================================
    // HandleFullStateRequested — Host รับคำขอ Full State จาก Late-Joiner
    // -----------------------------------------------------------------------
    // ระบบ Late-Joiner:
    //   Client เข้าห้องหลัง Host เริ่มเกมแล้ว → ขอสถานะล่าสุด (Board + Economy + Turn)
    //   Host ส่งกลับเฉพาะคนนั้น (ไม่ broadcast ทั่ว — ไม่รีเซ็ท timer คนอื่น)
    // =============================================================================
    // host: late-joiner ขอ full state มา → ส่ง board/economy/turn ปัจจุบันกลับเฉพาะคนที่ขอ
    private void HandleFullStateRequested(int requesterPlayerId)
    {
        if (!isOnlineMatchMode || FusionManager.Instance == null || !FusionManager.Instance.IsMasterClient)
        {
            return;
        }

        GameLog.Log($"[GameController] Host ได้รับคำขอ full state จาก player {requesterPlayerId} → ส่งกลับเฉพาะคนนั้น");
        FusionManager.Instance.SendBoardStateToPlayer(requesterPlayerId, BuildBoardSnapshot());
        FusionManager.Instance.SendEconomyStateToPlayer(requesterPlayerId, BuildEconomySnapshot());
        // [Reconnect fix] แนบเวลาเทิร์นที่เหลือจริงของ host ไปด้วย — คน rejoin กลางเทิร์นจะได้ไม่เริ่มนับใหม่เต็มเวลา
        // [Reconnect ข้อ1] แนบ playOrder (คิวเทิร์นจริงหลังควิซสลับ) — คน rejoin สร้าง GameController ใหม่ (playOrder=[0,1,..])
        //   ถ้าไม่ส่งคิวไปด้วย currentPlayerIndex จะชี้คนละ seat → เทิร์นเพี้ยน (เห็นเป็นของคนอื่น) — ดู HandleOnlineTurnStateReceived
        FusionManager.Instance.SendTurnStateToPlayer(requesterPlayerId, currentPlayerIndex, currentRound, totalTurnCount, currentTurnDisplay,
            Mathf.CeilToInt(currentTurnTime), playOrder);
        // [TurnSync diag] ยืนยันว่า authority (build นี้) แนบคิวไปด้วย — ถ้าฝั่ง reconnect log "order=null" แปลว่า authority ยังเป็น build เก่า
        GameLog.Log($"[TurnSync] full-state→player {requesterPlayerId}: idx={currentPlayerIndex} order=[{(playOrder != null ? string.Join(",", playOrder) : "null")}]");
    }

    private bool IsMatchedOnlineSession()
    {
        // [FIX] เช็คจาก GameMode ที่ตั้งมาจากหน้าเลือกโหมดโดยตรง
        string gameMode = PlayerPrefs.GetString("GameMode", "Bot");
        if (gameMode == "Online") return true;
        if (gameMode == "Bot") return false;

        // Fallback แบบเดิมเผื่อกรณีอื่นๆ
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

        // [FIX] ถ้าเกมยังไม่เริ่ม ให้เรียก SetupPlayers() เหมือนเดิม
        // ถ้าเกมเริ่มไปแล้ว → ไม่เรียก SetupPlayers() อีก (กัน Turn Order รีเซ็ตกลางเกม)
        // สิ่งที่ทำแทน: แค่เช็คว่าคนไหนหลุด/เข้ามา และควบคุม isBot
        if (!hasStartedInitialGameplay)
        {
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

            GameLog.Log("[GameController] Online players ready. Starting PvP setup.");
            if (FusionManager.Instance != null && FusionManager.Instance.IsMasterClient)
            {
                PublishOnlineBoardState();
            }
            StartInitialGameplay();
        }
        else
        {
            // ——————————————————————————————————————————
            // เกมเริ่มไปแล้ว — ไม่รีเซ็ต SetupPlayers()
            // [FIX] เช็คว่ามี seat ไหนหลุดหายไป แล้วตั้งเป็น Bot สำหรับเกมเล่นต่อแทน
            // ——————————————————————————————————————————
            if (FusionManager.Instance != null)
            {
                UpdateDisconnectedPlayerStatus();
            }

            ApplyNetworkPlayerNamesToUi();
            UpdateTurnVisuals();

            // [Shared Mode · Step 6] seat อาจเพิ่งครบ/เพิ่งรู้ค่าหลังเกมเริ่ม (ตอน panel layout รันครั้งแรก
            //   _seatOrder ยังไม่ครบ → localSeat ผิด → panel ไม่สลับ). สั่งจัด panel ใหม่ด้วย localSeat ล่าสุด
            //   ปลอดภัย: ใช้ captured layout เดิม (pristine) แค่หมุนใหม่ ไม่รีเซ็ต turn order
            if (panelLayoutCoroutine != null) StopCoroutine(panelLayoutCoroutine);
            panelLayoutCoroutine = StartCoroutine(ConfigureOnlinePlayerPanelLayoutDeferred());

            if (ShouldWaitForOnlineOpponent())
            {
                ShowWarning("Waiting for opponent...");
            }
            else
            {
                ClearWarning();
                // ถ้าเทิร์นปัจจุบันเป็นของ slot ที่เพิ่งกลายเป็น Bot ให้ schedule bot turn
                ScheduleBotTurnIfNeeded();
            }
        }
    }

    // [FIX] เช็ค seat ว่าคนจริงยังเชื่อมต่ออยู่ไหม แล้วตั้ง isDisconnected = true/false (online เท่านั้น — ไม่แตะ isBot ของบอท offline)
    private void UpdateDisconnectedPlayerStatus()
    {
        if (FusionManager.Instance == null || players == null) return;
        if (FusionManager.Instance.Runner == null) return;

        // [Shared Mode · Step 5] เช็คการเชื่อมต่อรายตาม "stable seat map" (seat→PlayerId ตรึงไว้)
        //   ไม่ map ใหม่ตามรายชื่อ active สดๆ อีกแล้ว → seat ของคนที่เหลือไม่เลื่อนสวมรอยคนที่ออก
        int seatCount = activePlayerCount;
        for (int seat = 0; seat < seatCount && seat < players.Length; seat++)
        {
            if (players[seat] == null) continue;

            int seatPlayerId = FusionManager.Instance.GetPlayerIdBySeat(seat);
            bool isConnected = seatPlayerId >= 0 && FusionManager.Instance.IsPlayerConnected(seatPlayerId);
            bool wasDisconnected = players[seat].isDisconnected;

            if (!isConnected && !wasDisconnected)
            {
                // ออกไป — mark isDisconnected ไว้เพื่อ "บล็อก input + ใช้ตรวจ reconnect" เท่านั้น
                // ไม่มีบอทเล่นแทนแล้ว → เทิร์นของ seat นี้ถูกข้ามด้วย Turn Timer (ForceEndTurn)
                players[seat].isDisconnected = true;
                GameLog.Log($"[GameController] Seat {seat} หลุดเชื่อมต่อ → ข้ามเทิร์นอัตโนมัติ (timer) จนกว่าจะกลับเข้ามา");
            }
            else if (isConnected && wasDisconnected)
            {
                // ต่อกลับมา — คืนตัวเป็นผู้เล่นจริง + ดึง full state ล่าสุด
                players[seat].isDisconnected = false;
                GameLog.Log($"[GameController] Seat {seat} ต่อกลับมา → คืนตัวเป็นผู้เล่นจริง");
                FusionManager.Instance.RequestFullState();
            }
        }
    }

    private void HandleOnlineTurnStateReceived(int syncedCurrentPlayerIndex, int syncedRound, int syncedTotalTurnCount, int syncedTurnDisplay, int syncedRemainingSeconds, int[] syncedPlayOrder)
    {
        if (!isOnlineMatchMode)
        {
            return;
        }

        // [Reconnect ข้อ1] adopt "คิวเทิร์นจริง" จาก authority ก่อน map currentPlayerIndex
        //   ควิซสลับ playOrder; คน reconnect เกิด GameController ใหม่ (SetupPlayers รีเซ็ต playOrder=[0,1,..])
        //   ถ้าไม่ทับคิว → playOrder[currentPlayerIndex] ชี้คนละ seat กับ host → "เทิร์นกลายเป็นของคนอื่น แต่คนอื่นเห็นเทิร์นเดิม"
        //   (payload เก่า/คิวเสีย → syncedPlayOrder=null → คงคิวเดิม = พฤติกรรมก่อนหน้า)
        bool adoptedOrder = IsValidSyncedPlayOrder(syncedPlayOrder);
        if (adoptedOrder)
        {
            playOrder = syncedPlayOrder;
        }

        int orderLength = (playOrder != null && playOrder.Length > 0) ? playOrder.Length : Mathf.Max(1, activePlayerCount);
        currentPlayerIndex = Mathf.Clamp(syncedCurrentPlayerIndex, 0, orderLength - 1);

        // [TurnSync diag] ใช้ยืนยันว่า build นี้มี fix + playOrder ซิงก์จริง (ทั้ง 2 เครื่องต้องเห็น seat เดียวกันเป็นเทิร์น)
        //   ถ้า order=null แปลว่า "ผู้ส่ง (authority) เป็น build เก่า" ที่ยังไม่แนบ playOrder → ต้อง rebuild ฝั่งนั้นด้วย
        GameLog.Log($"[TurnSync] recv idx={syncedCurrentPlayerIndex} order=[{(syncedPlayOrder != null ? string.Join(",", syncedPlayOrder) : "null")}] adopted={adoptedOrder} " +
            $"→ playOrder=[{string.Join(",", playOrder)}] localSeat={GetLocalPlayerUiIndex()} curSeat={(currentPlayerIndex < playOrder.Length ? playOrder[currentPlayerIndex] : -1)} isLocalTurn={IsLocalPlayersTurn()}");
        currentRound = Mathf.Max(1, syncedRound);
        totalTurnCount = Mathf.Max(0, syncedTotalTurnCount);
        currentTurnDisplay = Mathf.Max(1, syncedTurnDisplay);

        // [Reconnect fix] ถ้าผู้ส่งแนบเวลาที่เหลือมา → ใช้ค่านั้น (rejoin กลางเทิร์นเวลาตรงกับคนอื่น ไม่นับใหม่)
        //   payload เก่า/ไม่ทราบ (-1) → fallback รีเซ็ตเต็มตามเดิม; เปลี่ยนเทิร์นปกติผู้ส่งแนบเวลาเต็มมาอยู่แล้ว = พฤติกรรมเดิม
        if (syncedRemainingSeconds > 0)
            currentTurnTime = Mathf.Min(syncedRemainingSeconds, turnDuration);
        else
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

    // [Desync fix] version ล่าสุดที่ apply ไปแล้ว — กัน snapshot เก่า (turn ก่อน) มาทับของใหม่
    private int _lastAppliedEconVersion = -1;
    private int _lastAppliedBoardVersion = -1;

    private void HandleOnlineEconomyStateReceived(FusionManager.EconomyStateSnapshot snapshot)
    {
        if (!isOnlineMatchMode)
        {
            return;
        }

        // ข้าม snapshot ที่ version เก่ากว่าที่ apply ไปแล้ว (= ต้นเหตุ "หยิบเหรียญแล้วโดน revert")
        if (snapshot.Version < _lastAppliedEconVersion)
        {
            return;
        }
        _lastAppliedEconVersion = snapshot.Version;

        ApplyEconomySnapshot(snapshot);
        EvaluateWinCondition();
    }

    // ───────── Player name + seat resolution ─────────

    private void ApplyNetworkPlayerNamesToUi()
    {
        if (players == null || FusionManager.Instance == null)
        {
            return;
        }

        // local seat = ตัวเราเอง (คำนวณจาก _seatOrder ล่าสุด — หลัง reconnect ถูกแก้โดย SEATMAP แล้ว)
        int localSeat = GetLocalPlayerUiIndex();
        string localName = GetConfiguredLocalPlayerName();

        for (int seatIndex = 0; seatIndex < activePlayerCount && seatIndex < players.Length; seatIndex++)
        {
            if (players[seatIndex] == null || players[seatIndex].nameText == null)
            {
                continue;
            }

            // seat ของเราเอง → ยืนยันชื่อเราเสมอ
            //   กันเคส reconnect: ตอน SetupPlayers ระบุ local seat ผิด (seatOrder ยังไม่พร้อม) แล้ว bake ชื่อเราลง seat อื่น
            if (seatIndex == localSeat)
            {
                players[seatIndex].nameText.text = localName;
                continue;
            }

            string playerName = FusionManager.Instance.GetPlayerNameBySeat(seatIndex);
            if (!string.IsNullOrWhiteSpace(playerName))
            {
                players[seatIndex].nameText.text = playerName;
                GameLog.Log($"[GameController] Updated player slot {seatIndex + 1} name to {playerName}");
            }
            else if (players[seatIndex].nameText.text == localName)
            {
                // ยังไม่รู้ชื่อจริงของ seat นี้ แต่ดันโชว์ "ชื่อเรา" (bake ผิดตอน reconnect) → เคลียร์เป็น placeholder
                players[seatIndex].nameText.text = "Online Player " + (seatIndex + 1);
            }
        }
    }

    // [NEW] รับ event PlayerCharacterReceived (playerId, characterIndex) จาก FusionManager
    // แล้วอัปเดต characterPortrait ของ seat ที่ตรงกับ playerId นั้น
    private void ApplyRemoteCharacterPortrait(int playerId, int characterIndex)
    {
        if (!isOnlineMatchMode || players == null || availableCharacters == null || availableCharacters.Length == 0)
        {
            return;
        }

        if (FusionManager.Instance == null) return;

        // หา seatIndex จาก playerId
        int seatIndex = FusionManager.Instance.GetSeatIndexForPlayerId(playerId);
        if (seatIndex < 0 || seatIndex >= players.Length || players[seatIndex] == null)
        {
            GameLog.Log($"[GameController] ApplyRemoteCharacterPortrait: seatIndex={seatIndex} invalid for playerId={playerId}");
            return;
        }

        int clampedIndex = Mathf.Clamp(characterIndex, 0, availableCharacters.Length - 1);
        CharacterData charData = availableCharacters[clampedIndex];

        if (players[seatIndex].characterPortrait != null && charData.portraitSprite != null)
        {
            players[seatIndex].characterPortrait.sprite = charData.portraitSprite;
            GameLog.Log($"[GameController] Set avatar for seat {seatIndex} (playerId={playerId}) → {charData.characterName}");
        }
    }

    // [NEW] รับ event PlayerFrameReceived (playerId, frameId) จาก FusionManager (ดึงจาก DB)
    // แล้วโหลด Sprite จาก Resources/Frames/ และ apply ลง nameFrameImage ของ seat นั้น
    private void ApplyRemoteNameFrame(int playerId, string frameId)
    {
        if (!isOnlineMatchMode || players == null || FusionManager.Instance == null) return;

        int seatIndex = FusionManager.Instance.GetSeatIndexForPlayerId(playerId);
        if (seatIndex < 0 || seatIndex >= players.Length || players[seatIndex] == null)
        {
            GameLog.Log($"[GameController] ApplyRemoteNameFrame: seatIndex={seatIndex} invalid for playerId={playerId}");
            return;
        }

        string effectiveId = string.IsNullOrEmpty(frameId) ? ShopManager.DEFAULT_FRAME : frameId;
        Sprite frameSprite = Resources.Load<Sprite>($"Frames/{effectiveId}");
        if (frameSprite == null && effectiveId != ShopManager.DEFAULT_FRAME)
            frameSprite = Resources.Load<Sprite>($"Frames/{ShopManager.DEFAULT_FRAME}");

        players[seatIndex].ApplyNameFrame(frameSprite, UnityEngine.Color.white);
        GameLog.Log($"[GameController] Set frame for seat {seatIndex} (playerId={playerId}) → {effectiveId}");
    }

    // ส่ง character ของเราให้คนอื่นเห็น หลัง Photon พร้อม (frame/re-apply ถูกถอดออกแล้ว)
    private IEnumerator DelayedSyncLocalProfile()
    {
        // รอให้ Photon พร้อมใช้งาน
        yield return new WaitForSeconds(1.0f);

        if (FusionManager.Instance != null && FusionManager.Instance.Runner != null)
        {
            int myCharIndex = UnityEngine.PlayerPrefs.GetInt("SelectedCharacter", 0);

            if (FusionManager.Instance.IsMasterClient)
                FusionManager.Instance.BroadcastLocalCharacter(myCharIndex);
            else
                FusionManager.Instance.SendLocalCharacterToServer(myCharIndex);
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

    // ───────── Turn state sync ─────────

    private void PublishOnlineTurnState()
    {
        if (!isOnlineMatchMode || FusionManager.Instance == null)
        {
            return;
        }

        // แนบเวลาที่เหลือด้วย — ตอนเปลี่ยนเทิร์นปกติ ResetTimer() ถูกเรียกก่อน publish เสมอ จึงเท่ากับเวลาเต็มพอดี
        // [Reconnect ข้อ1] แนบ playOrder ทุกครั้ง → คิวเทิร์นซิงก์ต่อเนื่อง (แม้พลาด quiz-reorder รอบก่อน เทิร์นถัดไปก็แก้ให้ตรง)
        FusionManager.Instance.SendTurnState(currentPlayerIndex, currentRound, totalTurnCount, currentTurnDisplay,
            Mathf.CeilToInt(currentTurnTime), playOrder);
    }

    // playOrder ที่รับมาจาก network ต้องอ้าง seat ในช่วงที่ถูกต้องทุกตัว ถึงจะเอามาทับของเดิม
    //   (กัน index หลุดช่วงตอน playOrder[currentPlayerIndex] ใน EndTurn/UpdateTurnVisuals/หยิบเหรียญ)
    private bool IsValidSyncedPlayOrder(int[] order)
    {
        if (order == null || order.Length == 0) return false;
        int seatCount = players != null ? players.Length : 0;
        if (seatCount == 0) return false;
        foreach (int seat in order)
        {
            if (seat < 0 || seat >= seatCount) return false;
        }
        return true;
    }

    // ───────── Economy state sync (bank + each player's coins/bonuses/score) ─────────

    // =============================================================================
    // PublishOnlineEconomyState + BuildEconomySnapshot + ApplyEconomySnapshot
    // -----------------------------------------------------------------------
    // Economy Snapshot = ภาพรวมเศรษฐกิจทั้งเกม:
    //   - BankCoins: เหรียญในกองกลาง [6 สี]
    //   - Players[]: ต่อใจแต่ละคน (Score, Coins, Bonuses, ReservedCards)
    // Build = สร้าง snapshot จาก state ปัจจุบันในเครื่อง
    // Apply = เขียนทับ snapshot ที่ได้รับมาลงในเครื่องนี้
    // =============================================================================
    public void PublishOnlineEconomyState()
    {
        if (!isOnlineMatchMode || FusionManager.Instance == null)
        {
            return;
        }

        var econSnap = BuildEconomySnapshot();
        FusionManager.Instance.SendEconomyState(econSnap);
    }

    private FusionManager.EconomyStateSnapshot BuildEconomySnapshot()
    {
        var snapshot = new FusionManager.EconomyStateSnapshot
        {
            BankCoins = (int[])bankCoins.Clone(),
            Players = new FusionManager.EconomyPlayerSnapshot[activePlayerCount],
            Version = totalTurnCount   // logical clock (sync อยู่แล้ว) — กัน snapshot เก่าทับ
        };

        for (int i = 0; i < activePlayerCount; i++)
        {
            PlayerUI player = players[i];
            snapshot.Players[i] = new FusionManager.EconomyPlayerSnapshot
            {
                Score = player != null ? player.currentScore : 0,
                Coins = player != null ? (int[])player.coins.Clone() : new int[6],
                Bonuses = player != null ? (int[])player.bonuses.Clone() : new int[5],
                QuizBlackCoins = player != null ? player.quizBlackCoins : 0,
                ReservedCardIds = player != null ? player.reservedCards.Select(c => c.cardId).ToArray() : System.Array.Empty<string>()
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

                if (snapshot.Players[i].ReservedCardIds != null)
                {
                    RebuildReservedAreaIfChanged(player, snapshot.Players[i].ReservedCardIds);
                }
            }
        }

        UpdateBankUI();
    }

    // ───────── Board state sync (face-up market) ─────────

    // =============================================================================
    // PublishOnlineBoardState + BuildBoardSnapshot + ApplyBoardSnapshot
    // -----------------------------------------------------------------------
    // Board Snapshot = สถานะการ์ดบนกระดาน (face-up market):
    //   - Tier1/2/3CardIds: cardId สำหรับทุกช่อง (""=ช่องว่าง)
    //   - UsedCardIds: card ที่ถูกจั่วแล้วทั้งหมด (กันการ์ดซ้ำข้ามเครื่อง)
    // RebuildTierIfChanged — เทียบสถานะก่อนบน rebuild ทุกเทิร์น (กันการ์ดกระพริบ)
    // =============================================================================
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
            UsedCardIds = new List<string>(usedCardIds).ToArray(),
            Version = totalTurnCount,   // logical clock — กัน snapshot เก่าทับ
            // [Noble sync] ชุดขุนนาง + ใคร claim ใบไหน — ให้ทุกเครื่องเห็นชุดเดียวกันและเห็นการ claim
            NobleEntries = nobleManager != null ? nobleManager.BuildSyncEntries() : null
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

        // ข้าม snapshot กระดานที่เก่ากว่าที่ apply แล้ว (กันการ์ดที่เพิ่งเปลี่ยนโดน revert)
        if (snapshot.Version < _lastAppliedBoardVersion)
        {
            return;
        }
        _lastAppliedBoardVersion = snapshot.Version;

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

        // [Noble sync] ชุดขุนนาง + สถานะ claim ตามผู้ส่ง (null = ผู้ส่ง build เก่า/ยัง setup ไม่เสร็จ → ข้าม)
        //   nobleManager อาจยัง null ถ้า snapshot มาก่อน StartInitialGameplay — ข้ามได้ เดี๋ยว publish รอบถัดไปตามมา
        if (snapshot.NobleEntries != null && nobleManager != null)
        {
            nobleManager.SyncFromEntries(snapshot.NobleEntries);
        }
    }

    private void RebuildTierIfChanged(Transform container, string[] cardIds)
    {
        if (container == null || cardIds == null)
        {
            return;
        }

        // เทียบสถานะปัจจุบันกับที่รับมา — ถ้าเหมือนกันแล้วไม่ rebuild (กันการ์ดกระพริบทุกเทิร์น)
        // เก็บ "" (ช่องว่างจากกองหมด) ไว้ด้วย เพื่อรักษา "ตำแหน่งช่อง" ให้ตรงกันทุกเครื่อง (ไม่งั้นการ์ดเลื่อน/desync)
        List<string> incoming = new List<string>(cardIds);

        List<string> current = new List<string>();
        foreach (Transform child in container)
        {
            CardDisplay display = child.GetComponent<CardDisplay>();
            current.Add(display != null && display.data != null ? display.data.cardId : string.Empty);
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
            // ช่องว่าง (กองหมด) → วาง placeholder รักษาตำแหน่ง ไม่ให้การ์ดที่เหลือเลื่อน
            if (string.IsNullOrEmpty(id))
            {
                SpawnEmptyCardSlot(container);
                continue;
            }

            CardData data = FindCardDataById(id);
            if (data == null)
            {
                Debug.LogWarning($"[GameController] ไม่พบ CardData สำหรับ cardId '{id}' ตอน sync กระดาน");
                SpawnEmptyCardSlot(container); // กันช่องหาย/เลื่อน ถ้าหา data ไม่เจอ
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

    private void RebuildReservedAreaIfChanged(PlayerUI player, string[] cardIds)
    {
        if (player == null || cardIds == null)
        {
            return;
        }

        // เทียบสถานะปัจจุบันกับที่รับมา
        List<string> incoming = new List<string>();
        foreach (string id in cardIds)
        {
            if (!string.IsNullOrEmpty(id)) incoming.Add(id);
        }

        List<string> current = new List<string>();
        if (player.reservedCards != null)
        {
            foreach (CardData card in player.reservedCards)
            {
                if (card != null) current.Add(card.cardId);
            }
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

        // เคลียร์และสร้างข้อมูลใหม่
        if (player.reservedCards != null)
        {
            player.reservedCards.Clear();
            foreach (string id in incoming)
            {
                CardData data = FindCardDataById(id);
                if (data != null)
                {
                    player.reservedCards.Add(data);
                }
                else
                {
                    Debug.LogWarning($"[GameController] ไม่พบ CardData '{id}' ตอน sync reserved cards ของผู้เล่น {player.nameText?.text}");
                }
            }
        }

        // เคลียร์และสร้าง UI ใหม่
        if (player.reservedAreaTransform != null && cardPrefab != null)
        {
            ClearContainer(player.reservedAreaTransform);
            foreach (CardData data in player.reservedCards)
            {
                GameObject resCard = Instantiate(cardPrefab, player.reservedAreaTransform);
                resCard.transform.localScale = new Vector3(0.6f, 0.6f, 1f);

                CardDisplay resDisplay = resCard.GetComponent<CardDisplay>();
                if (resDisplay != null)
                {
                    resDisplay.LoadCardData(data);
                    resDisplay.isReserved = true;
                    resDisplay.ownerUI = player;
                }
            }
        }
    }

    // ───────── Player panel layout rotation (host ≠ คนแรกใน scene ของแต่ละ client) ─────────

    private Coroutine panelLayoutCoroutine;

    // =============================================================================
    // ConfigureOnlinePlayerPanelLayout — หมุน Player Panel
    // -----------------------------------------------------------------------
    // ปัญหา: ใน Online โหมด แต่ละ client มี localSeat ต่างกัน
    // เช่น: localSeat=1 ควรเห็น ตัวเองอยู่ล่าง (panel[0]) เหมือน seat 0 สำหรับ คนที่ seat=0
    // GetRotatedLayoutIndex — คำนวณ Index หลังหมุน โดย localSeat เสมออยู่ที่ตำแหน่ง 0
    // =============================================================================
    private IEnumerator ConfigureOnlinePlayerPanelLayoutDeferred()
    {
        // รอให้ผ่าน layout pass + render ของเฟรมนี้ก่อน แล้วบังคับอัปเดต canvas ให้ตำแหน่งนิ่ง
        yield return new WaitForEndOfFrame();
        Canvas.ForceUpdateCanvases();
        ConfigureOnlinePlayerPanelLayout();
        panelLayoutCoroutine = null;
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

        // หา "ช่องบ้าน" = ตำแหน่งซ้ายล่างสุด → ยกให้เจ้าของเครื่อง (local) เสมอ
        // (ไม่ผูกกับว่า players[0] วางตรงไหนในซีน — โค้ดเลือกช่องซ้ายล่างเอง)
        int homeSlot = GetBottomLeftLayoutIndex(layoutCount);
        GameLog.Log($"[GameController] Panel layout: localSeat={localSeat}, homeSlot(ซ้ายล่าง)={homeSlot}, layoutCount={layoutCount}");

        for (int seatIndex = 0; seatIndex < layoutCount; seatIndex++)
        {
            if (players[seatIndex] == null)
            {
                continue;
            }

            int rotatedLayoutIndex = GetRotatedLayoutIndex(seatIndex, localSeat, homeSlot, layoutCount);
            ApplyPlayerPanelLayout(players[seatIndex], capturedPlayerPanelLayouts[rotatedLayoutIndex]);
        }
    }

    // ช่อง layout ที่อยู่ "ซ้ายล่างสุด" บนจอ — เลือกจากตำแหน่งจริง (x น้อย + y น้อย = ใกล้มุมซ้ายล่าง (0,0) ของจอ Unity)
    private int GetBottomLeftLayoutIndex(int layoutCount)
    {
        int bestIndex = 0;
        float bestScore = float.MaxValue;
        for (int i = 0; i < layoutCount && i < capturedPlayerPanelLayouts.Length; i++)
        {
            Vector3 pos = capturedPlayerPanelLayouts[i].WorldPosition;
            float score = pos.x + pos.y; // ยิ่งน้อย = ยิ่งใกล้ซ้ายล่าง
            if (score < bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }
        return bestIndex;
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
                SiblingIndex = rectTransform.GetSiblingIndex(),
                WorldPosition = rectTransform.position
            };
        }

        playerPanelLayoutsCaptured = true;
    }

    // คืน index ของ captured layout ที่ seat นี้ควรไปอยู่
    //   local seat → homeSlot (ซ้ายล่าง) เสมอ, seat อื่นวนรอบโต๊ะถัดจาก homeSlot ตามลำดับ seat
    private static int GetRotatedLayoutIndex(int seatIndex, int localSeatIndex, int homeSlot, int layoutCount)
    {
        if (layoutCount <= 0)
        {
            return 0;
        }

        int offset = (((seatIndex - localSeatIndex) % layoutCount) + layoutCount) % layoutCount;
        return ((homeSlot + offset) % layoutCount + layoutCount) % layoutCount;
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

        // [FIX] ปรับตำแหน่ง reservedAreaTransform ให้อยู่ด้านล่าง Panel เสมอ
        // Player Panel ที่อยู่ด้านบนหน้าจอ (anchorMin.y >= 0.5) จะต้อง flip กรอบจองการ์ดลงล่าง
        if (player.reservedAreaTransform != null && player.reservedAreaTransform.TryGetComponent(out RectTransform reservedRT))
        {
            bool isPanelOnTop = layout.AnchorMin.y >= 0.5f;

            if (isPanelOnTop)
            {
                // Panel อยู่ด้านบน → ย้ายกรอบจองการ์ดลงด้านล่างของ Panel
                reservedRT.anchorMin = new UnityEngine.Vector2(reservedRT.anchorMin.x, 0f);
                reservedRT.anchorMax = new UnityEngine.Vector2(reservedRT.anchorMax.x, 0f);
                // Pivot ไว้ด้านบนของ element → element จะห้อยลงจาก anchor
                reservedRT.pivot = new UnityEngine.Vector2(0.5f, 1f);
                // anchoredPosition.y เป็น 0 = ติดกับขอบล่างของ Panel พอดี
                reservedRT.anchoredPosition = new UnityEngine.Vector2(reservedRT.anchoredPosition.x, 0f);
            }
            else
            {
                // Panel อยู่ด้านล่าง → กรอบจองการ์ดอยู่ด้านบนของ Panel (layout ปกติ)
                reservedRT.anchorMin = new UnityEngine.Vector2(reservedRT.anchorMin.x, 1f);
                reservedRT.anchorMax = new UnityEngine.Vector2(reservedRT.anchorMax.x, 1f);
                reservedRT.pivot = new UnityEngine.Vector2(0.5f, 0f);
                reservedRT.anchoredPosition = new UnityEngine.Vector2(reservedRT.anchoredPosition.x, 0f);
            }
        }
    }


    // ───────── Online room helpers ─────────

    private int GetConfiguredOnlinePlayerCount()
    {
        // ถ้าเป็นห้องที่สร้างเอง (ไม่มีรหัส AutoMatchmaking) ให้นับจำนวนคนในห้องของ FusionManager เลย
        if (FusionManager.Instance != null && string.IsNullOrWhiteSpace(PlayerPrefs.GetString(MatchmakingRoomCodePrefsKey, string.Empty)))
        {
            return Mathf.Clamp(FusionManager.Instance.ActivePlayerCount, 2, 4);
        }

        return Mathf.Clamp(PlayerPrefs.GetInt(MatchmakingTargetPlayerCountPrefsKey, 2), 2, Mathf.Min(4, players != null && players.Length > 0 ? players.Length : 4));
    }
}
