using UnityEngine;
using UnityEditor;
using TMPro;
using System.Text.RegularExpressions;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public class ReplaceFontTool
{
    [MenuItem("Tools/Replace Thai Fonts with FC Quantum")]
    public static void ReplaceFonts()
    {
        string newFontPath = "Assets/Fonts/FC Quantum [Non-commercial] SDF.asset";
        string oldFontPath = "Assets/Fonts/LayijiMahaniyom-Bao-1.asset";

        TMP_FontAsset newFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(newFontPath);
        TMP_FontAsset oldFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(oldFontPath);

        if (newFont == null)
        {
            Debug.LogError("Could not find new font at " + newFontPath);
            return;
        }

        int changedPrefabs = 0;
        int changedScenes = 0;
        int totalTextReplaced = 0;

        // 1. Update Prefabs
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
        foreach (string guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith("Packages/")) continue;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            TMP_Text[] texts = prefab.GetComponentsInChildren<TMP_Text>(true);
            bool modified = false;

            foreach (TMP_Text text in texts)
            {
                if (ShouldReplaceFont(text, oldFont))
                {
                    text.font = newFont;
                    EditorUtility.SetDirty(text);
                    modified = true;
                    totalTextReplaced++;
                }
            }

            if (modified)
            {
                PrefabUtility.SavePrefabAsset(prefab);
                changedPrefabs++;
            }
        }

        // 2. Update Scenes
        string[] sceneGuids = AssetDatabase.FindAssets("t:Scene");
        foreach (string guid in sceneGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith("Packages/")) continue;

            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            
            bool modified = false;
            GameObject[] rootObjects = scene.GetRootGameObjects();
            
            foreach (GameObject root in rootObjects)
            {
                TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
                foreach (TMP_Text text in texts)
                {
                    if (ShouldReplaceFont(text, oldFont))
                    {
                        text.font = newFont;
                        EditorUtility.SetDirty(text);
                        modified = true;
                        totalTextReplaced++;
                    }
                }
            }

            if (modified)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                changedScenes++;
            }
        }

        Debug.Log($"<color=green><b>Font Replacement Complete!</b></color>\n" +
                  $"Replaced font on {totalTextReplaced} Text components.\n" +
                  $"Modified {changedPrefabs} Prefabs and {changedScenes} Scenes.");
    }

    private static bool ShouldReplaceFont(TMP_Text textComponent, TMP_FontAsset oldFont)
    {
        // 1. If it's using the old Thai font
        if (textComponent.font == oldFont)
            return true;

        // 2. If the text contains Thai characters
        if (Regex.IsMatch(textComponent.text, @"[\u0E00-\u0E7F]"))
            return true;

        // 3. If it's using the default font and might need Thai support, we can also return true, 
        // but let's stick to the specific requirements to avoid messing up intentional English fonts.
        return false;
    }
}
