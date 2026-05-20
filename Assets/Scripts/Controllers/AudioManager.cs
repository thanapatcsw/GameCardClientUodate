using UnityEngine;
using UnityEngine.SceneManagement;

namespace StartupCity.Audio
{
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("Audio Sources")]
        [Tooltip("ใส่ AudioSource สำหรับเล่นเพลงพื้นหลัง (BGM)")]
        [SerializeField] private AudioSource bgmSource;
        [Tooltip("ใส่ AudioSource สำหรับเล่นเสียงเอฟเฟกต์ (SFX)")]
        [SerializeField] private AudioSource sfxSource;

        [Header("Background Music (BGM)")]
        [Tooltip("เพลงพื้นหลังหน้า Menu/Lobby")]
        public AudioClip menuBGM;
        [Tooltip("เพลงพื้นหลังหน้าเล่นเกม")]
        public AudioClip gameBGM;

        [Header("UI Sound Effects")]
        [Tooltip("เสียงกดปุ่มทั่วไป")]
        public AudioClip buttonClickSFX;
        [Tooltip("เสียงข้อความแจ้งเตือนสีแดง (Warning Text)")]
        public AudioClip warningTextSFX;
        [Tooltip("เสียงตอนกดสวมใส่ (Equip) กรอบ หรือเปลี่ยนรูปลักษณ์")]
        public AudioClip equipFrameSFX;

        [Header("Quiz / Event Sound Effects")]
        [Tooltip("เสียงเมื่อตอบคำถามถูกต้อง")]
        public AudioClip correctAnswerSFX;
        [Tooltip("เสียงเมื่อตอบคำถามผิดพลาด")]
        public AudioClip wrongAnswerSFX;
        [Tooltip("เสียงจับเวลาหรือเสียงเข็มนาฬิกาเดิน (Ticking/Countdown)")]
        public AudioClip quizTimerSFX;

        // ==========================================
        // 💡 Recommended Sounds (เสียงแนะนำเพิ่มเติม)
        // ==========================================
        [Header("Recommended Sound Effects")]
        [Tooltip("เสียงตอนซื้อของใน Shop สำเร็จ")]
        public AudioClip buySuccessSFX;
        [Tooltip("เสียงตอนมี Popup เด้งขึ้นมา")]
        public AudioClip popupOpenSFX;
        [Tooltip("เสียงตอนปิด Popup")]
        public AudioClip popupCloseSFX;
        [Tooltip("เสียงตอนจั่วการ์ดขึ้นมือ")]
        public AudioClip cardDrawSFX;
        [Tooltip("เสียงตอนลงการ์ดบนบอร์ด")]
        public AudioClip cardPlaySFX;
        [Tooltip("เสียงเข้าสู่เทิร์นของผู้เล่น")]
        public AudioClip turnStartSFX;
        [Tooltip("เสียงจบเกม - ชนะ")]
        public AudioClip gameWinSFX;
        [Tooltip("เสียงจบเกม - แพ้")]
        public AudioClip gameLoseSFX;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
                return;
            }
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // สลับ BGM อัตโนมัติตาม Scene
            string name = scene.name;
            if (name == "LoginScece" || name == "MainMenu 1" || name == "StoreScene")
                PlayMenuBGM();
            else if (name == "SampleScene" || name == "DailyQuizScene" || name == "GameScene")
                PlayGameBGM();
        }

        private void Start()
        {
            // เริ่มต้นด้วยการเล่นเพลงหน้า Menu ทันทีเมื่อเกมเปิด
            PlayMenuBGM();
        }

        // ==========================================
        // 🎵 Background Music Functions
        // ==========================================
        public void PlayMenuBGM()
        {
            PlayBGM(menuBGM);
        }

        public void PlayGameBGM()
        {
            PlayBGM(gameBGM);
        }

        private void PlayBGM(AudioClip clip)
        {
            if (clip == null || bgmSource == null) return;
            
            // ถ้าเล่นเพลงเดียวกันอยู่แล้ว ไม่ต้องเริ่มใหม่
            if (bgmSource.clip == clip && bgmSource.isPlaying) return;

            bgmSource.clip = clip;
            bgmSource.loop = true;
            bgmSource.Play();
        }

        public void StopBGM()
        {
            if (bgmSource != null) bgmSource.Stop();
        }

        // ==========================================
        // 🔊 Sound Effects Functions
        // ==========================================
        public void PlaySFX(AudioClip clip)
        {
            if (clip != null && sfxSource != null)
            {
                // PlayOneShot ช่วยให้เสียงเล่นทับซ้อนกันได้ (เช่นกดปุ่มรัวๆ)
                sfxSource.PlayOneShot(clip);
            }
        }

        // --- Core UI & Quiz ---
        public void PlayButtonClick() => PlaySFX(buttonClickSFX);
        public void PlayWarningText() => PlaySFX(warningTextSFX);
        public void PlayEquipFrame() => PlaySFX(equipFrameSFX);
        public void PlayCorrectAnswer() => PlaySFX(correctAnswerSFX);
        public void PlayWrongAnswer() => PlaySFX(wrongAnswerSFX);
        public void PlayTimerTick() => PlaySFX(quizTimerSFX);

        // --- Recommended Functions ---
        public void PlayBuySuccess() => PlaySFX(buySuccessSFX);
        public void PlayPopupOpen() => PlaySFX(popupOpenSFX);
        public void PlayPopupClose() => PlaySFX(popupCloseSFX);
        public void PlayCardDraw() => PlaySFX(cardDrawSFX);
        public void PlayCardPlay() => PlaySFX(cardPlaySFX);
        public void PlayTurnStart() => PlaySFX(turnStartSFX);
        public void PlayGameWin() => PlaySFX(gameWinSFX);
        public void PlayGameLose() => PlaySFX(gameLoseSFX);
    }
}
