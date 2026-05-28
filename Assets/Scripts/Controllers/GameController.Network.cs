using UnityEngine;

// ============================================================
// GameController — ส่วน Fusion online callbacks (high-level)
//   จัดการ event ที่รับเข้ามาจาก FusionManager เช่นการ sync state, late joiner,
//   active players เปลี่ยนแปลง ฯลฯ
//
//   หมายเหตุ: ส่วน infrastructure ขั้นต่ำ (Publish/Build/Apply snapshot,
//   player panel layout, ฯลฯ) ยังคงอยู่ในไฟล์หลัก GameController.cs ช่วงท้าย
//   เพื่อลดความเสี่ยงในการย้ายโค้ดจำนวนมากใกล้ deadline
// ============================================================
public partial class GameController
{
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

        GameLog.Log("[GameController] Online players ready. Refreshing PvP setup.");

        // Host re-broadcast กระดานปัจจุบันให้ผู้เล่นที่เพิ่งเข้ามา (late joiner) เห็นตรงกัน
        if (FusionManager.Instance != null && FusionManager.Instance.IsMasterClient)
        {
            PublishOnlineBoardState();
        }

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
}
