using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class QuizManager : MonoBehaviour
{
    public static QuizManager Instance { get; private set; }

    [System.Serializable]
    public class QuizQuestion
    {
        [TextArea(2, 4)]
        public string questionText;
        public string[] choices = new string[4];
        [Tooltip("กรอก 0, 1, 2 หรือ 3")]
        public int correctChoiceIndex;
    }

    [System.Serializable]
    public class PlayerAnswer
    {
        public int playerIndex;
        public bool isCorrect;
        public float timeTaken;
    }

    public GameController gameController;

    [Header("---- คลังคำถาม (Quiz Database) ----")]
    public List<QuizQuestion> questionDatabase;
    private QuizQuestion currentQuestion;

    [Header("---- UI หน้าต่างคำถาม ----")]
    public GameObject quizPanel;
    public TextMeshProUGUI questionText;
    public TextMeshProUGUI timerText;
    public Button[] answerButtons;
    public TextMeshProUGUI[] answerChoiceTexts;
    public TextMeshProUGUI rewardText;
    public ResultScreenUI resultScreen;

    [Header("---- ตั้งค่าเวลา ----")]
    public float timeLimit = 10f;
    private float currentTime;
    private bool isQuizActive;
    private bool isWaitingForOnlineResults;

    private readonly List<PlayerAnswer> currentAnswers = new List<PlayerAnswer>();

    public bool IsQuizActive => isQuizActive;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        if (quizPanel != null) quizPanel.SetActive(false);
    }

    private void OnEnable()
    {
        SubscribeNetworkEvents();
    }

    private void OnDisable()
    {
        UnsubscribeNetworkEvents();
    }

    private void Update()
    {
        if (!isQuizActive)
        {
            return;
        }

        if (isWaitingForOnlineResults && !IsOnlineQuizHost())
        {
            if (timerText != null) timerText.text = "WAIT";
            return;
        }

        currentTime -= Time.deltaTime;
        if (timerText != null) timerText.text = Mathf.Max(0, Mathf.CeilToInt(currentTime)).ToString();

        if (currentTime > 0f)
        {
            return;
        }

        if (IsOnlineQuizMode())
        {
            HandleOnlineQuizTimeout();
        }
        else
        {
            ForceEndQuiz();
        }
    }

    public void StartQuiz()
    {
        SubscribeNetworkEvents();

        if (questionDatabase == null || questionDatabase.Count == 0)
        {
            Debug.LogError("ยังไม่ได้ใส่คำถามใน Database!");
            return;
        }

        if (IsOnlineQuizMode())
        {
            if (IsOnlineQuizHost())
            {
                int questionIndex = Random.Range(0, questionDatabase.Count);
                StartQuizInternal(questionIndex);
                FusionManager.Instance.SendQuizStart(questionIndex);
            }
            else
            {
                PrepareClientWaitingForQuizStart();

                if (FusionManager.Instance.TryConsumePendingQuizStart(out int bufferedQuestionIndex))
                {
                    StartQuizInternal(bufferedQuestionIndex);
                }
            }

            return;
        }

        StartQuizInternal(Random.Range(0, questionDatabase.Count));
    }

    public void OnClickAnswer(int choiceIndex)
    {
        if (!isQuizActive || isWaitingForOnlineResults || currentQuestion == null)
        {
            return;
        }

        int myIndex = gameController != null ? gameController.LocalPlayerSeatIndex : 0;
        if (currentAnswers.Any(answer => answer.playerIndex == myIndex))
        {
            return;
        }

        float timeUsed = Mathf.Clamp(timeLimit - currentTime, 0f, timeLimit);
        bool correct = choiceIndex == currentQuestion.correctChoiceIndex;

        Debug.Log($"<color=yellow>[Quiz] Player {myIndex + 1} answered {choiceIndex}. Correct answer is {currentQuestion.correctChoiceIndex}. Result: {(correct ? "correct" : "wrong")}</color>");

        UpsertAnswer(myIndex, correct, timeUsed);
        DisableAnswerButtons();

        if (IsOnlineQuizMode())
        {
            if (IsOnlineQuizHost())
            {
                if (HaveAllPlayersAnswered())
                {
                    ForceEndQuiz();
                }
            }
            else
            {
                FusionManager.Instance?.SendQuizAnswer(myIndex, correct, timeUsed);
                BeginWaitingForOnlineResults();
            }

            return;
        }

        StartCoroutine(WaitAndFinishQuiz());
    }

    private System.Collections.IEnumerator WaitAndFinishQuiz()
    {
        yield return new WaitForSeconds(1.0f);

        if (gameController == null || !gameController.IsOnlineMatchMode)
        {
            SimulateOtherPlayers(gameController != null ? gameController.LocalPlayerSeatIndex : 0);
        }

        ForceEndQuiz();
    }

    private void SimulateOtherPlayers(int excludeIndex)
    {
        int totalPlayers = GetTotalPlayersForQuiz();
        for (int i = 0; i < totalPlayers; i++)
        {
            if (i == excludeIndex)
            {
                continue;
            }

            float botDifficulty = Random.Range(0.4f, 0.8f);
            float botSpeed = Random.Range(1.0f, timeLimit);
            UpsertAnswer(i, Random.value < botDifficulty, botSpeed);
        }
    }

    private void ForceEndQuiz()
    {
        if (IsOnlineQuizMode() && !IsOnlineQuizHost())
        {
            return;
        }

        isQuizActive = false;
        isWaitingForOnlineResults = false;
        if (gameController != null) gameController.SetGameplayInputLocked(false);
        if (quizPanel != null) quizPanel.SetActive(false);

        if (currentAnswers.Count == 0)
        {
            Debug.Log("<color=red>หมดเวลา! ไม่มีใครตอบเลย</color>");
        }

        List<int> rewardGemIndices = DetermineRewardGemIndices(currentAnswers);
        ProcessQuizResults(currentAnswers, rewardGemIndices);
        gameController?.PublishOnlineEconomyState();
        gameController?.EvaluateWinCondition();

        if (IsOnlineQuizHost() && FusionManager.Instance != null)
        {
            FusionManager.Instance.SendQuizResults(
                currentAnswers.Select(answer => new FusionManager.QuizAnswerSnapshot
                {
                    PlayerIndex = answer.playerIndex,
                    IsCorrect = answer.isCorrect,
                    TimeTaken = answer.timeTaken
                }),
                rewardGemIndices);
        }
    }

    public void ProcessQuizResults(List<PlayerAnswer> answers, List<int> forcedRewardGemIndices = null, bool applyRewardsToState = true)
    {
        int totalPlayers = GetTotalPlayersForQuiz();
        if (totalPlayers <= 0)
        {
            Debug.LogWarning("[Quiz] No players available to rank.");
            return;
        }

        List<PlayerAnswer> rankedPlayers = BuildRankedPlayers(answers, totalPlayers);

        Debug.Log("\n<color=yellow>=== สรุปผลการตอบคำถาม ===</color>");
        for (int i = 0; i < rankedPlayers.Count; i++)
        {
            PlayerAnswer answer = rankedPlayers[i];
            Debug.Log($"อันดับ {i + 1}: ผู้เล่น {answer.playerIndex + 1} ตอบถูก: {answer.isCorrect} เวลา: {answer.timeTaken:F2} s");
        }

        PlayerAnswer winner = rankedPlayers[0];
        bool hasWinner = winner.isCorrect;

        if (hasWinner)
        {
            List<int> rewardGemIndices = forcedRewardGemIndices ?? new List<int>();
            string rewardMessage = applyRewardsToState
                ? ApplyRewardGemIndices(winner.playerIndex, rewardGemIndices)
                : DescribeRewardGemIndices(rewardGemIndices);
            Debug.Log($"<color=green>ผู้เล่น {winner.playerIndex + 1} ชนะควิซ! {rewardMessage}</color>");

            if (rewardText != null)
            {
                string playerName = GetPlayerName(winner.playerIndex);
                rewardText.text = $"ผู้ชนะ: {playerName}\n{rewardMessage}";
            }
        }
        else
        {
            Debug.Log("<color=red>ไม่มีใครตอบถูกเลย! อดรางวัลทั้งหมด</color>");
            if (rewardText != null) rewardText.text = "ไม่มีใครตอบถูกเลย!";
        }

        int[] newTurnOrder = new int[totalPlayers];
        for (int i = 0; i < totalPlayers; i++)
        {
            newTurnOrder[i] = rankedPlayers[i].playerIndex;
        }

        gameController?.ApplyNewTurnOrder(newTurnOrder);
    }

    private void StartQuizInternal(int questionIndex)
    {
        if (questionDatabase == null || questionDatabase.Count == 0)
        {
            return;
        }

        int safeQuestionIndex = Mathf.Clamp(questionIndex, 0, questionDatabase.Count - 1);
        currentQuestion = questionDatabase[safeQuestionIndex];
        currentAnswers.Clear();
        currentTime = timeLimit;
        isQuizActive = true;
        isWaitingForOnlineResults = false;

        if (questionText != null) questionText.text = currentQuestion.questionText;

        for (int i = 0; i < answerButtons.Length; i++)
        {
            if (i < answerChoiceTexts.Length && answerChoiceTexts[i] != null && i < currentQuestion.choices.Length)
            {
                answerChoiceTexts[i].text = currentQuestion.choices[i];
            }

            if (answerButtons[i] != null)
            {
                answerButtons[i].interactable = true;
            }
        }

        if (timerText != null) timerText.text = Mathf.CeilToInt(currentTime).ToString();
        if (rewardText != null) rewardText.text = "รางวัลอันดับ 1: เหรียญดำพิเศษ 1 เหรียญ";
        if (gameController != null) gameController.SetGameplayInputLocked(true);
        if (quizPanel != null) quizPanel.SetActive(true);

        Debug.Log($"<color=white>เปิดควิซ: {currentQuestion.questionText}</color>");
    }

    private void PrepareClientWaitingForQuizStart()
    {
        isQuizActive = false;
        isWaitingForOnlineResults = false;
        if (gameController != null) gameController.SetGameplayInputLocked(false);
        if (quizPanel != null) quizPanel.SetActive(false);
        if (timerText != null) timerText.text = string.Empty;
    }

    private void BeginWaitingForOnlineResults()
    {
        isWaitingForOnlineResults = true;
        DisableAnswerButtons();
        if (timerText != null) timerText.text = "WAIT";
    }

    private void HandleOnlineQuizTimeout()
    {
        if (IsOnlineQuizHost())
        {
            ForceEndQuiz();
            return;
        }

        BeginWaitingForOnlineResults();
    }

    private void HandleRemoteQuizStarted(int questionIndex)
    {
        if (!IsOnlineQuizMode())
        {
            return;
        }

        StartQuizInternal(questionIndex);
    }

    private void HandleRemoteQuizAnswer(FusionManager.QuizAnswerSnapshot answerSnapshot)
    {
        if (!IsOnlineQuizHost() || !isQuizActive)
        {
            return;
        }

        UpsertAnswer(answerSnapshot.PlayerIndex, answerSnapshot.IsCorrect, answerSnapshot.TimeTaken);
        if (HaveAllPlayersAnswered())
        {
            ForceEndQuiz();
        }
    }

    private void HandleRemoteQuizResults(List<FusionManager.QuizAnswerSnapshot> answerSnapshots, List<int> rewardGemIndices)
    {
        if (!IsOnlineQuizMode())
        {
            return;
        }

        isQuizActive = false;
        isWaitingForOnlineResults = false;
        if (gameController != null) gameController.SetGameplayInputLocked(false);
        if (quizPanel != null) quizPanel.SetActive(false);

        List<PlayerAnswer> syncedAnswers = answerSnapshots
            .Select(answerSnapshot => new PlayerAnswer
            {
                playerIndex = answerSnapshot.PlayerIndex,
                isCorrect = answerSnapshot.IsCorrect,
                timeTaken = answerSnapshot.TimeTaken
            })
            .ToList();

        ProcessQuizResults(syncedAnswers, rewardGemIndices, applyRewardsToState: false);
    }

    private void UpsertAnswer(int playerIndex, bool isCorrect, float timeTaken)
    {
        PlayerAnswer existingAnswer = currentAnswers.FirstOrDefault(answer => answer.playerIndex == playerIndex);
        if (existingAnswer != null)
        {
            existingAnswer.isCorrect = isCorrect;
            existingAnswer.timeTaken = timeTaken;
            return;
        }

        currentAnswers.Add(new PlayerAnswer
        {
            playerIndex = playerIndex,
            isCorrect = isCorrect,
            timeTaken = timeTaken
        });
    }

    private bool HaveAllPlayersAnswered()
    {
        return currentAnswers
            .Select(answer => answer.playerIndex)
            .Distinct()
            .Count() >= GetTotalPlayersForQuiz();
    }

    private List<PlayerAnswer> BuildRankedPlayers(List<PlayerAnswer> answers, int totalPlayers)
    {
        List<PlayerAnswer> normalizedAnswers = (answers ?? new List<PlayerAnswer>())
            .Where(answer => answer != null)
            .GroupBy(answer => answer.playerIndex)
            .Select(group => group
                .OrderByDescending(answer => answer.isCorrect)
                .ThenBy(answer => answer.timeTaken)
                .First())
            .ToList();

        for (int i = 0; i < totalPlayers; i++)
        {
            bool alreadyAnswered = normalizedAnswers.Any(answer => answer.playerIndex == i);
            if (alreadyAnswered)
            {
                continue;
            }

            normalizedAnswers.Add(new PlayerAnswer
            {
                playerIndex = i,
                isCorrect = false,
                timeTaken = timeLimit + 999f
            });
        }

        return normalizedAnswers
            .OrderByDescending(answer => answer.isCorrect)
            .ThenBy(answer => answer.timeTaken)
            .ToList();
    }

    private List<int> DetermineRewardGemIndices(List<PlayerAnswer> answers)
    {
        int totalPlayers = GetTotalPlayersForQuiz();
        if (gameController == null || totalPlayers <= 0)
        {
            return new List<int>();
        }

        List<PlayerAnswer> rankedPlayers = BuildRankedPlayers(answers, totalPlayers);
        if (rankedPlayers.Count == 0 || !rankedPlayers[0].isCorrect)
        {
            return new List<int>();
        }

        return new List<int> { 5 };
    }

    private string ApplyRewardGemIndices(int playerIndex, List<int> rewardGemIndices)
    {
        if (gameController == null || playerIndex < 0 || playerIndex >= gameController.players.Length)
        {
            return "ล้มเหลว";
        }

        PlayerUI winnerUI = gameController.players[playerIndex];
        Dictionary<string, int> receivedGems = new Dictionary<string, int>();

        foreach (int gemIndex in rewardGemIndices ?? new List<int>())
        {
            if (gemIndex == 5)
            {
                winnerUI.AddQuizBlackCoin();

                const string blackCoinName = "เหรียญดำ";
                if (receivedGems.ContainsKey(blackCoinName))
                {
                    receivedGems[blackCoinName]++;
                }
                else
                {
                    receivedGems[blackCoinName] = 1;
                }

                continue;
            }

            if (gemIndex < 0 || gemIndex >= 5 || gameController.bankCoins[gemIndex] <= 0)
            {
                continue;
            }

            gameController.bankCoins[gemIndex]--;
            winnerUI.coins[gemIndex]++;

            string gemName = GetGemName(gemIndex);
            if (receivedGems.ContainsKey(gemName))
            {
                receivedGems[gemName]++;
            }
            else
            {
                receivedGems[gemName] = 1;
            }
        }

        winnerUI.UpdateUI();
        gameController.UpdateBankUI();

        if (receivedGems.Count == 0)
        {
            return "ไม่ได้รับไอเทมเพิ่ม";
        }

        List<string> parts = new List<string>();
        foreach (KeyValuePair<string, int> pair in receivedGems)
        {
            parts.Add($"{pair.Key} {pair.Value}");
        }

        return "ได้รับ " + string.Join(" / ", parts);
    }

    private string DescribeRewardGemIndices(List<int> rewardGemIndices)
    {
        Dictionary<string, int> receivedGems = new Dictionary<string, int>();

        foreach (int gemIndex in rewardGemIndices ?? new List<int>())
        {
            string gemName = GetGemName(gemIndex);
            if (receivedGems.ContainsKey(gemName))
            {
                receivedGems[gemName]++;
            }
            else
            {
                receivedGems[gemName] = 1;
            }
        }

        if (receivedGems.Count == 0)
        {
            return "ไม่ได้รับไอเทมเพิ่ม";
        }

        List<string> parts = new List<string>();
        foreach (KeyValuePair<string, int> pair in receivedGems)
        {
            parts.Add($"{pair.Key} {pair.Value}");
        }

        return "ได้รับ " + string.Join(" / ", parts);
    }

    private void DisableAnswerButtons()
    {
        foreach (Button answerButton in answerButtons)
        {
            if (answerButton != null)
            {
                answerButton.interactable = false;
            }
        }
    }

    private int GetTotalPlayersForQuiz()
    {
        if (gameController != null)
        {
            return gameController.ActivePlayerCount;
        }

        return 4;
    }

    private string GetPlayerName(int playerIndex)
    {
        if (gameController == null || playerIndex < 0 || playerIndex >= gameController.players.Length)
        {
            return "Player " + (playerIndex + 1);
        }

        string playerName = gameController.players[playerIndex].nameText != null
            ? gameController.players[playerIndex].nameText.text
            : string.Empty;

        return string.IsNullOrWhiteSpace(playerName) ? "Player " + (playerIndex + 1) : playerName;
    }

    private bool IsOnlineQuizMode()
    {
        return gameController != null && gameController.IsOnlineMatchMode && FusionManager.Instance != null;
    }

    private bool IsOnlineQuizHost()
    {
        return IsOnlineQuizMode() && FusionManager.Instance.IsMasterClient;
    }

    private void SubscribeNetworkEvents()
    {
        if (FusionManager.Instance == null)
        {
            return;
        }

        FusionManager.Instance.QuizStartedReceived -= HandleRemoteQuizStarted;
        FusionManager.Instance.QuizAnswerReceived -= HandleRemoteQuizAnswer;
        FusionManager.Instance.QuizResultsReceived -= HandleRemoteQuizResults;

        FusionManager.Instance.QuizStartedReceived += HandleRemoteQuizStarted;
        FusionManager.Instance.QuizAnswerReceived += HandleRemoteQuizAnswer;
        FusionManager.Instance.QuizResultsReceived += HandleRemoteQuizResults;
    }

    private void UnsubscribeNetworkEvents()
    {
        if (FusionManager.Instance == null)
        {
            return;
        }

        FusionManager.Instance.QuizStartedReceived -= HandleRemoteQuizStarted;
        FusionManager.Instance.QuizAnswerReceived -= HandleRemoteQuizAnswer;
        FusionManager.Instance.QuizResultsReceived -= HandleRemoteQuizResults;
    }

    private string GetGemName(int index)
    {
        switch (index)
        {
            case 0: return "CPU";
            case 1: return "RAM";
            case 2: return "Network";
            case 3: return "Storage";
            case 4: return "Security";
            case 5: return "เหรียญดำ";
            default: return "Item";
        }
    }
}
