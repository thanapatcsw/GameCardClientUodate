using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;
using Fusion;

public class LobbyPanelUI : MonoBehaviour
{
    public static LobbyPanelUI Instance { get; private set; }

    [Header("UI Elements")]
    public GameObject lobbyPanel;
    public TextMeshProUGUI roomCodeText;
    public TextMeshProUGUI playerListText;
    public Button startButton;
    public Button leaveButton;

    private void Awake()
    {
        Instance = this;
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        
        startButton.onClick.AddListener(OnStartClicked);
        leaveButton.onClick.AddListener(OnLeaveClicked);
    }

    public void Show(string roomCode, bool isHost)
    {
        if (lobbyPanel == null) return;
        
        lobbyPanel.SetActive(true);
        roomCodeText.text = $"Room Code: <color=#FFD700>{roomCode}</color>";
        
        // เฉพาะ Host ถึงจะเห็นปุ่มเริ่มเกม
        startButton.gameObject.SetActive(isHost);
        startButton.interactable = false; // จะเปิดเมื่อคนครบ 2+
    }

    public void Hide()
    {
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
    }

    // เรียกดดย FusionManager เมื่อมีผู้เล่น Join เข้ามา
    public void UpdatePlayerList(IEnumerable<PlayerRef> players, NetworkRunner runner)
    {
        string names = "<b>Players in Room:</b>\n";
        int count = 0;

        foreach (var player in players)
        {
            // ในเครื่องจริงเราจะดึงชื่อจาก NetworkObject ของผู้เล่นแต่ละคน
            // ตอนนี้เราโชว์ Player ID ไปก่อนเพื่อทดสอบ
            names += $"- Player {player.PlayerId} {(player == runner.LocalPlayer ? "(YOU)" : "")}\n";
            count++;
        }

        playerListText.text = names;
        
        // เงื่อนไขเริ่มเกม: เป็น Host และมีคน >= 2
        if (startButton.gameObject.activeSelf)
        {
            startButton.interactable = (count >= 2);
        }
    }

    private void OnStartClicked()
    {
        Debug.Log("<color=green>[Lobby] เริ่มเกมมมม!</color>");
        // [TODO] สั่งให้ทึกคนข้าม Scene ไปยัง GameScene
    }

    private void OnLeaveClicked()
    {
        Debug.Log("[Lobby] ออกจากห้อง");
        // [TODO] สั่งให้ Fusion ตัดการเชื่อมต่อ
        Hide();
    }
}
