using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using Supabase;
using Supabase.Gotrue;
using System.Threading.Tasks;

public class SupabaseManager : MonoBehaviour
{
    public static SupabaseManager Instance { get; private set; }

    [Header("Supabase Credentials (ได้จากหน้าเว็บ)")]
    [Tooltip("วาง URL ที่ก็อปปี้มาจาก Supabase ที่นี่")]
    public string supabaseUrl = "";
    
    [Tooltip("วาง Anon Key ที่ก็อปปี้มาจาก Supabase ที่นี่")]
    public string supabaseKey = "";

    // ตัวแปรสำหรับเรียกใช้ฐานข้อมูลจากสคริปต์อื่น
    private Supabase.Client supabaseClient;
    public Supabase.Client Client => supabaseClient;
    public bool IsInitialized { get; private set; }
    public string SupabaseUrl => supabaseUrl;
    public string SupabaseAnonKey => supabaseKey;

    private async void Awake()
    {
        // ทำเป็น Singleton เพื่อให้สคริปต์นี้อยู่ยืดข้าม Scene
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // เริ่มต้นการเชื่อมต่อ Supabase ทันทีที่เปิดเกม
        // ครอบ try/catch เพราะ async void ถ้า throw จะจับไม่ได้และทำให้แอปค้างเงียบ ๆ
        try
        {
            await InitializeSupabase();
        }
        catch (Exception e)
        {
            Debug.LogError($"<color=red>❌ [Supabase] เริ่มต้นการเชื่อมต่อล้มเหลว: {e.Message}</color>\n{e}");
        }
    }

    private async Task InitializeSupabase()
    {
        IsInitialized = false;

        // ถ้าไม่ได้กรอกใน Inspector ให้ดึงค่าจาก SupabaseConfig (แหล่งเดียว)
        if (string.IsNullOrEmpty(supabaseUrl)) supabaseUrl = SupabaseConfig.Url;
        if (string.IsNullOrEmpty(supabaseKey)) supabaseKey = SupabaseConfig.AnonKey;

        if (string.IsNullOrEmpty(supabaseUrl) || string.IsNullOrEmpty(supabaseKey))
        {
            Debug.LogError("<color=red>❌ [Supabase] ล้มเหลว! คุณยังไม่ได้ใส่ URL หรือ Key ใน Inspector</color>");
            return;
        }

        var options = new SupabaseOptions
        {
            AutoConnectRealtime = true
        };

        // สร้างตัวเชื่อมต่อ
        supabaseClient = new Supabase.Client(supabaseUrl, supabaseKey, options);
        await supabaseClient.InitializeAsync();
        IsInitialized = true;
        
        GameLog.Log("<color=green>✅ [Supabase] เชื่อมต่อ Database สำเร็จแล้ว!</color>");

        // ถ้ามี Session เก่าอยู่แล้ว (Auto Login) ให้โหลด Profile ทันที
        if (supabaseClient.Auth.CurrentUser != null)
        {
            GameLog.Log($"[Supabase] Active session found for: {supabaseClient.Auth.CurrentUser.Email}. Loading profile...");
            await PlayerDataService.LoadProfileAsync();
        }
    }

    // หมายเหตุ: การสมัครสมาชิกย้ายไปทำที่หน้าเว็บ (StreamingAssets/Web/index.html)
    // ผ่านระบบ OTP (Edge Functions send-otp / verify-otp) แล้ว จึงไม่มี SignUpUser ในเกมอีกต่อไป

    // ฟังก์ชันสำหรับเข้าสู่ระบบ (ส่งกลับสถานะสำเร็จ และ ข้อความ Error แบบเจาะจง)
    public async Task<(bool success, string errorMsg)> SignInUser(string email, string password)
    {
        try
        {
            email = email.Trim();
            password = password.Trim();
            
            var session = await supabaseClient.Auth.SignIn(email, password);
            if (session != null && session.User != null)
            {
                GameLog.Log($"<color=green>✅ [Supabase] ล็อกอินสำเร็จ ยินดีต้อนรับ: {session.User.Email}</color>");
                
                // โหลดข้อมูลผู้เล่น (Gems, MMR) จาก Database ทันทีที่ล็อกอิน
                await PlayerDataService.LoadProfileAsync();
                
                return (true, "");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"<color=red>❌ [Supabase] ล็อกอินไม่สำเร็จ: {e.Message}</color>");
            return (false, e.Message);
        }
        return (false, "เกิดข้อผิดพลาดไม่ทราบสาเหตุ");
    }

    // [Helper] ดึง Username จาก User Metadata ใน Session ปัจจุบัน
    public string GetCurrentUsername()
    {
        if (supabaseClient?.Auth.CurrentUser != null)
        {
            var user = supabaseClient.Auth.CurrentUser;
            if (user.UserMetadata != null && user.UserMetadata.ContainsKey("username"))
            {
                return user.UserMetadata["username"].ToString();
            }
            return user.Email; // ถ้าไม่มี username ให้ใช้อีเมลแก้ขัด
        }
        return "Player 1";
    }

    // [Legacy] Private room / manual flows may still use this helper, but Auto Match no longer
    // relies on client-side inserts here. Server-side matchmaking now creates room metadata.
    public async Task<bool> CreateRoom(string roomCode, string sessionName, int playerCount = 1)
    {
        try
        {
            if (supabaseClient == null) return false;

            var room = new RoomData
            {
                RoomCode = roomCode,
                SessionName = sessionName,
                HostName = GetCurrentUsername(),
                PlayerCount = Mathf.Clamp(playerCount, 1, 4),
                Status = "waiting",
                CreatedAt = DateTime.UtcNow
            };

            // บันทึกลงตาราง "rooms"
            await supabaseClient.From<RoomData>().Insert(room);
            
            GameLog.Log($"<color=green>✅ [Supabase] บันทึกห้อง [{roomCode}] ลงใน Database สำเร็จ!</color>");
            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"<color=red>❌ [Supabase] สร้างห้องไม่สำเร็จ: {ex.Message}</color>");
            return false;
        }
    }

    // ฟังก์ชันสำหรับออกจากระบบ
    public async Task SignOut()
    {
        if (supabaseClient?.Auth != null)
        {
            await supabaseClient.Auth.SignOut();
            GameLog.Log("<color=orange>⚠️ [Supabase] ออกจากระบบแล้ว</color>");

            // ล้างเฉพาะ key ที่เกี่ยวกับ auth และ player data
            // ไม่ใช้ DeleteAll() เพราะจะลบ settings อื่นๆ ทิ้งโดยไม่ตั้งใจ
            PlayerPrefs.DeleteKey("Username");
            PlayerPrefs.DeleteKey("TotalGems");
            PlayerPrefs.DeleteKey("MMR");
            PlayerPrefs.DeleteKey("OwnedItems");
            PlayerPrefs.DeleteKey("EquippedFrame");
            PlayerPrefs.DeleteKey("SelectedCharacter");
            PlayerPrefs.DeleteKey("MatchmakingPlayerId");
            PlayerPrefs.DeleteKey("MatchmakingRoomId");
            PlayerPrefs.DeleteKey("MatchmakingRoomCode");
            PlayerPrefs.DeleteKey("MatchmakingTargetPlayerCount");
            PlayerPrefs.DeleteKey("GameMode");
            PlayerPrefs.Save();
        }
    }
}
