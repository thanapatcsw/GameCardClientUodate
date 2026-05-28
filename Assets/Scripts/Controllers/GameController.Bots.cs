using System.Collections;
using UnityEngine;

// ============================================================
// GameController — ส่วน Bot AI execution
//   จัดการ coroutine สั่ง BotController.ExecuteTurn เมื่อถึงเทิร์นของบอท
//   (offline single-player เท่านั้น — online มี BotController ไม่ทำงาน)
// ============================================================
public partial class GameController
{
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
        float delay = Random.Range(botTurnDelayMin, botTurnDelayMax);
        yield return new WaitForSeconds(delay);
        botTurnCoroutine = null;

        if (isGameOver || isWaitingForContinueAfterResult || !IsCurrentPlayerBot()) yield break;

        EnsureBotController();
        isExecutingBotTurn = true;
        botController.ExecuteTurn(playOrder[currentPlayerIndex]);
        isExecutingBotTurn = false;
    }
}
