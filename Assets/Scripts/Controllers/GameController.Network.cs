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
        FusionManager.Instance.SendTurnStateToPlayer(requesterPlayerId, currentPlayerIndex, currentRound, totalTurnCount, currentTurnDisplay);
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
                UpdateDisconnectedPlayerBotStatus();
            }

            ApplyNetworkPlayerNamesToUi();
            UpdateTurnVisuals();

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

    // [FIX] เช็ค seat index ที่ Fusion ไม่มีตัวตนอยู่แล้ว และตั้ง isBot = true/false ตามสถานะการเชื่อมต่อ
    private void UpdateDisconnectedPlayerBotStatus()
    {
        if (FusionManager.Instance == null || players == null) return;

        var activePlayers = FusionManager.Instance.Runner?.ActivePlayers;
        if (activePlayers == null) return;

        // สร้างชุดของ seat index ที่ยังเชื่อมอยู่
        var connectedSeats = new System.Collections.Generic.HashSet<int>();
        int seatCount = activePlayerCount;
        foreach (var player in activePlayers)
        {
            int seatIndex = FusionManager.Instance.GetLocalPlayerSeatIndex();
            // Map PlayerId -> seat index โดยเรียงลำดับ (Ordered)
            int orderedIdx = 0;
            foreach (var op in FusionManager.Instance.Runner.ActivePlayers
                         .OrderBy(p => p.PlayerId))
            {
                if (op == player) { connectedSeats.Add(orderedIdx); break; }
                orderedIdx++;
            }
        }

        for (int seat = 0; seat < seatCount && seat < players.Length; seat++)
        {
            if (players[seat] == null) continue;
            bool isConnected = connectedSeats.Contains(seat);
            bool wasBot = players[seat].isBot;

            if (!isConnected && !wasBot)
            {
                // ออกไป — เปลี่ยนเป็น Bot ชั่วคราว
                players[seat].isBot = true;
                GameLog.Log($"[GameController] Seat {seat} หลุดเชื่อมต่อ → เปลี่ยนเป็น Bot ชั่วคราว");
            }
            else if (isConnected && wasBot && seat < seatCount)
            {
                // ต่อกลับมา — ถ้า seat นี้ในอดีตเคยเป็นคนจริง (ไม่ใช่ Bot ดั้งเดิม)
                // เขาต่อกลับมา — reset isBot = false
                players[seat].isBot = false;
                GameLog.Log($"[GameController] Seat {seat} ต่อกลับมา → คืนตัวเป็นผู้เล่นจริง");
                FusionManager.Instance.RequestFullState();
            }
        }
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

    // ───────── Player name + seat resolution ─────────

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

    // ───────── Turn state sync ─────────

    private void PublishOnlineTurnState()
    {
        if (!isOnlineMatchMode || FusionManager.Instance == null)
        {
            return;
        }

        FusionManager.Instance.SendTurnState(currentPlayerIndex, currentRound, totalTurnCount, currentTurnDisplay);
    }

    // ───────── Economy state sync (bank + each player's coins/bonuses/score) ─────────

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

    // ───────── Board state sync (face-up market) ─────────

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

    // ───────── Player panel layout rotation (host ≠ คนแรกใน scene ของแต่ละ client) ─────────

    private Coroutine panelLayoutCoroutine;

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
