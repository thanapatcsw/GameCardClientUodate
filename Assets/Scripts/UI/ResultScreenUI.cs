using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using System;
using StartupCity.Audio;

public class ResultScreenUI : MonoBehaviour
{
    private const string MatchmakingRoomIdPrefsKey = "MatchmakingRoomId";
    private const string MatchmakingRoomCodePrefsKey = "MatchmakingRoomCode";

    [Header("---- Match Result UI (End Game) ----")]
    public GameObject matchPanel;
    public TextMeshProUGUI matchTitleText;
    public Transform matchRankContainer;
    public TextMeshProUGUI matchMmrText;
    public Button matchActionButton;
    public TextMeshProUGUI matchButtonText;

    [Header("---- Quiz Result UI (In Game) ----")]
    public GameObject quizResultPanel;
    public TextMeshProUGUI quizTitleText;
    public Transform quizRankContainer;
    public TextMeshProUGUI quizRewardText;
    public Button quizActionButton;
    public TextMeshProUGUI quizButtonText;

    [Header("---- Shared Settings ----")]
    public GameObject playerRowPrefab;
    public float autoCloseTime = 10f;
    private float countdown;
    private bool isDisplaying = false;
    private bool isGameOver = false;
    private bool isAutoCloseEnabled = false;
    public Action onClosed;

    [Header("---- Effects ----")]
    public ParticleSystem victoryParticles;

    void Awake()
    {
        if (matchPanel != null) matchPanel.SetActive(false);
        if (quizResultPanel != null) quizResultPanel.SetActive(false);
        
        if (matchActionButton != null) matchActionButton.onClick.AddListener(OnActionButtonClick);
        if (quizActionButton != null) quizActionButton.onClick.AddListener(OnActionButtonClick);
    }

    void Update()
    {
        if (isDisplaying && isAutoCloseEnabled && countdown > 0)
        {
            countdown -= Time.deltaTime;
            UpdateActionButtonText();

            if (countdown <= 0)
            {
                OnActionButtonClick();
            }
        }
    }

    private void UpdateActionButtonText()
    {
        TextMeshProUGUI activeBtnText = isGameOver ? matchButtonText : quizButtonText;
        if (activeBtnText != null)
        {
            string actionLabel = isGameOver ? "กลับหน้าเมนู" : "เริ่มเกมต่อ";
            activeBtnText.text = $"{actionLabel} ({Mathf.CeilToInt(countdown)}s)";
        }
    }

    public void ShowResults(string title, List<string> playerRankings, bool gameOverStatus, string rewardMsg = "", bool playFireworks = false)
    {
        isGameOver = gameOverStatus;
        isAutoCloseEnabled = true;
        countdown = autoCloseTime;
        isDisplaying = true;

        // ปิด Panel ทั้งหมดก่อน
        if (matchPanel != null) matchPanel.SetActive(false);
        if (quizResultPanel != null) quizResultPanel.SetActive(false);

        // เลือก Panel และองค์ประกอบ UI ที่จะใช้งาน
        GameObject activePanel = isGameOver ? matchPanel : quizResultPanel;
        TextMeshProUGUI activeTitle = isGameOver ? matchTitleText : quizTitleText;
        Transform activeContainer = isGameOver ? matchRankContainer : quizRankContainer;

        if (activePanel != null) activePanel.SetActive(true);
        if (activeTitle != null) 
        {
            activeTitle.text = title;
            // เปลี่ยนสีหัวข้อตามผลลัพธ์ (เขียว = ถูก, แดง = ผิด)
            activeTitle.color = playFireworks ? Color.green : Color.red;
        }

        // แสดง Reward (เฉพาะควิซ)
        if (!isGameOver && quizRewardText != null)
        {
            quizRewardText.text = rewardMsg;
            quizRewardText.color = playFireworks ? Color.green : Color.red; // เปลี่ยนสีข้อความแจ้งเตือนด้วย
            quizRewardText.gameObject.SetActive(!string.IsNullOrEmpty(rewardMsg));
        }

        // แสดงลำดับ Rank / Turn Order
        if (activeContainer != null)
        {
            foreach (Transform child in activeContainer)
            {
                Destroy(child.gameObject);
            }

            for (int i = 0; i < playerRankings.Count; i++)
            {
                GameObject row = Instantiate(playerRowPrefab, activeContainer);
                var rowText = row.GetComponentInChildren<TextMeshProUGUI>();
                if (rowText != null)
                {
                    rowText.text = $"อันดับที่ {i + 1}: {playerRankings[i]}";
                    if (i == 0) rowText.color = new Color(1f, 0.84f, 0f); // สีทองสำหรับอันดับ 1
                }
            }
        }

        UpdateActionButtonText();

        // จัดการ MMR (เฉพาะจบเกม)
        if (isGameOver && matchMmrText != null)
        {
            int delta = PlayerPrefs.GetInt("LastMmrDelta", 0);
            int newMmr = PlayerPrefs.GetInt("MMR", 1000);
            // [FIX] ล้างค่าทันทีหลังอ่าน ป้องกันค่าเก่าโผล่ในรอบถัดไป
            PlayerPrefs.DeleteKey("LastMmrDelta");
            PlayerPrefs.Save();
            string sign = delta >= 0 ? "+" : "";
            matchMmrText.text = $"MMR: {sign}{delta}  (Total: {newMmr})";
            matchMmrText.color = delta >= 0 ? Color.green : Color.red;
            matchMmrText.gameObject.SetActive(true);
        }

        if ((isGameOver || playFireworks) && victoryParticles != null)
        {
            victoryParticles.Play();
        }

        // เสียงจบเกม
        if (isGameOver)
        {
            if (playFireworks) AudioManager.Instance?.PlayGameWin();
            else               AudioManager.Instance?.PlayGameLose();
        }

        GameLog.Log($"[ResultUI] แสดงหน้าจอ {(isGameOver ? "Match" : "Quiz")}: {title}");
    }

    public void OnActionButtonClick()
    {
        AudioManager.Instance?.PlayButtonClick();
        if (!isDisplaying) return;
        
        isDisplaying = false;
        isAutoCloseEnabled = false;
        
        if (matchPanel != null) matchPanel.SetActive(false);
        if (quizResultPanel != null) quizResultPanel.SetActive(false);
        
        StopAllCoroutines();

        if (isGameOver)
        {
            GameLog.Log("[ResultUI] กลับหน้าเมนู...");
            CloseOnlineRoomAndClearMatchState();
            SceneManager.LoadScene("MainMenu 1"); 
        }
        else
        {
            if (onClosed != null) onClosed.Invoke();
        }
    }

    private void CloseOnlineRoomAndClearMatchState()
    {
        if (FusionManager.Instance != null) FusionManager.Instance.Disconnect();
        PlayerPrefs.DeleteKey(MatchmakingRoomIdPrefsKey);
        PlayerPrefs.DeleteKey(MatchmakingRoomCodePrefsKey);
        PlayerPrefs.Save();
    }
}
