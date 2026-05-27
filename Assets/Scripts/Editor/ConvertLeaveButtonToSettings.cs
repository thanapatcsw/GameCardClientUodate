using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;

/// <summary>
/// เครื่องมือ Editor: แปลงปุ่มมุมขวาบน "LeaveGame" (เดิมเป็นกากบาท) ให้เป็นปุ่มตั้งค่า
/// แบบถาวรในฉาก — เห็นผลทันทีตั้งแต่ Edit mode (ไม่ต้องกด Play)
///
/// วิธีใช้: เปิดฉาก SampleScene แล้วไปที่เมนู  Tools ▸ Settings Button ▸ แปลงปุ่ม X เป็นปุ่มตั้งค่า
/// </summary>
public static class ConvertLeaveButtonToSettings
{
    private const string LeaveButtonName = "LeaveGame";
    private const string GearAssetPath = "Assets/Generated/SettingsGearIcon.png";

    [MenuItem("Tools/Settings Button/แปลงปุ่ม X เป็นปุ่มตั้งค่า")]
    public static void Convert()
    {
        // 1) หาปุ่ม LeaveGame ในฉากที่เปิดอยู่ (รวมที่ถูกปิด)
        GameObject btnGO = FindInActiveScene(LeaveButtonName);
        if (btnGO == null)
        {
            EditorUtility.DisplayDialog("ไม่พบปุ่ม",
                $"หา GameObject ชื่อ \"{LeaveButtonName}\" ในฉากที่เปิดอยู่ไม่เจอ\nกรุณาเปิดฉาก SampleScene ก่อน", "OK");
            return;
        }

        Button btn = btnGO.GetComponent<Button>();
        if (btn == null)
        {
            EditorUtility.DisplayDialog("ไม่มี Button", $"\"{LeaveButtonName}\" ไม่มี component Button", "OK");
            return;
        }

        // 2) ทำให้มี SettingsPanelUI ในฉาก (สร้างใหม่ถ้ายังไม่มี)
        SettingsPanelUI panel = Object.FindFirstObjectByType<SettingsPanelUI>();
        if (panel == null)
        {
            GameObject panelGO = new GameObject("SettingsPanelUI");
            panel = panelGO.AddComponent<SettingsPanelUI>();
            Undo.RegisterCreatedObjectUndo(panelGO, "Create SettingsPanelUI");
        }

        // 3) เปลี่ยนรูปกากบาท -> ไอคอนเฟือง
        Sprite gear = GetOrCreateGearSprite();
        Image img = btn.GetComponent<Image>();
        if (img != null && gear != null)
        {
            Undo.RecordObject(img, "Change to gear icon");
            img.sprite = gear;
            img.color = new Color(0.9f, 0.92f, 0.98f, 1f);
            EditorUtility.SetDirty(img);
        }

        // 4) เปลี่ยน OnClick: ลบของเดิม (LeaveToMainMenu) แล้วผูก OpenSettings แทน
        Undo.RecordObject(btn, "Rewire OnClick to OpenSettings");
        for (int i = btn.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            UnityEventTools.RemovePersistentListener(btn.onClick, i);
        UnityEventTools.AddPersistentListener(btn.onClick, panel.OpenSettings);
        EditorUtility.SetDirty(btn);

        // 5) บันทึกฉากให้ค่าคงอยู่
        EditorSceneManager.MarkSceneDirty(btnGO.scene);
        EditorSceneManager.SaveScene(btnGO.scene);

        Debug.Log("<color=green><b>[Settings Button] แปลงปุ่ม X เป็นปุ่มตั้งค่าเรียบร้อย!</b></color> เห็นเฟืองได้ทันทีใน Edit mode");
        EditorUtility.DisplayDialog("สำเร็จ",
            "เปลี่ยนปุ่มมุมขวาบนเป็นปุ่มตั้งค่า (เฟือง) เรียบร้อยแล้ว\nกดปุ่มแล้วจะเปิดหน้าปรับเสียง + ออกเมนูหลัก", "เยี่ยม!");
    }

    // ----------------------------------------------------------
    private static GameObject FindInActiveScene(string objectName)
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid()) return null;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == objectName) return t.gameObject;
        return null;
    }

    // ----------------------------------------------------------
    //  สร้างไฟล์ไอคอนเฟือง (PNG) แล้ว import เป็น Sprite ถ้ายังไม่มี
    // ----------------------------------------------------------
    private static Sprite GetOrCreateGearSprite()
    {
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(GearAssetPath);
        if (existing != null) return existing;

        const int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color clear = new Color(1f, 1f, 1f, 0f);
        Vector2 c = new Vector2(size / 2f, size / 2f);

        const float teeth = 8f;
        float outerR = size * 0.46f;   // ปลายฟันเฟือง
        float rootR  = size * 0.36f;   // ร่องระหว่างฟัน
        float bodyR  = size * 0.40f;   // ตัวเฟือง (วงตัน)
        float holeR  = size * 0.16f;   // รูตรงกลาง

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - c.x, dy = y - c.y;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx);
                float tooth = Mathf.Cos(ang * teeth) > 0f ? outerR : rootR;
                float edge = Mathf.Max(bodyR, tooth);
                bool solid = r <= edge && r >= holeR;
                tex.SetPixel(x, y, solid ? Color.white : clear);
            }
        }
        tex.Apply();

        // เขียนเป็นไฟล์ PNG ในโฟลเดอร์ Assets/Generated
        string dir = Path.GetDirectoryName(GearAssetPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(GearAssetPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(GearAssetPath, ImportAssetOptions.ForceUpdate);

        // ตั้งค่า importer ให้เป็น Sprite + มี alpha โปร่งใส
        TextureImporter importer = AssetImporter.GetAtPath(GearAssetPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(GearAssetPath);
    }
}
