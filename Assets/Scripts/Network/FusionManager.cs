using UnityEngine;
using Fusion;
using Fusion.Sockets;
using System.Collections.Generic;
using System;
using System.IO;
using UnityEngine.SceneManagement;
using System.Threading.Tasks;
using System.Linq;
using System.Text;
using System.Globalization;

public class FusionManager : MonoBehaviour, INetworkRunnerCallbacks
{
    public static FusionManager Instance { get; private set; }
    public event Action PlayerNamesUpdated;
    public event Action ActivePlayersChanged;
    public event Action<int, int, int, int> TurnStateReceived;
    public event Action<int> QuizStartedReceived;
    public event Action<QuizAnswerSnapshot> QuizAnswerReceived;
    public event Action<List<QuizAnswerSnapshot>, List<int>> QuizResultsReceived;
    public event Action<EconomyStateSnapshot> EconomyStateReceived;
    public event Action<BoardStateSnapshot> BoardStateReceived;
    public event Action QuizStartRequested;
    // late-joiner ขอ full state จาก host — ส่ง playerId ของคนที่ขอ เพื่อให้ host ตอบกลับเฉพาะคนนั้น
    public event Action<int> FullStateRequested;

    private const char PlayerNameSeparator = '|';
    private const string PlayerNameMessageType = "NAME";
    private const string TurnStateMessageType = "TURN";
    private const string QuizStartMessageType = "QUIZSTART";
    private const string QuizAnswerMessageType = "QUIZANSWER";
    private const string QuizResultMessageType = "QUIZRESULT";
    private const string EconomyStateMessageType = "ECON";
    private const string BoardStateMessageType = "BOARD";
    private const string QuizRequestMessageType = "QUIZREQ";
    private const string StateRequestMessageType = "STATEREQ";
    private NetworkRunner _runner;
    private NetworkSceneManagerDefault _sceneManager;
    private readonly Dictionary<int, string> _playerNames = new Dictionary<int, string>();
    private bool _hasPendingQuizStart;
    private int _pendingQuizStartIndex = -1;

    public struct QuizAnswerSnapshot
    {
        public int PlayerIndex;
        public bool IsCorrect;
        public float TimeTaken;
    }

    public struct EconomyPlayerSnapshot
    {
        public int Score;
        public int[] Coins;
        public int[] Bonuses;
        public int QuizBlackCoins;
    }

    public struct EconomyStateSnapshot
    {
        public int[] BankCoins;
        public EconomyPlayerSnapshot[] Players;
    }

    // สถานะการ์ดบนกระดาน (face-up market) สำหรับ sync ออนไลน์
    // แต่ละ tier เก็บ cardId ตามลำดับช่อง (string.Empty = ช่องว่าง)
    // UsedCardIds = cardId ทั้งหมดที่ถูกจั่วออกจากกอง (กันการ์ดซ้ำ/เพี้ยนข้ามเครื่อง)
    public struct BoardStateSnapshot
    {
        public string[] Tier1CardIds;
        public string[] Tier2CardIds;
        public string[] Tier3CardIds;
        public string[] UsedCardIds;
    }

    [Header("---- Scene Names ----")]
    public string gameSceneName = "SampleScene";

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

    public bool IsMasterClient => _runner != null && _runner.IsServer;
    public NetworkRunner Runner => _runner;
    public int ActivePlayerCount => _runner == null ? 0 : _runner.ActivePlayers.Count();

    public void StartMatchedGame(string roomCode, string sceneName = null)
    {
        // [FIX] ระบุว่าเป็นโหมดออนไลน์
        PlayerPrefs.SetString("GameMode", "Online");
        PlayerPrefs.Save();

        // ถ้ามีการระบุชื่อฉากมา (เช่น จาก Auto-Match) ให้โหลดฉากนั้นทันที
        // แต่ถ้าไม่มี (เช่น จากการสร้างห้องแมนนวล) ให้ส่ง null เพื่อรอในหน้า Lobby
        _ = StartGame(GameMode.AutoHostOrClient, roomCode, sceneName);
    }

    public void LoadGameScene()
    {
        if (_runner != null && _runner.IsServer)
        {
            string sceneToLoad = string.IsNullOrEmpty(gameSceneName) ? "SampleScene" : gameSceneName;
            _runner.LoadScene(ResolveSceneRef(sceneToLoad), UnityEngine.SceneManagement.LoadSceneMode.Single);

            // snapshot ผู้เล่นที่อยู่จริงตอนเกมเริ่ม + อัปเดต status='playing' ในครั้งเดียว
            // (ไม่ sync ทุก join/leave เพราะ DB เก็บไว้เป็นบันทึกแมตช์ ไม่ใช่สถานะ lobby realtime)
            SetRoomStatus("playing", _runner.ActivePlayers.Count());
        }
    }

    // host-only helper สำหรับอัปเดตสถานะห้องใน Supabase (waiting → playing → finished)
    // ส่ง playerCount มาด้วยถ้าต้อง snapshot จำนวนผู้เล่นที่อยู่ขณะนั้น
    public void SetRoomStatus(string status, int? playerCount = null)
    {
        if (_runner == null || !_runner.IsServer) return;
        string roomCode = _runner.SessionInfo?.Name;
        if (string.IsNullOrEmpty(roomCode)) return;
        if (SupabaseManager.Instance == null || !SupabaseManager.Instance.IsInitialized) return;

        _ = PlayerDataService.CreateRoomAsync(roomCode, playerCount: playerCount, status: status);
    }

    public async Task StartGame(GameMode mode, string roomName, string sceneToLoad = null)
    {
        // [FIX] ระบุว่าเป็นโหมดออนไลน์เสมอเมื่อมีการเริ่ม Network
        PlayerPrefs.SetString("GameMode", "Online");
        PlayerPrefs.Save();

        await ResetRunnerAsync();

        _runner = gameObject.AddComponent<NetworkRunner>();
        _sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();
        _runner.AddCallbacks(this);
        _runner.ProvideInput = true;
        _playerNames.Clear();
        _hasPendingQuizStart = false;
        _pendingQuizStartIndex = -1;

        // ถ้าไม่ได้ระบุฉากมา ให้ใช้ฉากปัจจุบัน (สำหรับ Lobby แมนนวล)
        // แต่ถ้าเป็นโหมด Auto-Match หรือมีการระบุฉากมา ให้ใช้ฉากนั้นเลย
        SceneRef targetScene;
        if (!string.IsNullOrEmpty(sceneToLoad))
        {
            targetScene = ResolveSceneRef(sceneToLoad);
        }
        else
        {
            targetScene = SceneRef.FromIndex(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }

        var result = await _runner.StartGame(new StartGameArgs()
        {
            GameMode = mode,
            SessionName = roomName,
            Scene = targetScene,
            SceneManager = _sceneManager
        });

        if (result.Ok)
        {
            GameLog.Log($"[Fusion] Started session successfully: {roomName} (Mode: {mode})");
            
            if (_runner != null && _runner.IsServer && SupabaseManager.Instance != null && SupabaseManager.Instance.IsInitialized)
            {
                // server-authoritative: ผ่าน Edge Function (service_role) แทนการ insert ตรงจาก client ที่ติด RLS
                _ = PlayerDataService.CreateRoomAsync(roomName, roomName, 1);
            }

            // [ต่อปลั๊ก!] สั่งให้ LobbyUI แสดงหน้า RoomInfoView และโชว์รหัสห้อง
            if (LobbyUI.Instance != null)
            {
                LobbyUI.Instance.SetViewState(true); // เปิดหน้า RoomInfoView
                if (LobbyUI.Instance.roomNameText != null)
                {
                    LobbyUI.Instance.roomNameText.text = "Room Code : " + roomName;
                }
            }
        }
        else
        {
            Debug.LogError($"[Fusion] StartGame failed: {result.ShutdownReason}");
            CleanupRunnerComponents();
        }
    }

    public void Disconnect()
    {
        _ = ResetRunnerAsync();
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        GameLog.Log($"[Fusion] Player joined: {player}");

        RegisterPlayerName(runner.LocalPlayer.PlayerId, GetLocalPlayerName(runner.LocalPlayer.PlayerId));

        if (runner.IsServer && player != runner.LocalPlayer)
        {
            SendKnownPlayerNamesToPlayer(player);
        }

        if (player == runner.LocalPlayer && LobbyUI.Instance != null)
        {
            LobbyUI.Instance.SetViewState(true);
        }

        if (player == runner.LocalPlayer && !runner.IsServer)
        {
            SendLocalPlayerNameToServer();
        }

        RefreshPlayerList(runner);
        NotifyActivePlayersChanged();
        // ไม่ sync player_count ขึ้น DB ทุกครั้ง — รอ snapshot ตอน LoadGameScene
        // (lobby UI อ่านจาก Fusion ตรงอยู่แล้ว, DB เก็บไว้เป็น "บันทึกแมตช์")
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        GameLog.Log($"[Fusion] Player left: {player}");
        if (_playerNames.Remove(player.PlayerId))
        {
            NotifyPlayerNamesUpdated();
        }
        RefreshPlayerList(runner);
        NotifyActivePlayersChanged();
    }

    private void RefreshPlayerList(NetworkRunner runner)
    {
        string list = "Players in Room:\n";
        foreach (var p in runner.ActivePlayers)
        {
            list += "- Player " + p.PlayerId + (p == runner.LocalPlayer ? " (You)" : string.Empty) + "\n";
        }

        if (LobbyUI.Instance != null)
        {
            LobbyUI.Instance.UpdatePlayerList(list, runner.ActivePlayers.Count(), runner.IsServer);
        }
    }

    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        GameLog.Log($"[Fusion] Runner shutdown: {shutdownReason}");
        if (runner == _runner)
        {
            CleanupRunnerComponents();
        }
        NotifyActivePlayersChanged();
    }

    public void OnConnectedToServer(NetworkRunner runner)
    {
        GameLog.Log("[Fusion] Connected to server successfully.");
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        GameLog.Log($"[Fusion] Disconnected from server: {reason}");
    }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
    {
        string payload = Encoding.UTF8.GetString(data.Array, data.Offset, data.Count);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        string[] parts = payload.Split(PlayerNameSeparator);
        if (parts.Length == 0)
        {
            return;
        }

        if (string.Equals(parts[0], PlayerNameMessageType, StringComparison.Ordinal))
        {
            if (parts.Length < 3 || !int.TryParse(parts[1], out int playerId))
            {
                return;
            }

            string playerName = string.Join(PlayerNameSeparator.ToString(), parts.Skip(2));
            RegisterPlayerName(playerId, playerName);

            if (runner.IsServer)
            {
                BroadcastPlayerName(player, playerId, playerName);
            }

            return;
        }

        if (string.Equals(parts[0], TurnStateMessageType, StringComparison.Ordinal))
        {
            if (parts.Length < 5)
            {
                return;
            }

            if (!int.TryParse(parts[1], out int currentPlayerIndex) ||
                !int.TryParse(parts[2], out int currentRound) ||
                !int.TryParse(parts[3], out int totalTurnCount) ||
                !int.TryParse(parts[4], out int currentTurnDisplay))
            {
                return;
            }

            if (runner.IsServer)
            {
                TurnStateReceived?.Invoke(currentPlayerIndex, currentRound, totalTurnCount, currentTurnDisplay);

                foreach (var activePlayer in runner.ActivePlayers)
                {
                    if (activePlayer == player || activePlayer == runner.LocalPlayer)
                    {
                        continue;
                    }

                    runner.SendReliableDataToPlayer(activePlayer, default, data.ToArray());
                }
            }
            else
            {
                TurnStateReceived?.Invoke(currentPlayerIndex, currentRound, totalTurnCount, currentTurnDisplay);
            }

            return;
        }

        if (string.Equals(parts[0], QuizStartMessageType, StringComparison.Ordinal))
        {
            if (runner.IsServer || parts.Length < 2 || !int.TryParse(parts[1], out int questionIndex))
            {
                return;
            }

            _hasPendingQuizStart = true;
            _pendingQuizStartIndex = questionIndex;
            QuizStartedReceived?.Invoke(questionIndex);
            return;
        }

        if (string.Equals(parts[0], QuizRequestMessageType, StringComparison.Ordinal))
        {
            // เฉพาะ host เท่านั้นที่ตอบสนองคำขอเริ่มควิซ (client เป็นคนส่งมา)
            if (runner.IsServer)
            {
                QuizStartRequested?.Invoke();
            }

            return;
        }

        if (string.Equals(parts[0], StateRequestMessageType, StringComparison.Ordinal))
        {
            // เฉพาะ host เท่านั้นที่ตอบสนองคำขอ full state (late-joiner เป็นคนส่งมา)
            // ส่ง playerId ของคนขอไปด้วย เพื่อให้ host ตอบกลับเฉพาะคนนั้น (ไม่รีเซ็ต timer คนอื่น)
            if (runner.IsServer)
            {
                FullStateRequested?.Invoke(player.PlayerId);
            }

            return;
        }

        if (string.Equals(parts[0], QuizAnswerMessageType, StringComparison.Ordinal))
        {
            if (!runner.IsServer || parts.Length < 4 || !int.TryParse(parts[1], out int answerPlayerIndex))
            {
                return;
            }

            if (!TryParseBooleanFlag(parts[2], out bool isCorrect) ||
                !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float timeTaken))
            {
                return;
            }

            QuizAnswerReceived?.Invoke(new QuizAnswerSnapshot
            {
                PlayerIndex = answerPlayerIndex,
                IsCorrect = isCorrect,
                TimeTaken = timeTaken
            });

            return;
        }

        if (string.Equals(parts[0], QuizResultMessageType, StringComparison.Ordinal))
        {
            if (runner.IsServer || parts.Length < 2)
            {
                return;
            }

            List<QuizAnswerSnapshot> quizAnswers = DecodeQuizAnswers(parts[1]);
            List<int> rewardGemIndices = parts.Length >= 3
                ? DecodeRewardGemIndices(parts[2])
                : new List<int>();

            QuizResultsReceived?.Invoke(quizAnswers, rewardGemIndices);
            return;
        }

        if (string.Equals(parts[0], EconomyStateMessageType, StringComparison.Ordinal))
        {
            if (parts.Length < 3)
            {
                return;
            }

            EconomyStateSnapshot snapshot = DecodeEconomyState(parts[1], parts[2]);
            if (runner.IsServer)
            {
                EconomyStateReceived?.Invoke(snapshot);

                foreach (var activePlayer in runner.ActivePlayers)
                {
                    if (activePlayer == player || activePlayer == runner.LocalPlayer)
                    {
                        continue;
                    }

                    runner.SendReliableDataToPlayer(activePlayer, default, data.ToArray());
                }
            }
            else
            {
                EconomyStateReceived?.Invoke(snapshot);
            }

            return;
        }

        if (string.Equals(parts[0], BoardStateMessageType, StringComparison.Ordinal))
        {
            if (parts.Length < 5)
            {
                return;
            }

            BoardStateSnapshot boardSnapshot = new BoardStateSnapshot
            {
                Tier1CardIds = DecodeStringArray(parts[1]),
                Tier2CardIds = DecodeStringArray(parts[2]),
                Tier3CardIds = DecodeStringArray(parts[3]),
                UsedCardIds = DecodeStringArray(parts[4])
            };

            BoardStateReceived?.Invoke(boardSnapshot);

            if (runner.IsServer)
            {
                foreach (var activePlayer in runner.ActivePlayers)
                {
                    if (activePlayer == player || activePlayer == runner.LocalPlayer)
                    {
                        continue;
                    }

                    runner.SendReliableDataToPlayer(activePlayer, default, data.ToArray());
                }
            }

            return;
        }

        int separatorIndex = payload.IndexOf(PlayerNameSeparator);
        if (separatorIndex <= 0 || separatorIndex >= payload.Length - 1)
        {
            return;
        }

        string legacyPlayerIdText = payload.Substring(0, separatorIndex);
        string legacyPlayerName = payload.Substring(separatorIndex + 1);
        if (!int.TryParse(legacyPlayerIdText, out int legacyPlayerId))
        {
            return;
        }

        RegisterPlayerName(legacyPlayerId, legacyPlayerName);

        if (runner.IsServer)
        {
            BroadcastPlayerName(player, legacyPlayerId, legacyPlayerName);
        }
    }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

    private SceneRef ResolveSceneRef(string sceneName = null)
    {
        string targetScene = string.IsNullOrEmpty(sceneName) ? gameSceneName : sceneName;
        var buildIndex = FindBuildIndexByName(targetScene);
        if (buildIndex >= 0)
        {
            return SceneRef.FromIndex(buildIndex);
        }

        return SceneRef.FromIndex(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
    }

    private static int FindBuildIndexByName(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            return -1;
        }

        for (var i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
        {
            var scenePath = SceneUtility.GetScenePathByBuildIndex(i);
            var buildSceneName = Path.GetFileNameWithoutExtension(scenePath);

            if (string.Equals(buildSceneName, sceneName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private async Task ResetRunnerAsync()
    {
        if (_runner != null)
        {
            try
            {
                await _runner.Shutdown();
            }
            catch (Exception shutdownException)
            {
                Debug.LogWarning($"[Fusion] Runner shutdown warning: {shutdownException.Message}");
            }
        }

        CleanupRunnerComponents();
    }

    private void CleanupRunnerComponents()
    {
        if (_runner != null)
        {
            Destroy(_runner);
            _runner = null;
        }

        if (_sceneManager != null)
        {
            Destroy(_sceneManager);
            _sceneManager = null;
        }
    }

    public string GetRemotePlayerName(int remoteIndex)
    {
        if (_runner == null || remoteIndex < 0)
        {
            return null;
        }

        var remotePlayers = _runner.ActivePlayers
            .Where(p => p != _runner.LocalPlayer)
            .OrderBy(p => p.PlayerId)
            .ToList();

        if (remoteIndex >= remotePlayers.Count)
        {
            return null;
        }

        var remotePlayer = remotePlayers[remoteIndex];
        return _playerNames.TryGetValue(remotePlayer.PlayerId, out string remoteName)
            ? remoteName
            : null;
    }

    public int GetLocalPlayerSeatIndex()
    {
        if (_runner == null)
        {
            return 0;
        }

        var orderedPlayers = GetOrderedActivePlayers();
        for (int i = 0; i < orderedPlayers.Count; i++)
        {
            if (orderedPlayers[i] == _runner.LocalPlayer)
            {
                return i;
            }
        }

        return 0;
    }

    public string GetPlayerNameBySeat(int seatIndex)
    {
        var orderedPlayers = GetOrderedActivePlayers();
        if (seatIndex < 0 || seatIndex >= orderedPlayers.Count)
        {
            return null;
        }

        return GetDisplayNameForPlayer(orderedPlayers[seatIndex]);
    }

    public void SendTurnState(int currentPlayerIndex, int currentRound, int totalTurnCount, int currentTurnDisplay)
    {
        if (_runner == null)
        {
            return;
        }

        byte[] payload = EncodeTurnStatePayload(currentPlayerIndex, currentRound, totalTurnCount, currentTurnDisplay);
        if (_runner.IsServer)
        {
            foreach (var activePlayer in _runner.ActivePlayers)
            {
                if (activePlayer == _runner.LocalPlayer)
                {
                    continue;
                }

                _runner.SendReliableDataToPlayer(activePlayer, default, payload);
            }

            return;
        }

        _runner.SendReliableDataToServer(default, payload);
    }

    public void SendBoardState(BoardStateSnapshot snapshot)
    {
        if (_runner == null)
        {
            return;
        }

        byte[] payload = BuildBoardPayload(snapshot);

        if (_runner.IsServer)
        {
            foreach (var activePlayer in _runner.ActivePlayers)
            {
                if (activePlayer == _runner.LocalPlayer)
                {
                    continue;
                }

                _runner.SendReliableDataToPlayer(activePlayer, default, payload);
            }

            return;
        }

        _runner.SendReliableDataToServer(default, payload);
    }

    // client ขอให้ host เริ่มควิซ (เมื่อ client เป็นคนจบเทิร์นที่ถึงรอบควิซ)
    public void RequestQuizStart()
    {
        if (_runner == null)
        {
            return;
        }

        byte[] payload = Encoding.UTF8.GetBytes(QuizRequestMessageType);

        if (_runner.IsServer)
        {
            // host เรียกเองได้โดยตรง ไม่ต้องส่งผ่าน network
            QuizStartRequested?.Invoke();
            return;
        }

        _runner.SendReliableDataToServer(default, payload);
    }

    // client (late-joiner) ขอ full state ปัจจุบันจาก host
    public void RequestFullState()
    {
        if (_runner == null || _runner.IsServer)
        {
            return; // host มี state ครบอยู่แล้ว ไม่ต้องขอ
        }

        byte[] payload = Encoding.UTF8.GetBytes(StateRequestMessageType);
        _runner.SendReliableDataToServer(default, payload);
    }

    // host ตอบกลับ full state เฉพาะ player ที่ขอ (ส่งเจาะจง ไม่ broadcast — กันรีเซ็ต timer คนที่กำลังเล่นอยู่)
    public void SendBoardStateToPlayer(int playerId, BoardStateSnapshot snapshot)
    {
        if (_runner == null || !_runner.IsServer || !TryGetPlayerRef(playerId, out PlayerRef target))
        {
            return;
        }

        _runner.SendReliableDataToPlayer(target, default, BuildBoardPayload(snapshot));
    }

    public void SendEconomyStateToPlayer(int playerId, EconomyStateSnapshot snapshot)
    {
        if (_runner == null || !_runner.IsServer || !TryGetPlayerRef(playerId, out PlayerRef target))
        {
            return;
        }

        _runner.SendReliableDataToPlayer(target, default, BuildEconomyPayload(snapshot));
    }

    public void SendTurnStateToPlayer(int playerId, int currentPlayerIndex, int currentRound, int totalTurnCount, int currentTurnDisplay)
    {
        if (_runner == null || !_runner.IsServer || !TryGetPlayerRef(playerId, out PlayerRef target))
        {
            return;
        }

        _runner.SendReliableDataToPlayer(target, default,
            EncodeTurnStatePayload(currentPlayerIndex, currentRound, totalTurnCount, currentTurnDisplay));
    }

    private bool TryGetPlayerRef(int playerId, out PlayerRef result)
    {
        if (_runner != null)
        {
            foreach (var activePlayer in _runner.ActivePlayers)
            {
                if (activePlayer.PlayerId == playerId)
                {
                    result = activePlayer;
                    return true;
                }
            }
        }

        result = default;
        return false;
    }

    private static byte[] BuildBoardPayload(BoardStateSnapshot snapshot)
    {
        return Encoding.UTF8.GetBytes(string.Join(
            PlayerNameSeparator.ToString(),
            BoardStateMessageType,
            EncodeStringArray(snapshot.Tier1CardIds),
            EncodeStringArray(snapshot.Tier2CardIds),
            EncodeStringArray(snapshot.Tier3CardIds),
            EncodeStringArray(snapshot.UsedCardIds)));
    }

    private static byte[] BuildEconomyPayload(EconomyStateSnapshot snapshot)
    {
        string bankPayload = EncodeIntArray(snapshot.BankCoins);
        string playersPayload = EncodeEconomyPlayers(snapshot.Players);
        return Encoding.UTF8.GetBytes(
            $"{EconomyStateMessageType}{PlayerNameSeparator}{bankPayload}{PlayerNameSeparator}{playersPayload}");
    }

    public void SendQuizStart(int questionIndex)
    {
        if (_runner == null || !_runner.IsServer)
        {
            return;
        }

        byte[] payload = Encoding.UTF8.GetBytes($"{QuizStartMessageType}{PlayerNameSeparator}{questionIndex}");
        foreach (var activePlayer in _runner.ActivePlayers)
        {
            if (activePlayer == _runner.LocalPlayer)
            {
                continue;
            }

            _runner.SendReliableDataToPlayer(activePlayer, default, payload);
        }
    }

    public void SendQuizAnswer(int playerIndex, bool isCorrect, float timeTaken)
    {
        if (_runner == null || _runner.IsServer)
        {
            return;
        }

        string correctnessFlag = isCorrect ? "1" : "0";
        string timeTakenText = timeTaken.ToString("0.000", CultureInfo.InvariantCulture);
        byte[] payload = Encoding.UTF8.GetBytes(
            $"{QuizAnswerMessageType}{PlayerNameSeparator}{playerIndex}{PlayerNameSeparator}{correctnessFlag}{PlayerNameSeparator}{timeTakenText}");
        _runner.SendReliableDataToServer(default, payload);
    }

    public void SendQuizResults(IEnumerable<QuizAnswerSnapshot> answers, IEnumerable<int> rewardGemIndices)
    {
        if (_runner == null || !_runner.IsServer)
        {
            return;
        }

        string answersPayload = EncodeQuizAnswers(answers);
        string rewardsPayload = EncodeRewardGemIndices(rewardGemIndices);
        byte[] payload = Encoding.UTF8.GetBytes(
            $"{QuizResultMessageType}{PlayerNameSeparator}{answersPayload}{PlayerNameSeparator}{rewardsPayload}");

        foreach (var activePlayer in _runner.ActivePlayers)
        {
            if (activePlayer == _runner.LocalPlayer)
            {
                continue;
            }

            _runner.SendReliableDataToPlayer(activePlayer, default, payload);
        }
    }

    public void SendEconomyState(EconomyStateSnapshot snapshot)
    {
        if (_runner == null)
        {
            return;
        }

        byte[] payload = BuildEconomyPayload(snapshot);

        if (_runner.IsServer)
        {
            foreach (var activePlayer in _runner.ActivePlayers)
            {
                if (activePlayer == _runner.LocalPlayer)
                {
                    continue;
                }

                _runner.SendReliableDataToPlayer(activePlayer, default, payload);
            }

            return;
        }

        _runner.SendReliableDataToServer(default, payload);
    }

    public bool TryConsumePendingQuizStart(out int questionIndex)
    {
        if (_hasPendingQuizStart)
        {
            questionIndex = _pendingQuizStartIndex;
            _hasPendingQuizStart = false;
            _pendingQuizStartIndex = -1;
            return true;
        }

        questionIndex = -1;
        return false;
    }

    private void SendLocalPlayerNameToServer()
    {
        if (_runner == null)
        {
            return;
        }

        string localName = GetLocalPlayerName(_runner.LocalPlayer.PlayerId);
        byte[] payload = EncodePlayerNamePayload(_runner.LocalPlayer.PlayerId, localName);
        _runner.SendReliableDataToServer(default, payload);
    }

    private void SendKnownPlayerNamesToPlayer(PlayerRef targetPlayer)
    {
        if (_runner == null || !_runner.IsServer)
        {
            return;
        }

        foreach (var pair in _playerNames)
        {
            byte[] payload = EncodePlayerNamePayload(pair.Key, pair.Value);
            _runner.SendReliableDataToPlayer(targetPlayer, default, payload);
        }
    }

    private void BroadcastPlayerName(PlayerRef sourcePlayer, int playerId, string playerName)
    {
        if (_runner == null || !_runner.IsServer)
        {
            return;
        }

        byte[] payload = EncodePlayerNamePayload(playerId, playerName);
        foreach (var activePlayer in _runner.ActivePlayers)
        {
            if (activePlayer == sourcePlayer)
            {
                continue;
            }

            _runner.SendReliableDataToPlayer(activePlayer, default, payload);
        }
    }

    private void RegisterPlayerName(int playerId, string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return;
        }

        _playerNames[playerId] = playerName;
        NotifyPlayerNamesUpdated();
    }

    private void NotifyPlayerNamesUpdated()
    {
        PlayerNamesUpdated?.Invoke();
    }

    private void NotifyActivePlayersChanged()
    {
        ActivePlayersChanged?.Invoke();
    }

    private static byte[] EncodePlayerNamePayload(int playerId, string playerName)
    {
        string safeName = string.IsNullOrWhiteSpace(playerName) ? "Player " + playerId : playerName.Trim();
        return Encoding.UTF8.GetBytes($"{PlayerNameMessageType}{PlayerNameSeparator}{playerId}{PlayerNameSeparator}{safeName}");
    }

    private static byte[] EncodeTurnStatePayload(int currentPlayerIndex, int currentRound, int totalTurnCount, int currentTurnDisplay)
    {
        return Encoding.UTF8.GetBytes(
            $"{TurnStateMessageType}{PlayerNameSeparator}{currentPlayerIndex}{PlayerNameSeparator}{currentRound}{PlayerNameSeparator}{totalTurnCount}{PlayerNameSeparator}{currentTurnDisplay}");
    }

    private List<PlayerRef> GetOrderedActivePlayers()
    {
        if (_runner == null)
        {
            return new List<PlayerRef>();
        }

        return _runner.ActivePlayers
            .OrderBy(p => p.PlayerId)
            .ToList();
    }

    private string GetDisplayNameForPlayer(PlayerRef player)
    {
        if (_playerNames.TryGetValue(player.PlayerId, out string playerName) && !string.IsNullOrWhiteSpace(playerName))
        {
            return playerName;
        }

        return "Player " + player.PlayerId;
    }

    private static bool TryParseBooleanFlag(string value, out bool result)
    {
        if (value == "1")
        {
            result = true;
            return true;
        }

        if (value == "0")
        {
            result = false;
            return true;
        }

        return bool.TryParse(value, out result);
    }

    private static string EncodeQuizAnswers(IEnumerable<QuizAnswerSnapshot> answers)
    {
        if (answers == null)
        {
            return string.Empty;
        }

        return string.Join(";", answers.Select(answer =>
            string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1},{2:0.000}",
                answer.PlayerIndex,
                answer.IsCorrect ? 1 : 0,
                answer.TimeTaken)));
    }

    private static List<QuizAnswerSnapshot> DecodeQuizAnswers(string payload)
    {
        var decodedAnswers = new List<QuizAnswerSnapshot>();
        if (string.IsNullOrWhiteSpace(payload))
        {
            return decodedAnswers;
        }

        string[] answerEntries = payload.Split(';');
        foreach (string answerEntry in answerEntries)
        {
            if (string.IsNullOrWhiteSpace(answerEntry))
            {
                continue;
            }

            string[] answerParts = answerEntry.Split(',');
            if (answerParts.Length < 3 ||
                !int.TryParse(answerParts[0], out int playerIndex) ||
                !TryParseBooleanFlag(answerParts[1], out bool isCorrect) ||
                !float.TryParse(answerParts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float timeTaken))
            {
                continue;
            }

            decodedAnswers.Add(new QuizAnswerSnapshot
            {
                PlayerIndex = playerIndex,
                IsCorrect = isCorrect,
                TimeTaken = timeTaken
            });
        }

        return decodedAnswers;
    }

    private static string EncodeRewardGemIndices(IEnumerable<int> rewardGemIndices)
    {
        if (rewardGemIndices == null)
        {
            return string.Empty;
        }

        return string.Join(",", rewardGemIndices);
    }

    private static List<int> DecodeRewardGemIndices(string payload)
    {
        var rewardGemIndices = new List<int>();
        if (string.IsNullOrWhiteSpace(payload))
        {
            return rewardGemIndices;
        }

        string[] rewardParts = payload.Split(',');
        foreach (string rewardPart in rewardParts)
        {
            if (int.TryParse(rewardPart, out int gemIndex))
            {
                rewardGemIndices.Add(gemIndex);
            }
        }

        return rewardGemIndices;
    }

    private static string EncodeEconomyPlayers(IEnumerable<EconomyPlayerSnapshot> players)
    {
        if (players == null)
        {
            return string.Empty;
        }

        return string.Join(";", players.Select(player =>
            $"{player.Score}~{EncodeIntArray(player.Coins)}~{EncodeIntArray(player.Bonuses)}~{player.QuizBlackCoins}"));
    }

    private static EconomyStateSnapshot DecodeEconomyState(string bankPayload, string playersPayload)
    {
        var snapshot = new EconomyStateSnapshot
        {
            BankCoins = DecodeIntArray(bankPayload),
            Players = System.Array.Empty<EconomyPlayerSnapshot>()
        };

        if (string.IsNullOrWhiteSpace(playersPayload))
        {
            return snapshot;
        }

        string[] playerEntries = playersPayload.Split(';');
        var players = new List<EconomyPlayerSnapshot>(playerEntries.Length);
        foreach (string playerEntry in playerEntries)
        {
            if (string.IsNullOrWhiteSpace(playerEntry))
            {
                continue;
            }

            string[] parts = playerEntry.Split('~');
            if (parts.Length < 3 || !int.TryParse(parts[0], out int score))
            {
                continue;
            }

            int quizBlackCoins = 0;
            if (parts.Length >= 4)
            {
                int.TryParse(parts[3], out quizBlackCoins);
            }

            players.Add(new EconomyPlayerSnapshot
            {
                Score = score,
                Coins = DecodeIntArray(parts[1]),
                Bonuses = DecodeIntArray(parts[2]),
                QuizBlackCoins = quizBlackCoins
            });
        }

        snapshot.Players = players.ToArray();
        return snapshot;
    }

    private static string EncodeIntArray(IEnumerable<int> values)
    {
        if (values == null)
        {
            return string.Empty;
        }

        return string.Join(",", values);
    }

    private static int[] DecodeIntArray(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return System.Array.Empty<int>();
        }

        string[] parts = payload.Split(',');
        int[] values = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            int.TryParse(parts[i], out values[i]);
        }

        return values;
    }

    // cardId ไม่มี ',' หรือ '|' อยู่แล้ว ใช้ '-' แทนช่องว่าง
    private const string EmptyCardSlotToken = "-";

    private static string EncodeStringArray(IEnumerable<string> values)
    {
        if (values == null)
        {
            return string.Empty;
        }

        return string.Join(",", values.Select(v => string.IsNullOrEmpty(v) ? EmptyCardSlotToken : v));
    }

    private static string[] DecodeStringArray(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return System.Array.Empty<string>();
        }

        string[] parts = payload.Split(',');
        string[] values = new string[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            values[i] = parts[i] == EmptyCardSlotToken ? string.Empty : parts[i];
        }

        return values;
    }

    private static string GetLocalPlayerName(int fallbackPlayerId)
    {
        if (SupabaseManager.Instance != null)
        {
            string supabaseName = SupabaseManager.Instance.GetCurrentUsername();
            if (!string.IsNullOrWhiteSpace(supabaseName))
            {
                return supabaseName;
            }
        }

        string savedName = PlayerPrefs.GetString("Username", string.Empty);
        return string.IsNullOrWhiteSpace(savedName) ? "Player " + fallbackPlayerId : savedName;
    }
}
