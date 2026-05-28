using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using StartupCity.Audio;

public class DailyQuizManager : MonoBehaviour
{
    [Header("---- Database ----")]
    [SerializeField] private TextAsset quizJson;
    
    [Header("---- UI Panels ----")]
    [SerializeField] private GameObject quizPanel;
    [SerializeField] private GameObject resultPanel;
    [SerializeField] private GameObject alreadyPlayedPanel;
    [SerializeField] private CanvasGroup mainCanvasGroup;

    [Header("---- Question UI ----")]
    [SerializeField] private TextMeshProUGUI questionText;
    [SerializeField] private TextMeshProUGUI categoryText;
    [SerializeField] private Button[] answerButtons;
    [SerializeField] private TextMeshProUGUI[] answerTexts;

    [Header("---- Timer Visuals ----")]
    [SerializeField] private Image timerBar;
    [SerializeField] private Gradient timerGradient;
    [SerializeField] private TextMeshProUGUI timerText;

    [Header("---- Effects ----")]
    [SerializeField] private ParticleSystem confettiParticles;
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioClip correctSound;
    [SerializeField] private AudioClip wrongSound;

    [Header("---- Result UI ----")]
    [SerializeField] private TextMeshProUGUI resultTitleText;
    [SerializeField] private TextMeshProUGUI resultMessageText;
    [SerializeField] private TextMeshProUGUI rewardText;
    [SerializeField] private Button[] allBackToMenuButtons; // ใส่ปุ่มย้อนกลับจากทุก Panel ที่นี่

    [Header("---- Settings ----")]
    [SerializeField] private float timeLimit = 20f;
    [SerializeField] private int gemReward = 100;

    [Header("---- Category Colors ----")]
    [SerializeField] private Color cpuColor = Color.red;
    [SerializeField] private Color ramColor = new Color(0.2f, 0.6f, 1f); // Blue
    [SerializeField] private Color networkColor = new Color(0.6f, 0.2f, 1f); // Purple
    [SerializeField] private Color securityColor = Color.yellow;
    [SerializeField] private Color storageColor = new Color(0.8f, 0.8f, 0.8f); // Silver
    [SerializeField] private Color programmingColor = Color.green;
    [SerializeField] private Color defaultColor = Color.white;

    [Header("---- Audio Settings ----")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip correctSfx;
    [SerializeField] private AudioClip wrongSfx;
    [SerializeField] private AudioClip timerSfx;

    private QuizDatabase quizDb;
    private QuizQuestion currentQuestion;
    // [FIX] เก็บ correct index หลัง shuffle แยกออกมา
    // ห้ามแก้ currentQuestion.correctIndex โดยตรงเพราะมันชี้ไปที่ object ต้นฉบับใน quizDb
    private int _shuffledCorrectIndex;
    private float currentTime;
    private bool isQuizActive;
    private bool hasAnswered;
    private int lastSecondTicked = -1;
    private string _currentQuestionId; // เก็บ external_id ของคำถามที่กำลังแสดง เพื่อส่งให้ server บันทึก quiz history

    [Serializable]
    public class QuizQuestion
    {
        public string id;
        public string category;
        public string question;
        public string[] choices;
        public int correctIndex;
    }

    [Serializable]
    private class QuizDatabase
    {
        public QuizQuestion[] questions;
    }

    private void Start()
    {
        // สลับ BGM มาใช้เพลงเกม
        AudioManager.Instance?.PlayGameBGM();
        InitializeUI();
        CheckDailyStatus();
    }

    // ───── ดึงคำถามที่ยังไม่เคยตอบจาก Supabase แล้ว fallback เป็น local random ─────
    private async void LoadQuestionsAndStartAsync()
    {
        if (quizJson == null) quizJson = Resources.Load<TextAsset>("quiz_database");
        if (quizJson != null)
            quizDb = JsonUtility.FromJson<QuizDatabase>(quizJson.text);

        if (quizDb == null || quizDb.questions == null || quizDb.questions.Length == 0)
        {
            Debug.LogError("[DailyQuiz] Question database is empty!");
            return;
        }

        // ขั้น 1: ลองดึง external_id ของข้อที่ยังไม่เคยตอบจาก server
        string unansweredId = await PlayerDataService.FetchUnansweredDailyQuestionIdAsync();

        QuizQuestion picked = null;
        if (!string.IsNullOrEmpty(unansweredId))
        {
            // หาคำถามใน local DB ที่ตรงกับ external_id ที่ server บอกว่ายังไม่ตอบ
            foreach (var q in quizDb.questions)
            {
                if (q.id == unansweredId) { picked = q; break; }
            }
        }

        // ขั้น 2: fallback → สุ่มจาก local DB ทั้งหมด (เช่น offline หรือตอบครบทุกข้อแล้ว)
        if (picked == null)
            picked = quizDb.questions[UnityEngine.Random.Range(0, quizDb.questions.Length)];

        _currentQuestionId = picked.id; // จำ id ไว้ส่งตอนรับรางวัล
        currentQuestion = picked;
        DisplayQuestion(currentQuestion);
        currentTime = timeLimit;
        lastSecondTicked = Mathf.CeilToInt(timeLimit);
        isQuizActive = true;
        hasAnswered = false;
        if (quizPanel != null) quizPanel.SetActive(true);
    }

    private void InitializeUI()
    {
        // --- ระบบ Auto-Find ปุ่ม (ต้องทำก่อนซ่อน Panel) ---
        if (answerButtons == null || answerButtons.Length < 4) answerButtons = new Button[4];
        if (answerTexts == null || answerTexts.Length < 4) answerTexts = new TextMeshProUGUI[4];

        if (quizPanel != null)
        {
            // ค้นหาจากลูกของ quizPanel เพื่อความชัวร์
            Button[] btnsInPanel = quizPanel.GetComponentsInChildren<Button>(true);
            foreach (var btn in btnsInPanel)
            {
                if (btn.name == "ChoiceBtn_0") answerButtons[0] = btn;
                else if (btn.name == "ChoiceBtn_1") answerButtons[1] = btn;
                else if (btn.name == "ChoiceBtn_2") answerButtons[2] = btn;
                else if (btn.name == "ChoiceBtn_3") answerButtons[3] = btn;
            }
        }

        for (int i = 0; i < 4; i++)
        {
            if (answerButtons[i] != null && answerTexts[i] == null)
            {
                answerTexts[i] = answerButtons[i].GetComponentInChildren<TextMeshProUGUI>();
            }
        }
        // ---------------------------------------------

        if (quizPanel != null) quizPanel.SetActive(false);
        if (resultPanel != null) resultPanel.SetActive(false);
        if (alreadyPlayedPanel != null) alreadyPlayedPanel.SetActive(false);
        
        if (mainCanvasGroup != null)
        {
            mainCanvasGroup.alpha = 0;
            StartCoroutine(FadeIn(mainCanvasGroup, 1f));
        }
        
        if (allBackToMenuButtons != null && allBackToMenuButtons.Length > 0)
        {
            foreach (var btn in allBackToMenuButtons)
            {
                if (btn != null)
                {
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(BackToMainMenu);
                }
            }
        }
        else
        {
            Debug.LogWarning("[DailyQuiz] No BackToMenu buttons assigned! Result panel might not be closeable.");
        }
    }

    private void CheckDailyStatus()
    {
        string lastDate = PlayerPrefs.GetString("LastDailyQuizDate", "");
        string today = DateTime.Now.ToString("yyyy-MM-dd");

        if (lastDate == today)
        {
            ShowAlreadyPlayed();
        }
        else
        {
            LoadQuestionsAndStart();
        }
    }

    private void LoadQuestionsAndStart()
    {
        // delegate ไปยัง async version ที่ดึงข้อที่ยังไม่เคยตอบจาก server
        LoadQuestionsAndStartAsync();
    }

    private void DisplayQuestion(QuizQuestion question)
    {
        if (questionText != null) questionText.text = question.question;
        if (categoryText != null) categoryText.text = question.category;
        
        SetCategoryColor(question.category);

        if (answerButtons == null || answerButtons.Length == 0)
        {
            Debug.LogError("[DailyQuiz] Answer Buttons array is empty!");
            return;
        }

        // --- ระบบ Shuffle ช้อยส์สำหรับ Daily Quiz ---
        if (question.choices == null || question.choices.Length == 0)
        {
            Debug.LogError("[DailyQuiz] question.choices is null or empty!");
            return;
        }
        if (question.correctIndex < 0 || question.correctIndex >= question.choices.Length)
        {
            Debug.LogError($"[DailyQuiz] correctIndex {question.correctIndex} is out of bounds (choices: {question.choices.Length})!");
            question.correctIndex = 0;
        }

        string correctValue = question.choices[question.correctIndex];
        string[] shuffledChoices = (string[])question.choices.Clone();
        
        // Fisher-Yates shuffle
        for (int i = shuffledChoices.Length - 1; i > 0; i--)
        {
            int r = UnityEngine.Random.Range(0, i + 1);
            string tmp = shuffledChoices[i];
            shuffledChoices[i] = shuffledChoices[r];
            shuffledChoices[r] = tmp;
        }

        // หา Index ใหม่ที่ถูกต้อง — เก็บไว้ใน field แยก ห้ามแก้ question.correctIndex
        // เพราะ currentQuestion ชี้ไปที่ object ต้นฉบับใน quizDb (reference)
        _shuffledCorrectIndex = System.Array.IndexOf(shuffledChoices, correctValue);
        // ------------------------------------------

        for (int i = 0; i < answerButtons.Length; i++)
        {
            if (answerButtons[i] == null) continue;

            if (i < shuffledChoices.Length)
            {
                int index = i;
                
                // อัปเดต Text (ค้นหา TMP/Text ทุกตัวในปุ่ม)
                TextMeshProUGUI[] allTMPs = answerButtons[i].GetComponentsInChildren<TextMeshProUGUI>(true);
                foreach (var tmp in allTMPs) tmp.text = shuffledChoices[i];

                Text[] allStandardTexts = answerButtons[i].GetComponentsInChildren<Text>(true);
                foreach (var st in allStandardTexts) st.text = shuffledChoices[i];

                answerButtons[i].gameObject.SetActive(true);
                answerButtons[i].onClick.RemoveAllListeners();
                answerButtons[i].onClick.AddListener(() => SubmitDailyAnswer(index));
                answerButtons[i].interactable = true;
            }
            else
            {
                answerButtons[i].gameObject.SetActive(false);
            }
        }
    }

    public void SubmitDailyAnswer(int choiceIndex)
    {
        if (!isQuizActive || hasAnswered) return;

        hasAnswered = true;
        isQuizActive = false;
        StopTimerSound(); // หยุดเสียงนับถอยหลังก่อนเล่นเสียง correct/wrong

        bool isCorrect = (choiceIndex == _shuffledCorrectIndex);
        
        // เล่นเสียงผ่าน AudioManager หลัก (Singleton) ก่อน — ถ้าไม่มีค่อย fallback ไป local
        // [BUGFIX] เดิมเล่นทั้ง 2 ที่ → เสียงซ้อน 2 ครั้ง
        if (AudioManager.Instance != null)
        {
            if (isCorrect) AudioManager.Instance.PlayCorrectAnswer();
            else           AudioManager.Instance.PlayWrongAnswer();
        }
        else if (audioSource != null)
        {
            audioSource.PlayOneShot(isCorrect ? correctSfx : wrongSfx);
        }

        MarkAsPlayed();
        ShowResult(isCorrect);
    }

    // [DEPRECATED] สำหรับรองรับของเดิมใน Inspector (ถ้ามี)
    public void OnAnswerSelected(int index) => SubmitDailyAnswer(index);

    private void Update()
    {
        if (!isQuizActive || hasAnswered) return;

        currentTime -= Time.deltaTime;
        float progress = currentTime / timeLimit;
        
        if (timerText != null) timerText.text = Mathf.CeilToInt(currentTime).ToString();
        if (timerBar != null) 
        {
            timerBar.fillAmount = progress;
            if (timerGradient != null)
            {
                timerBar.color = timerGradient.Evaluate(progress);
            }
        }

        // เล่นเสียงนับถอยหลังจับเวลาวินาทีละครั้ง
        int currentSecond = Mathf.CeilToInt(currentTime);
        if (currentSecond != lastSecondTicked && currentSecond >= 0)
        {
            lastSecondTicked = currentSecond;
            PlayTimerSound();
        }

        if (currentTime <= 0)
        {
            OnTimeOut();
        }
    }


    private void OnTimeOut()
    {
        if (hasAnswered) return;
        hasAnswered = true;
        isQuizActive = false;
        StopTimerSound(); // หยุดเสียง tick ค้าง
        MarkAsPlayed();
        ShowResult(false, "หมดเวลาแล้ว!");
    }

    private void PlayTimerSound()
    {
        if (audioSource != null && timerSfx != null)
        {
            // [BUGFIX] หยุดเสียง tick เดิมก่อนเล่นใหม่ (timerSfx อาจยาวกว่า 1 วินาที → stack กันได้)
            audioSource.Stop();
            audioSource.PlayOneShot(timerSfx);
        }
        else
        {
            AudioManager.Instance?.PlayTimerTick();
        }
    }

    /// <summary>หยุดเสียงนับถอยหลังทันที — เรียกตอนตอบเสร็จ/หมดเวลา เพื่อไม่ให้ tick ค้าง</summary>
    private void StopTimerSound()
    {
        if (audioSource != null) audioSource.Stop();
        AudioManager.Instance?.StopTimerTick();
    }

    private void MarkAsPlayed()
    {
        PlayerPrefs.SetString("LastDailyQuizDate", DateTime.Now.ToString("yyyy-MM-dd"));
        PlayerPrefs.Save();
    }

    private void SetCategoryColor(string category)
    {
        if (categoryText == null) return;

        string cleanCategory = category.Trim().ToLower();
        Color targetColor = defaultColor; 

        switch (cleanCategory)
        {
            case "cpu":
                targetColor = cpuColor;
                break;
            case "ram":
                targetColor = ramColor;
                break;
            case "network":
                targetColor = networkColor;
                break;
            case "security":
                targetColor = securityColor;
                break;
            case "storage":
            case "database":
            case "sql":
                targetColor = storageColor;
                break;
            case "programming":
            case "coding":
                targetColor = programmingColor;
                break;
        }

        // Brute Force Update: เปลี่ยนสีทุก Component Text ที่อยู่ใน Category (รวม Shadow/Outline ถ้ามี)
        var allTmps = categoryText.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
        foreach (var tmp in allTmps) 
        {
            tmp.text = category.ToUpper();
            tmp.color = targetColor;
        }

        var allTexts = categoryText.GetComponentsInChildren<UnityEngine.UI.Text>(true);
        foreach (var t in allTexts) 
        {
            t.text = category.ToUpper();
            t.color = targetColor;
        }

        GameLog.Log($"[DailyQuiz] Category: {category} (Force Updated all Text components to {targetColor})");
    }

    private void ShowResult(bool success, string customMsg = "")
    {
        if (quizPanel != null) quizPanel.SetActive(false);
        if (resultPanel != null) resultPanel.SetActive(true);
        
        if (success)
        {
            // หัวข้อแสดงผลชัดเจน สีเขียว
            resultTitleText.text = "ถูกต้อง! (CORRECT)";
            resultTitleText.color = Color.green;
            
            resultMessageText.text = "เก่งมาก! คุณได้รับรางวัลจากการตอบคำถามประจำวัน";
            resultMessageText.color = Color.white;
            rewardText.text = $"รับรางวัลพิเศษ {gemReward} Gems";
            
            // Trigger Effects (เสียง correct/wrong ถูกเล่นไปแล้วใน SubmitDailyAnswer ผ่าน AudioManager
            // อย่าเล่นซ้ำที่นี่ — เคยทำให้เสียงดัง 2 ครั้งทับกัน)
            if (confettiParticles != null) confettiParticles.Play();

            // อัปเดต local เพื่อโชว์ผลทันที (optimistic)
            if (CurrencyManager.Instance != null)
            {
                CurrencyManager.Instance.AddGems(gemReward);
            }
            else
            {
                int newTotal = PlayerPrefs.GetInt("TotalGems", 0) + gemReward;
                PlayerPrefs.SetInt("TotalGems", newTotal);
                PlayerPrefs.Save();
            }

            // เขียน DB แบบ server-authoritative: server กำหนดจำนวน + กันรับซ้ำ/วัน
            // (ถ้า server ปฏิเสธ เช่นรับไปแล้ววันนี้ ค่าจะถูก reconcile ตอนโหลดโปรไฟล์ครั้งถัดไป)
            // ส่ง question_id ให้ server บันทึกลง quiz_history (daily_quiz_claims.question_id)
            _ = PlayerDataService.GrantQuizRewardAsync(_currentQuestionId);
        }
        else
        {
            // หัวข้อแสดงผลชัดเจน สีแดง
            resultTitleText.text = "ผิด! (WRONG)";
            resultTitleText.color = Color.red;
            
            resultMessageText.text = string.IsNullOrEmpty(customMsg) ? "คำตอบยังไม่ถูกต้อง พรุ่งนี้ลองใหม่นะ!" : customMsg;
            resultMessageText.color = new Color(1f, 0.8f, 0.8f); // สีขาวอมแดงจางๆ
            rewardText.text = "สู้ๆ นะ! ไว้ลองใหม่วันพรุ่งนี้";
            // (เสียง wrong เล่นไปแล้วที่ SubmitDailyAnswer — ไม่เล่นซ้ำ)
        }
    }

    private void ShowAlreadyPlayed()
    {
        if (alreadyPlayedPanel != null) alreadyPlayedPanel.SetActive(true);
    }

    public void BackToMainMenu()
    {
        AudioManager.Instance?.PlayButtonClick();
        // กลับ MainMenu สลับ BGM คืน
        AudioManager.Instance?.PlayMenuBGM();
        StartCoroutine(FadeOutAndLoad("MainMenu 1"));
    }

    private IEnumerator FadeIn(CanvasGroup cg, float duration)
    {
        float t = 0;
        while (t < duration)
        {
            t += Time.deltaTime;
            cg.alpha = t / duration;
            yield return null;
        }
        cg.alpha = 1;
    }

    private IEnumerator FadeOutAndLoad(string sceneName)
    {
        GameLog.Log($"[DailyQuiz] Attempting to load scene: {sceneName}");

        if (mainCanvasGroup != null)
        {
            float t = 1;
            while (t > 0)
            {
                t -= Time.deltaTime * 2f; // Fade out เร็วขึ้นเล็กน้อย
                mainCanvasGroup.alpha = t;
                yield return null;
            }
        }

        // เช็คว่าซีนมีอยู่ใน Build Settings หรือไม่
        bool canLoad = false;
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name == sceneName) { canLoad = true; break; }
        }

        if (canLoad)
        {
            SceneManager.LoadScene(sceneName);
        }
        else
        {
            Debug.LogError($"[DailyQuiz] Scene '{sceneName}' not found in Build Settings! Please add it in File > Build Settings.");
            
            // แผนสำรอง: ถ้าโหลด MainMenu ไม่ได้ ให้ลองโหลดซีนที่ Index 0 (หน้าแรกสุดของเกม)
            if (SceneManager.sceneCountInBuildSettings > 0)
            {
                Debug.LogWarning("[DailyQuiz] Falling back to Scene at Index 0");
                SceneManager.LoadScene(0);
            }
            else
            {
                // ถ้าใน Build Settings ไม่มีอะไรเลย ให้รีเฟรชซีนปัจจุบัน (เป็นทางเลือกสุดท้าย)
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            }
        }
    }
}
