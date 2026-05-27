using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// เครื่องมือช่วยจัด layout กระดานใน Edit mode:
/// ใส่ "การ์ดหลอก" (placeholder) ลงใน tier/noble container ชั่วคราว เพื่อให้เห็น layout จริง
/// ขณะปรับ (เพราะตอนรันจริงการ์ดถูกสร้าง runtime — edit mode ปกติแถวจะว่าง)
///
/// ปลอดภัย: placeholder ถูกตั้งชื่อมี prefix [PREVIEW] และตอนรันเกม PopulateBoard()
/// จะ ClearContainer ทิ้งก่อนเสมอ จึงไม่กระทบเกมจริง. กด Clear เพื่อเก็บกวาดก่อนเซฟได้
/// </summary>
public static class BoardLayoutPreviewTool
{
    private const string PreviewPrefix = "[PREVIEW] ";
    private const int CardsPerTier = 4;
    private const int NoblesPerSide = 2;

    [MenuItem("Tools/Board Layout/Fill Placeholder Cards")]
    private static void FillPlaceholders()
    {
        GameController gc = Object.FindFirstObjectByType<GameController>();
        if (gc == null)
        {
            EditorUtility.DisplayDialog("Board Layout", "ไม่พบ GameController ใน scene ที่เปิดอยู่", "OK");
            return;
        }

        // เคลียร์ของเก่าก่อนกันซ้ำ
        ClearPlaceholders();

        int added = 0;
        added += Fill(gc.tier3Container, gc.cardPrefab, CardsPerTier, "Tier3");
        added += Fill(gc.tier2Container, gc.cardPrefab, CardsPerTier, "Tier2");
        added += Fill(gc.tier1Container, gc.cardPrefab, CardsPerTier, "Tier1");
        added += Fill(gc.leftNobleContainer, gc.noblePrefab, NoblesPerSide, "NobleL");
        added += Fill(gc.rightNobleContainer, gc.noblePrefab, NoblesPerSide, "NobleR");

        if (added > 0)
        {
            EditorSceneManager.MarkSceneDirty(gc.gameObject.scene);
        }
        Debug.Log($"[BoardLayoutPreview] ใส่การ์ดหลอก {added} ใบแล้ว — ปรับ layout ได้เลย เสร็จแล้วกด Clear Placeholder Cards");
    }

    [MenuItem("Tools/Board Layout/Clear Placeholder Cards")]
    private static void ClearPlaceholders()
    {
        var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int removed = 0;
        foreach (Transform t in all)
        {
            if (t != null && t.name.StartsWith(PreviewPrefix))
            {
                Undo.DestroyObjectImmediate(t.gameObject);
                removed++;
            }
        }

        if (removed > 0)
        {
            var gc = Object.FindFirstObjectByType<GameController>();
            if (gc != null) EditorSceneManager.MarkSceneDirty(gc.gameObject.scene);
            Debug.Log($"[BoardLayoutPreview] ลบการ์ดหลอก {removed} ใบแล้ว");
        }
    }

    private static int Fill(Transform container, GameObject prefab, int count, string label)
    {
        if (container == null)
        {
            Debug.LogWarning($"[BoardLayoutPreview] {label}: container ยังไม่ได้ผูกใน GameController — ข้าม");
            return 0;
        }
        if (prefab == null)
        {
            Debug.LogWarning($"[BoardLayoutPreview] {label}: prefab (Card/Noble) ยังไม่ได้ผูกใน GameController — ข้าม");
            return 0;
        }

        int added = 0;
        for (int i = 0; i < count; i++)
        {
            GameObject obj = (GameObject)PrefabUtility.InstantiatePrefab(prefab, container);
            if (obj == null) continue;
            obj.name = $"{PreviewPrefix}{label}_{i + 1}";
            Undo.RegisterCreatedObjectUndo(obj, "Add Placeholder Card");
            added++;
        }
        return added;
    }
}
