using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Networking;

public class QuizManager : MonoBehaviour
{
    public static QuizManager Instance { get; private set; }

    // --- JSON Data Classes ---
    [System.Serializable]
    private class QuizDatabaseJson
    {
        public string version;
        public int totalQuestions;
        public QuizEntryJson[] questions;
    }

    [System.Serializable]
    private class QuizEntryJson
    {
        public string id;
        public string category;
        public string difficulty;
        public string question;
        public string[] choices;
        public int correctIndex;
    }

    [System.Serializable]
    public class QuizQuestion
    {
        public string id;
        public string category;
        public string difficulty;
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

    // ─── Supabase Quiz Patch Config ───────────────────────────────────────
    // URL และ Key จะถูกกรอกอัตโนมัติ — ไม่ต้องแก้ใน Inspector
    private const string SUPABASE_URL     = "https://uwspzhwvjpkcjpoqgkhp.supabase.co";
    private const string SUPABASE_ANON    = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InV3c3B6aHd2anBrY2pwb3Fna2hwIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzQ4MzUwMzYsImV4cCI6MjA5MDQxMTAzNn0.hgTN21pBcTD2meqXxKnydit0U7inI3OpMOAFVy9NtEE";
    private const string CACHE_KEY        = "quiz_cache_v1";
    private const int    CACHE_TTL_HOURS  = 24;  // โหลดใหม่จาก Supabase ทุก 24 ชม.

    [Header("---- คลังคำถาม (Quiz Database) ----")]
    public List<QuizQuestion> questionDatabase;
    private QuizQuestion currentQuestion;

    // --- Anti-Repeat System ---
    private HashSet<string> usedQuestionIds = new HashSet<string>();

    [Header("---- UI หน้าต่างคำถาม ----")]
    public GameObject quizPanel;
    public TextMeshProUGUI questionText;
    public TextMeshProUGUI timerText;
    [Tooltip("ใส่ Image (Image Type = Filled) สำหรับแสดงหลอดเวลา")]
    public Image timeBarFill;
    public Button[] answerButtons;
    public TextMeshProUGUI[] answerChoiceTexts;
    public TextMeshProUGUI rewardText;
    public ResultScreenUI resultScreen;

    [Header("---- ตั้งค่าเวลา ----")]
    public float timeLimit = 10f;
    private float currentTime;
    private bool isQuizActive;
    private bool isWaitingForOnlineResults;
    private int lastSecondTicked = -1;

    [Header("---- ข้อความตอบกลับ (Quiz Feedback) ----")]
    public string[] correctFeedbacks = {
        "เพอร์เฟกต์! คำตอบถูกต้อง! รับรางวัลพิเศษไปเลย!",
        "เฉียบขาด! คุณคือผู้ชนะในรอบนี้!",
        "ถูกต้อง! รับโบนัสทรัพยากรไปเลย!"
    };
    public string[] incorrectFeedbacks = {
        "น่าเสียดาย! พลาดไปนิดเดียวจริงๆ",
        "ผิดคาด! ไม่เป็นไรนะ รอบหน้าเอาใหม่!"
    };
    
    // ผู้เล่นสามารถนำ Text ไปแปะในหน้า ResultScreen หรือหน้าไหนก็ได้
    public TextMeshProUGUI quizFeedbackText; 

    [Header("---- Audio Settings ----")]
    public AudioSource audioSource;
    public AudioClip correctSfx;
    public AudioClip wrongSfx;
    [Tooltip("เสียงจับเวลา/นาฬิกานับถอยหลัง")]
    public AudioClip timerSfx;

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

        // โหลดคำถาม: ลอง Supabase ก่อน → fallback → local JSON
        StartCoroutine(LoadQuizDatabaseHybrid());
    }

    // ─── JSON Classes สำหรับ Supabase RPC response ──────────────────────
    [System.Serializable]
    private class SupabaseQuestion
    {
        public long   id;
        public long   patch_id;
        public string patch_name;
        public string external_id;
        public string category;
        public string difficulty;
        public string question;
        public string[] choices;
        public int    correct_index;
    }

    [System.Serializable]
    private class SupabaseQuestionArray { public SupabaseQuestion[] questions; }

    // ─── Hybrid Loader ───────────────────────────────────────────────────
    private IEnumerator LoadQuizDatabaseHybrid()
    {
        // ขั้น 1: ลองโหลดจาก Supabase ถ้ามีเน็ต
        bool supabaseOk = false;
        yield return StartCoroutine(FetchFromSupabase(result => supabaseOk = result));

        if (!supabaseOk)
        {
            Debug.LogWarning("[Quiz] Supabase unavailable — trying PlayerPrefs cache...");
            if (!LoadFromCache())
            {
                Debug.LogWarning("[Quiz] Cache miss — falling back to local JSON.");
                LoadQuizDatabaseFromJson();
            }
        }

        if (questionDatabase == null || questionDatabase.Count == 0)
            Debug.LogError("[Quiz] ไม่มีคำถามในคลัง! ตรวจสอบ Supabase patch หรือ quiz_database.json");
        else
            Debug.Log($"<color=cyan>[Quiz] Ready: {questionDatabase.Count} questions loaded.</color>");
    }

    // ─── Fetch จาก Supabase ─────────────────────────────────────────────
    private IEnumerator FetchFromSupabase(System.Action<bool> onDone)
    {
        string url = SUPABASE_URL + "/rest/v1/rpc/get_active_questions";
        using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes("{}");
            req.uploadHandler   = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type",  "application/json");
            req.SetRequestHeader("apikey",         SUPABASE_ANON);
            req.SetRequestHeader("Authorization",  "Bearer " + SUPABASE_ANON);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[Quiz] Supabase fetch failed: {req.error}");
                onDone(false);
                yield break;
            }

            string json = req.downloadHandler.text;
            if (string.IsNullOrEmpty(json) || json == "[]" || json == "null")
            {
                Debug.LogWarning("[Quiz] Supabase returned empty question list.");
                onDone(false);
                yield break;
            }

            // Supabase RPC คืน JSON array ตรง ๆ — wrap เพื่อ FromJson
            string wrapped = "{\"questions\":" + json + "}";
            SupabaseQuestionArray parsed = JsonUtility.FromJson<SupabaseQuestionArray>(wrapped);

            if (parsed == null || parsed.questions == null || parsed.questions.Length == 0)
            {
                Debug.LogWarning("[Quiz] Supabase response parse failed.");
                onDone(false);
                yield break;
            }

            questionDatabase = new List<QuizQuestion>();
            foreach (SupabaseQuestion sq in parsed.questions)
            {
                string[] choicesArr = sq.choices;
                if (choicesArr == null || choicesArr.Length < 2) continue;

                questionDatabase.Add(new QuizQuestion
                {
                    id                 = !string.IsNullOrEmpty(sq.external_id) ? sq.external_id : sq.id.ToString(),
                    category           = sq.category,
                    difficulty         = sq.difficulty,
                    questionText       = sq.question,
                    choices            = choicesArr,
                    correctChoiceIndex = sq.correct_index
                });
            }

            // บันทึก cache
            PlayerPrefs.SetString(CACHE_KEY,          json);
            PlayerPrefs.SetString(CACHE_KEY + "_ts",  System.DateTime.UtcNow.ToString("o"));
            PlayerPrefs.Save();

            Debug.Log($"<color=lime>[Quiz] Supabase: loaded {questionDatabase.Count} questions & cached.</color>");
            onDone(true);
        }
    }

    // ─── โหลดจาก PlayerPrefs Cache ──────────────────────────────────────
    private bool LoadFromCache()
    {
        string json = PlayerPrefs.GetString(CACHE_KEY, "");
        string ts   = PlayerPrefs.GetString(CACHE_KEY + "_ts", "");
        if (string.IsNullOrEmpty(json)) return false;

        // ตรวจ TTL
        if (!string.IsNullOrEmpty(ts) &&
            System.DateTime.TryParse(ts, null, System.Globalization.DateTimeStyles.RoundtripKind, out System.DateTime saved))
        {
            if ((System.DateTime.UtcNow - saved).TotalHours > CACHE_TTL_HOURS)
            {
                Debug.LogWarning("[Quiz] Cache expired — will use local JSON and refresh next online session.");
                return false;
            }
        }

        string wrapped = "{\"questions\":" + json + "}";
        SupabaseQuestionArray parsed = JsonUtility.FromJson<SupabaseQuestionArray>(wrapped);
        if (parsed == null || parsed.questions == null || parsed.questions.Length == 0) return false;

        questionDatabase = new List<QuizQuestion>();
        foreach (SupabaseQuestion sq in parsed.questions)
        {
            string[] choicesArr = sq.choices;
            if (choicesArr == null || choicesArr.Length < 2) continue;

            questionDatabase.Add(new QuizQuestion
            {
                id                 = !string.IsNullOrEmpty(sq.external_id) ? sq.external_id : sq.id.ToString(),
                category           = sq.category,
                difficulty         = sq.difficulty,
                questionText       = sq.question,
                choices            = choicesArr,
                correctChoiceIndex = sq.correct_index
            });
        }

        Debug.Log($"<color=yellow>[Quiz] Loaded {questionDatabase.Count} questions from cache (offline).</color>");
        return questionDatabase.Count > 0;
    }

    // ─── Local JSON Fallback (เดิม — ไม่แตะ) ────────────────────────────
    private void LoadQuizDatabaseFromJson()
    {
        TextAsset jsonFile = Resources.Load<TextAsset>("quiz_database");
        if (jsonFile == null)
        {
            Debug.LogWarning("[Quiz] quiz_database.json not found in Resources. Using Inspector data if available.");
            return;
        }

        QuizDatabaseJson db = JsonUtility.FromJson<QuizDatabaseJson>(jsonFile.text);
        if (db == null || db.questions == null || db.questions.Length == 0)
        {
            Debug.LogWarning("[Quiz] quiz_database.json is empty or invalid.");
            return;
        }

        questionDatabase = new List<QuizQuestion>();
        foreach (QuizEntryJson entry in db.questions)
        {
            questionDatabase.Add(new QuizQuestion
            {
                id                 = entry.id,
                category           = entry.category,
                difficulty         = entry.difficulty,
                questionText       = entry.question,
                choices            = entry.choices,
                correctChoiceIndex = entry.correctIndex
            });
        }

        Debug.Log($"<color=cyan>[Quiz] Loaded {questionDatabase.Count} questions from local JSON.</color>");
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
        if (timeBarFill != null) timeBarFill.fillAmount = Mathf.Clamp01(currentTime / timeLimit);

        int currentSecond = Mathf.CeilToInt(currentTime);
        if (currentSecond != lastSecondTicked && currentSecond >= 0)
        {
            lastSecondTicked = currentSecond;
            PlayTimerSound();
        }

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
                int questionIndex = GetNextQuestionIndex();
                StartQuizInternal(questionIndex);
                FusionManager.Instance.SendQuizStart(questionIndex);
            }
            else
            {
                // สำหรับ Client: ไม่ต้องปิด Panel ทิ้ง (เพราะเดี๋ยว Host ส่งมา)
                // แค่ล้างสถานะรอผลลัพธ์เก่าออก
                isWaitingForOnlineResults = false;
                
                // ถ้ามีข้อมูลที่ Host เคยส่งมาค้างอยู่ใน Buffer ให้เริ่มทันที
                if (FusionManager.Instance.TryConsumePendingQuizStart(out int bufferedQuestionIndex))
                {
                    StartQuizInternal(bufferedQuestionIndex);
                }
                else
                {
                    // ถ้ายังไม่มีข้อมูล ให้รอ และอาจจะแสดงข้อความ Loading...
                    if (quizPanel != null) quizPanel.SetActive(true);
                    if (questionText != null) questionText.text = "รอ Host เลือกคำถาม...";
                }
            }

            return;
        }

        StartQuizInternal(GetNextQuestionIndex());
    }

    private int GetNextQuestionIndex()
    {
        // Reset if all questions have been used
        if (usedQuestionIds.Count >= questionDatabase.Count)
        {
            usedQuestionIds.Clear();
            Debug.Log("<color=cyan>[Quiz] All questions used. Resetting pool.</color>");
        }

        // Build list of available indices
        List<int> available = new List<int>();
        for (int i = 0; i < questionDatabase.Count; i++)
        {
            string qId = questionDatabase[i].id ?? i.ToString();
            if (!usedQuestionIds.Contains(qId))
            {
                available.Add(i);
            }
        }

        int chosen = available[Random.Range(0, available.Count)];
        string chosenId = questionDatabase[chosen].id ?? chosen.ToString();
        usedQuestionIds.Add(chosenId);
        return chosen;
    }

    public void SubmitAnswer(int choiceIndex)
    {
        if (!isQuizActive || isWaitingForOnlineResults || currentQuestion == null)
        {
            return;
        }

        bool isCorrect = (choiceIndex == currentQuestion.correctChoiceIndex);
        float timeTaken = timeLimit - currentTime;

        // บันทึกคำตอบของเรา
        currentAnswers.Add(new PlayerAnswer
        {
            playerIndex = gameController != null ? gameController.LocalPlayerSeatIndex : 0,
            isCorrect = isCorrect,
            timeTaken = timeTaken
        });

        if (audioSource != null)
        {
            audioSource.PlayOneShot(isCorrect ? correctSfx : wrongSfx);
        }

        if (IsOnlineQuizMode())
        {
            // ส่งคำตอบไปยัง Network
            FusionManager.Instance.SendQuizAnswer(gameController != null ? gameController.LocalPlayerSeatIndex : 0, isCorrect, timeTaken);
            BeginWaitingForOnlineResults();
        }
        else
        {
            // โหมดคนเดียว ประมวลผลทันที
            isQuizActive = false;
            ProcessQuizResults(currentAnswers);
        }
    }

    private void PlayTimerSound()
    {
        if (audioSource != null && timerSfx != null)
        {
            audioSource.PlayOneShot(timerSfx);
        }
        else
        {
            StartupCity.Audio.AudioManager.Instance?.PlayTimerTick();
        }
    }

    // [DEPRECATED] เก็บไว้กันพัง แต่ให้ใช้ SubmitAnswer แทน
    public void OnClickAnswer(int choiceIndex) => SubmitAnswer(choiceIndex);


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

        // --- [NEW] Logic สำหรับโหมดบอท: ถ้าเราตอบผิด ให้สุ่มอันดับ 2-4 (ห้ามได้ที่ 1) ---
        if (!IsOnlineQuizMode())
        {
            int myIdx = gameController != null ? gameController.LocalPlayerSeatIndex : 0;
            PlayerAnswer myAns = rankedPlayers.FirstOrDefault(a => a.playerIndex == myIdx);
            
            if (myAns != null && !myAns.isCorrect)
            {
                // เราตอบผิดในโหมดบอท! สุ่มตำแหน่งใหม่ให้อยู่ระหว่าง 2-4 (Index 1 เป็นต้นไป)
                rankedPlayers.Remove(myAns);
                // สุ่ม index ตั้งแต่ 1 ถึง (จำนวนผู้เล่น - 1)
                int randomTargetIndex = Random.Range(1, totalPlayers); 
                rankedPlayers.Insert(Mathf.Min(randomTargetIndex, rankedPlayers.Count), myAns);
                
                Debug.Log($"[Quiz] Bot Mode & Incorrect: Player randomized to rank {rankedPlayers.IndexOf(myAns) + 1}");
            }
        }

        if (rankedPlayers == null || rankedPlayers.Count == 0)
        {
            Debug.LogWarning("[Quiz] BuildRankedPlayers returned empty list. Aborting.");
            return;
        }

        Debug.Log("\n<color=yellow>=== สรุปผลการตอบคำถาม ===</color>");
        for (int i = 0; i < rankedPlayers.Count; i++)
        {
            PlayerAnswer answer = rankedPlayers[i];
            Debug.Log($"อันดับ {i + 1}: ผู้เล่น {answer.playerIndex + 1} ตอบถูก: {answer.isCorrect} เวลา: {answer.timeTaken:F2} s");
        }

        PlayerAnswer winner = rankedPlayers[0];
        bool hasWinner = winner.isCorrect;
        string feedbackMsg = "";

        if (hasWinner)
        {
            List<int> rewardGemIndices = forcedRewardGemIndices;
            
            // ถ้าไม่มีการระบุรางวัลมา ให้สุ่มให้เอง 1-2 อย่าง
            if (rewardGemIndices == null || rewardGemIndices.Count == 0)
            {
                rewardGemIndices = new List<int>();
                if (gameController != null)
                {
                    List<int> availableIndices = new List<int>();
                    for (int i = 0; i < 5; i++)
                    {
                        if (gameController.bankCoins[i] > 0) availableIndices.Add(i);
                    }

                    if (availableIndices.Count > 0)
                    {
                        int rewardCount = Random.Range(1, 3); // สุ่ม 1-2 อย่าง
                        for (int i = 0; i < rewardCount && availableIndices.Count > 0; i++)
                        {
                            int randomIndex = Random.Range(0, availableIndices.Count);
                            rewardGemIndices.Add(availableIndices[randomIndex]);
                            // ไม่ต้องเอาออกจาก availableIndices เพื่อให้มีโอกาสได้ซ้ำ
                        }
                    }
                }
            }

            string rewardMessage = applyRewardsToState
                ? ApplyRewardGemIndices(winner.playerIndex, rewardGemIndices)
                : DescribeRewardGemIndices(rewardGemIndices);
            
            string playerName = GetPlayerName(winner.playerIndex);
            
            // เช็คว่าได้ของจริงไหม (ดูจากข้อความที่ส่งกลับมา)
            if (rewardMessage.Contains("รับรางวัลพิเศษ"))
            {
                feedbackMsg = $"ยินดีด้วย {playerName}! {rewardMessage}";
            }
            else
            {
                // กรณีตอบถูกแต่ไม่ได้ของ (เช่น คลังหมด)
                feedbackMsg = $"{playerName} ตอบถูก! แต่น่าเสียดายที่ทรัพยากรในคลังหมดแล้ว";
            }
            
            if (rewardText != null) rewardText.text = feedbackMsg;
        }
        else
        {
            if (incorrectFeedbacks != null && incorrectFeedbacks.Length > 0)
                feedbackMsg = incorrectFeedbacks[Random.Range(0, incorrectFeedbacks.Length)];
            
            if (rewardText != null) rewardText.text = "น่าเสียดาย! ไว้ลองใหม่ข้อหน้านะ";
        }

        if (quizFeedbackText != null)
        {
            quizFeedbackText.text = feedbackMsg;
            quizFeedbackText.color = hasWinner ? Color.green : Color.red;
        }

        int[] newTurnOrder = new int[totalPlayers];
        List<string> turnOrderNames = new List<string>();
        for (int i = 0; i < totalPlayers; i++)
        {
            newTurnOrder[i] = rankedPlayers[i].playerIndex;
            string pName = GetPlayerName(rankedPlayers[i].playerIndex);
            turnOrderNames.Add($"{pName} (เวลา: {rankedPlayers[i].timeTaken:F2}วิ)");
        }

        // --- ตรวจสอบว่า Local Player (ตัวเรา) ตอบถูกไหม ---
        int myIndex = gameController != null ? gameController.LocalPlayerSeatIndex : 0;
        PlayerAnswer myAnswer = rankedPlayers.FirstOrDefault(a => a.playerIndex == myIndex);
        bool iAmCorrect = myAnswer != null && myAnswer.isCorrect;

        string title = iAmCorrect ? "ยินดีด้วย! คุณตอบถูก (CORRECT)" : "เสียใจด้วย! คุณตอบผิด (WRONG)";
        
        // แสดงหน้าสรุปผล (Result Screen) พร้อมลำดับเทิร์นใหม่
        if (resultScreen != null)
        {
            if (gameController != null)
            {
                resultScreen.onClosed = gameController.OnResultScreenClosed;
            }

            // ส่งค่า iAmCorrect ไปที่ playFireworks เพื่อให้หน้าจอเปลี่ยนสี เขียว/แดง
            resultScreen.ShowResults(title, turnOrderNames, false, feedbackMsg, iAmCorrect);
            
            if (audioSource != null)
            {
                if (iAmCorrect && correctSfx != null) audioSource.PlayOneShot(correctSfx);
                else if (!iAmCorrect && wrongSfx != null) audioSource.PlayOneShot(wrongSfx);
            }

            if (gameController != null)
            {
                gameController.SetPendingQuizTurnOrder(newTurnOrder);
                gameController.SetWaitingForContinueAfterResult(true);
            }

            if (quizPanel != null) quizPanel.SetActive(false);
        }
        else
        {
            if (gameController != null)
            {
                gameController.ApplyNewTurnOrder(newTurnOrder);
            }
            if (quizPanel != null) quizPanel.SetActive(false);
        }
    }

    private void StartQuizInternal(int questionIndex)
    {
        if (questionDatabase == null || questionDatabase.Count == 0)
        {
            Debug.LogError("[QuizManager] Database ว่างเปล่า!");
            return;
        }

        int safeQuestionIndex = Mathf.Clamp(questionIndex, 0, questionDatabase.Count - 1);
        
        // --- ระบบ Shuffle ช้อยส์ ---
        QuizQuestion original = questionDatabase[safeQuestionIndex];
        currentQuestion = new QuizQuestion
        {
            id = original.id,
            category = original.category,
            difficulty = original.difficulty,
            questionText = original.questionText,
            choices = (string[])original.choices.Clone()
        };

        // จำคำตอบที่ถูกไว้ก่อนสลับ
        string correctValue = original.choices[original.correctChoiceIndex];

        // สุ่มสลับ (Fisher-Yates)
        for (int i = currentQuestion.choices.Length - 1; i > 0; i--)
        {
            int r = Random.Range(0, i + 1);
            string tmp = currentQuestion.choices[i];
            currentQuestion.choices[i] = currentQuestion.choices[r];
            currentQuestion.choices[r] = tmp;
        }

        // หา Index ใหม่ของคำตอบที่ถูก
        currentQuestion.correctChoiceIndex = System.Array.IndexOf(currentQuestion.choices, correctValue);

        currentAnswers.Clear();
        currentTime = timeLimit;
        lastSecondTicked = Mathf.CeilToInt(timeLimit);
        isQuizActive = true;
        isWaitingForOnlineResults = false;

        if (timeBarFill != null) timeBarFill.fillAmount = 1f;

        // UI Setup
        if (questionText != null) questionText.text = currentQuestion.questionText;

        if (answerButtons == null || answerButtons.Length == 0)
        {
            Debug.LogError("[QuizManager] ไม่พบ answerButtons ใน Inspector!");
            return;
        }

        for (int i = 0; i < answerButtons.Length; i++)
        {
            if (answerButtons[i] == null) continue;

            // พยายามหา Text ถ้าไม่ได้ลากใส่มา
            TextMeshProUGUI btnText = null;
            if (answerChoiceTexts != null && i < answerChoiceTexts.Length)
            {
                btnText = answerChoiceTexts[i];
            }
            if (btnText == null)
            {
                btnText = answerButtons[i].GetComponentInChildren<TextMeshProUGUI>(true);
            }

            if (btnText != null && i < currentQuestion.choices.Length)
            {
                btnText.text = currentQuestion.choices[i];
            }

            answerButtons[i].interactable = true;
            answerButtons[i].onClick.RemoveAllListeners();
            int index = i;
            answerButtons[i].onClick.AddListener(() => SubmitAnswer(index));
        }

        if (timerText != null) timerText.text = Mathf.CeilToInt(currentTime).ToString();
        if (rewardText != null) rewardText.text = "รางวัลอันดับ 1: รับโบนัสทรัพยากร!";
        
        if (gameController != null) gameController.SetGameplayInputLocked(true);
        if (quizPanel != null) quizPanel.SetActive(true);

        Debug.Log($"<color=white>เปิดควิซ (Shuffle แล้ว): {currentQuestion.questionText}</color>");
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
        if (winnerUI == null)
        {
            Debug.LogWarning($"[Quiz] ApplyRewardGemIndices: players[{playerIndex}] is null, skipping reward.");
            return "ไม่สามารถมอบรางวัลได้";
        }
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

        return "รับรางวัลพิเศษ " + string.Join(" / ", parts) + " เหรียญ";
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

        return "รับรางวัลพิเศษ " + string.Join(" / ", parts) + " เหรียญ";
    }

    private void DisableAnswerButtons()
    {
        if (answerButtons == null) return;
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

        // เช็คว่า PlayerUI slot นี้ไม่เป็น null (สำคัญมาก! สล็อตบอตอาจเป็น null ได้)
        PlayerUI player = gameController.players[playerIndex];
        if (player == null)
        {
            return "Player " + (playerIndex + 1);
        }

        string playerName = player.nameText != null
            ? player.nameText.text
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
