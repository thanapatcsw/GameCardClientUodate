using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;
using TMPro;

public class LoadingScreenManager : MonoBehaviour
{
    // ตัวแปร static สำหรับเก็บชื่อฉากเป้าหมายเอาไว้ข้าม Scene
    public static string targetSceneName;

    [Header("UI Elements (ตั้งค่าที่ Inspector)")]
    public Image progressBar;               // [ใส่หรือไม่ใส่ก็ได้] หลอดโหลด 
    public TextMeshProUGUI progressText;    // [ใส่หรือไม่ใส่ก็ได้] ข้อความแสดงตัวเลข %
    
    [Header("Spinner Setting")]
    public RectTransform spinnerIcon;       // ลากวงกลมที่จะให้หมุนมาใส่ช่องนี้
    public float spinnerSpeed = 300f;       // ความเร็วในการหมุน

    // -------------------------------------------------------------
    // ฟังก์ชันนี้เอาไว้ให้ฉากอื่นๆ (เช่น ปุ่ม Login) เรียกใช้งาน
    // -------------------------------------------------------------
    public static void LoadScene(string sceneName)
    {
        // 1. จำชื่อด่านปลายทางไว้
        targetSceneName = sceneName;
        // 2. โหลดเข้าฉาก LoadingScene ทันที
        SceneManager.LoadScene("LoadingScene"); 
    }

    // -------------------------------------------------------------
    // โค้ดการทำงานเมื่อเข้ามาในฉาก LoadingScene
    // -------------------------------------------------------------
    private void Start()
    {
        // เมื่อฉากโหลดเสร็จ ให้เริ่มทำการโหลดด่านเป้าหมายแบบเบื้องหลัง (Async) ทันที
        if (!string.IsNullOrEmpty(targetSceneName))
        {
            StartCoroutine(LoadTargetSceneAsync());
        }
    }

    private void Update()
    {
        // สั่งให้วงล้อหมุนตลอดเวลา
        if (spinnerIcon != null)
        {
            spinnerIcon.Rotate(0, 0, -spinnerSpeed * Time.deltaTime);
        }
    }

    private IEnumerator LoadTargetSceneAsync()
    {
        // สั่งโหลด Scene เป้าหมาย
        AsyncOperation operation = SceneManager.LoadSceneAsync(targetSceneName);
        
        // บล็อกไม่ให้ย้ายฉากทันทีที่โหลดเสร็จ
        operation.allowSceneActivation = false;

        while (!operation.isDone)
        {
            // Unity โหลดเต็ม 100% ค่า progress จะเป็น 0.9 ดังนั้นต้องนำมาเทียบสัดส่วน
            float progress = Mathf.Clamp01(operation.progress / 0.9f);

            // อัปเดตแถบ UI หลอดโหลด (ถ้ามีการตั้งค่าไว้)
            if (progressBar != null)
                progressBar.fillAmount = progress;
                
            // อัปเดต UI ข้อความ % (ถ้ามีการตั้งค่าไว้)
            if (progressText != null)
                progressText.text = $"LOADING... {Mathf.RoundToInt(progress * 100)}%";

            // ถ้าโหลดด่านหลังบ้านเสร็จแล้ว (ได้ค่าเกิน 0.9)
            if (operation.progress >= 0.9f)
            {
                // หน่วงเวลาให้อ่านหน้าจอโหลดสักนิด (0.5 - 1 วิ) 
                yield return new WaitForSeconds(0.5f);
                
                // ปลดบล็อกให้เกมย้ายเข้าด่านเป้าหมายจริงๆ
                operation.allowSceneActivation = true;
            }

            yield return null;
        }
    }
}
