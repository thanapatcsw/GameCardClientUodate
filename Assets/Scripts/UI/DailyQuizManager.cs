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
        if (quizJson == null) quizJson = Resources.Load<TextAsset>("quiz_database");
        
        if (quizJson != null)
        {
            quizDb = JsonUtility.FromJson<QuizDatabase>(quizJson.text);
        }

        if (quizDb == null || quizDb.questions == null || quizDb.questions.Length == 0)
        {
            Debug.LogError("[DailyQuiz] Question database is empty!");
            return;
        }

        // Pick random
        currentQuestion = quizDb.questions[UnityEngine.Random.Range(0, quizDb.questions.Length)];
        
        // Setup UI
        DisplayQuestion(currentQuestion);

        currentTime = timeLimit;
        lastSecondTicked = Mathf.CeilToInt(timeLimit);
        isQuizActive = true;
        hasAnswered = false;
        if (quizPanel != null) quizPanel.SetActive(true);
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
        
        bool isCorrect = (choiceIndex == _shuffledCorrectIndex);
        
        // เล่นเสียงผ่าน AudioManager หลัก (Singleton)
        if (isCorrect) AudioManager.Instance?.PlayCorrectAnswer();
        else           AudioManager.Instance?.PlayWrongAnswer();

        // Fallback: เล่นผ่าน local AudioSource ถ้า Assign ไว้ตรง Inspector
        if (audioSource != null)
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
        MarkAsPlayed();
        ShowResult(false, "หมดเวลาแล้ว!");
    }

    private void PlayTimerSound()
    {
        if (audioSource != null && timerSfx != null)
        {
            audioSource.PlayOneShot(timerSfx);
        }
        else
        {
            AudioManager.Instance?.PlayTimerTick();
        }
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

        Debug.Log($"[DailyQuiz] Category: {category} (Force Updated all Text components to {targetColor})");
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
            
            // Trigger Effects
            if (confettiParticles != null) confettiParticles.Play();
            if (sfxSource != null && correctSound != null) sfxSource.PlayOneShot(correctSound);

            // เล่นเสียงถูกผ่าน AudioManager (ถ้ายังไม่ได้เล่นจาก SubmitDailyAnswer)
            // AudioManager.Instance?.PlayCorrectAnswer();  // ← uncomment ถ้า ShowResult ถูกเรียกโดยตรง

            if (CurrencyManager.Instance != null)
            {
                CurrencyManager.Instance.AddGems(gemReward);
            }
            else
            {
                // Fallback: อัปเดต PlayerPrefs โดยตรงถ้าไม่มี CurrencyManager
                int currentGems = PlayerPrefs.GetInt("TotalGems", 0);
                int newTotal = currentGems + gemReward;
                PlayerPrefs.SetInt("TotalGems", newTotal);
                PlayerPrefs.Save();
                
                // บันทึกลง Database ด้วย (ถ้าล็อกอินอยู่)
                _ = PlayerDataService.SaveCurrencyAsync(newTotal);
                
                Debug.Log($"[DailyQuiz] Added {gemReward} gems (Total: {newTotal}) to PlayerPrefs & Database (Fallback)");
            }
        }
        else
        {
            // หัวข้อแสดงผลชัดเจน สีแดง
            resultTitleText.text = "ผิด! (WRONG)";
            resultTitleText.color = Color.red;
            
            resultMessageText.text = string.IsNullOrEmpty(customMsg) ? "คำตอบยังไม่ถูกต้อง พรุ่งนี้ลองใหม่นะ!" : customMsg;
            resultMessageText.color = new Color(1f, 0.8f, 0.8f); // สีขาวอมแดงจางๆ
            rewardText.text = "สู้ๆ นะ! ไว้ลองใหม่วันพรุ่งนี้";
            
            if (sfxSource != null && wrongSound != null) sfxSource.PlayOneShot(wrongSound);
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
        Debug.Log($"[DailyQuiz] Attempting to load scene: {sceneName}");

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
