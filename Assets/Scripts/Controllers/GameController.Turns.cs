using System.Collections.Generic;
using UnityEngine;

// ============================================================
// GameController — ส่วน Turn flow & win condition
//
//   ครอบคลุมกลไกหมุนเทิร์น + เงื่อนไขชนะ + state guard ของ input:
//
//   Turn order / state setters:
//     ApplyNewTurnOrder         จัดลำดับใหม่จากผลควิซ
//     SetGameplayInputLocked    ล็อก input ระหว่างควิซ/ผลแจ้ง
//     SetWaitingForContinueAfterResult, OnResultScreenClosed, SetPendingQuizTurnOrder
//
//   Action guards (เรียกจากปุ่ม UI):
//     BlockActionDuringQuiz / BlockActionUntilContinue
//     IsLocalPlayersTurn / BlockActionOutsideLocalTurn
//
//   Turn flow:
//     EndTurn                   จบเทิร์น → bank settle, check noble, advance index,
//                                check quiz trigger, schedule bot, publish sync
//     ResetTimer / UpdateTurnVisuals / UpdateTurnCountUI
//
//   Win condition:
//     EvaluateWinCondition / CheckWinCondition (ตรวจครบ 20 แต้ม +
//       MMR/gem reward + ResultScreen + Supabase room='finished')
// ============================================================
public partial class GameController
{
    // ───────── Turn order / state setters ─────────

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

    // ───────── Action guards (input gating) ─────────

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

    // ───────── End-of-turn flow ─────────

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

    // ───────── Win condition check + end-game rewards ─────────

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

    // ───────── Visual / timer helpers ─────────

    void ResetTimer() { currentTurnTime = turnDuration; }

    void UpdateTurnVisuals() {
        if (players == null || players.Length == 0) return;
        if (playOrder == null || playOrder.Length == 0) return;
        if (currentPlayerIndex >= playOrder.Length) currentPlayerIndex = 0;

        int activePlayerIdx = playOrder[currentPlayerIndex];
        for (int i = 0; i < players.Length; i++)
            if (players[i] != null) players[i].SetActiveTurn(i == activePlayerIdx);
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

    // ───────── ForceEndTurn — Host-only, ไม่ตรวจ IsLocalPlayersTurn (bypass guards) ─────────
    // ใช้เมื่อ: (1) หมดเวลาและเป็นเทิร์นของ Remote Player/Bot
    //              (2) ผู้เล่นหลุดกลางเทิร์นและไม่มีตัวแทนในระบบ
    public void ForceEndTurn()
    {
        if (isGameOver) return;
        if (BlockActionDuringQuiz()) return;
        if (BlockActionUntilContinue()) return;

        GameLog.Log($"[GameController] ForceEndTurn: บังคับจบเทิร์น (bypass guards) สำหรับ Player slot {playOrder[currentPlayerIndex]}");

        // ยกเลิกเหรียญค้างแล้ว Sync
        if (GetTotalPendingCoins() > 0) {
            System.Array.Clear(pendingCoins, 0, pendingCoins.Length);
            foreach (var btn in bankButtons) if (btn != null) btn.UpdatePendingUI(0);
        }

        if (isOnlineMatchMode)
        {
            PublishOnlineBoardState();
            PublishOnlineEconomyState();
        }

        UpdateBankUI();
        ClearWarning();

        // Noble check
        if (playOrder != null && playOrder.Length > currentPlayerIndex)
        {
            PlayerUI p = players[playOrder[currentPlayerIndex]];
            nobleManager?.CheckClaim(p);
        }

        // [REVERT] ตรวจสอบการชนะเกม "ทันที" ในทุกๆ เทิร์น!
        EvaluateWinCondition();
        if (isGameOver) return;

        // เลื่อนตัวเลขเทิร์น
        totalTurnCount++;
        currentPlayerIndex++;

        if (playOrder == null || playOrder.Length == 0) return;

        bool isNewRound = false;
        if (currentPlayerIndex >= playOrder.Length) {
            currentPlayerIndex = 0;
            currentRound++;
            isNewRound = true;
            GameLog.Log($"\n========== [Force] เริ่มรอบที่ {currentRound} ==========\n");
        }

        currentTurnDisplay = currentRound;
        UpdateTurnCountUI();

        bool shouldStartQuiz = isOnlineMatchMode
            ? isNewRound && (currentTurnDisplay - 1) > 0 && (currentTurnDisplay - 1) % Mathf.Max(1, onlineQuizTurnInterval) == 0
            : isNewRound && (currentTurnDisplay - 1) > 0 && (currentTurnDisplay - 1) % Mathf.Max(1, quizInterval) == 0;

        if (shouldStartQuiz && QuizManager.Instance != null) {
            bool canStartQuizLocally = !isOnlineMatchMode || (FusionManager.Instance != null && FusionManager.Instance.IsMasterClient);
            if (canStartQuizLocally)
                QuizManager.Instance.StartQuiz();
            else if (FusionManager.Instance != null)
                FusionManager.Instance.RequestQuizStart();
        }

        ResetTimer();
        UpdateTurnVisuals();
        PublishOnlineTurnState();
        if (!shouldStartQuiz) ScheduleBotTurnIfNeeded();
    }
}
