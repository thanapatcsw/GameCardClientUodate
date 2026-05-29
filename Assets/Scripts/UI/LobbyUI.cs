using UnityEngine;
using TMPro;
using Fusion;
using StartupCity.Audio;

public class LobbyUI : MonoBehaviour
{
    public static LobbyUI Instance { get; private set; }

    [Header("---- View Groups ----")]
    public GameObject selectionView; // กลุ่ม UI กรอกชื่อห้อง
    public GameObject roomInfoView;  // กลุ่ม UI ข้อมูลในห้อง

    [Header("---- UI Elements ----")]
    public TMP_InputField roomNameInputField;
    public TMP_Text roomNameText;    // แสดงชื่อห้องที่เข้าอยู่
    public TMP_Text playerListText;  // แสดงรายชื่อคนในห้อง
    public GameObject startButton;   // ปุ่มเริ่มเกม (เฉพาะ Host)
    public TMP_Text statusWarningText; // ข้อความคำเตือน (เช่น รอคนเข้าห้อง)

    [Header("---- Panels ----")]
    public GameObject lobbyPanel;
    public GameObject modeSelectPanel; 

    private void Awake()
    { 
        Instance = this; 
    }

    private void Start()
    {
        // เริ่มต้นด้วยหน้าเลือกห้องเสมอ
        SetViewState(false);
    }

    // ปุ่มสร้างห้อง
    public void OnClickCreateRoom()
    {
        AudioManager.Instance?.PlayButtonClick();
        string rName = roomNameInputField.text;
        if (string.IsNullOrEmpty(rName)) 
        {
            rName = Random.Range(1000, 9999).ToString();
            roomNameInputField.text = rName;
        }
        
        SetViewState(true);
        if (roomNameText != null) roomNameText.text = "Room Code : " + rName;
        
        if (statusWarningText != null) 
        {
            statusWarningText.gameObject.SetActive(true);
            statusWarningText.text = "Connecting to Photon Fusion...";
        }
        
        if (startButton != null) startButton.SetActive(false);

        GameLog.Log($"[Lobby] กำลังสร้างห้องในโหมด Host: {rName}");
        if (FusionManager.Instance != null)
        {
            // [FIX-ANDROID] ใช้ Coroutine version (ไม่ใช้ async) เพื่อรับประกัน Main Thread
            FusionManager.Instance.StartGameCoroutine(Fusion.GameMode.Host, rName);
        }
    }

    // ปุ่มเข้าห้อง
    public void OnClickJoinRoom()
    {
        AudioManager.Instance?.PlayButtonClick();
        string rName = roomNameInputField.text;
        if (string.IsNullOrEmpty(rName)) {
            AudioManager.Instance?.PlayWarningText();
            Debug.LogWarning("[Lobby] กรุณาใส่ชื่อห้องที่ต้องการเข้าร่วม");
            return;
        }

        if (statusWarningText != null)
        {
            statusWarningText.gameObject.SetActive(true);
            statusWarningText.text = "Joining room...";
        }

        GameLog.Log($"[Lobby] กำลังเข้าร่วมห้อง: {rName}");
        StartCoroutine(JoinRoomWithRetryCoroutine(rName));
    }

    private System.Collections.IEnumerator JoinRoomWithRetryCoroutine(string rName)
    {
        const int maxRetries = 5;
        const float retryDelay = 1.5f;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            if (attempt > 0)
            {
                GameLog.Log($"[Lobby] Retry join attempt {attempt}/{maxRetries}...");
                if (statusWarningText != null)
                    statusWarningText.text = $"Retrying... ({attempt}/{maxRetries})";
                yield return new UnityEngine.WaitForSeconds(retryDelay);
            }

            bool? result = null;

            // [FIX-ANDROID] ใช้ Coroutine version + callback แทน ContinueWith
            yield return FusionManager.Instance.StartGameCoroutine(Fusion.GameMode.Client, rName);

            // ตรวจสอบว่า Runner เชื่อมต่อสำเร็จหรือไม่
            if (FusionManager.Instance.Runner != null && FusionManager.Instance.Runner.IsRunning)
            {
                GameLog.Log($"[Lobby] Joined room {rName} successfully.");
                SetViewState(true);
                yield break;
            }
        }

        // ถ้าเข้าไม่ได้หลังจากรีทรีทั้งหมด
        if (statusWarningText != null)
        {
            statusWarningText.gameObject.SetActive(true);
            statusWarningText.text = "Cannot join room. Please check the room code.";
        }
    }


    // --- ระบบสลับหน้าจอ (UI States) ---

    public void SetViewState(bool isInRoom)
    {
        if (selectionView != null) selectionView.SetActive(!isInRoom);
        if (roomInfoView != null) roomInfoView.SetActive(isInRoom);

        if (isInRoom)
        {
            // พยายามดึงชื่อห้องจาก Fusion ก่อน ถ้าไม่มีค่อยใช้จาก InputField
            string sessionName = "";
            if (FusionManager.Instance != null && FusionManager.Instance.Runner != null && FusionManager.Instance.Runner.SessionInfo.IsValid)
            {
                sessionName = FusionManager.Instance.Runner.SessionInfo.Name;
            }
            else
            {
                sessionName = roomNameInputField.text;
            }

            if (roomNameText != null) roomNameText.text = "Room Code : " + sessionName;
            
            // ตรวจสอบสถานะ MasterClient เบื้องต้น
            if (startButton != null) 
                startButton.SetActive(FusionManager.Instance != null && FusionManager.Instance.IsMasterClient);
        }
    }

    public void UpdatePlayerList(string list, int count, bool isMaster)
    {
        if (playerListText != null)
        {
            playerListText.text = list;
        }

        // จัดการปุ่มเริ่มเกมและคำเตือน
        if (isMaster)
        {
            // เริ่มเกมได้ถ้ามีคน 2, 3 หรือ 4 คน
            bool canStart = (count >= 2 && count <= 4);
            if (startButton != null) startButton.SetActive(canStart);
            
            if (statusWarningText != null)
            {
                statusWarningText.gameObject.SetActive(!canStart || count >= 4);
                if (count < 2) statusWarningText.text = "ห้องต้องการผู้เล่นอย่างน้อย 2 คนจึงจะเริ่มเกมได้";
                else if (count >= 4) statusWarningText.text = "ห้องเต็มแล้ว! กดเริ่มเกมได้เลย";
                else statusWarningText.text = ""; // ซ่อนคำเตือนเมื่อครบ 2-3 คน
                
                // ถ้ายืนยันจะให้โชว์ตลอดก็สามารถเปลี่ยนเป็น:
                // statusWarningText.gameObject.SetActive(true);
                // if (count < 2) statusWarningText.text = "ต้องมีผู้เล่นอย่างน้อย 2 คนจึงจะเริ่มได้";
                // else statusWarningText.text = $"มีผู้เล่น {count} คน พร้อมเริ่มเกม!";
            }
        }
        else
        {
            if (startButton != null) startButton.SetActive(false);
            if (statusWarningText != null)
            {
                statusWarningText.gameObject.SetActive(true);
                statusWarningText.text = "Waiting for Host to start the game...";
            }
        }
    }

    // ปุ่มออกจากการเชื่อมต่อ / ออกจากหน้าห้อง
    public void OnClickLeaveRoom()
    {
        AudioManager.Instance?.PlayButtonClick();
        GameLog.Log("[Lobby] ออกจากห้องตัวเอง/การเชื่อมต่อ");
        FusionManager.Instance.Disconnect();
        SetViewState(false);
    }

    public void OnClickStartGame()
    {
        AudioManager.Instance?.PlayButtonClick();
        GameLog.Log("[Lobby] สั่งเริ่มเกม!");
        if (FusionManager.Instance != null)
        {
            FusionManager.Instance.LoadGameScene();
        }
    }

    // ปุ่มกลับ
    public void OnClickBack()
    {
        AudioManager.Instance?.PlayButtonClick();
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        if (modeSelectPanel != null) modeSelectPanel.SetActive(true);
    }
}
