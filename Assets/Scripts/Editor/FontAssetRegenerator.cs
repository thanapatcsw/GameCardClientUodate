using UnityEngine;
using UnityEditor;
using TMPro;

public class FontAssetRegenerator
{
    [MenuItem("Tools/Regenerate Thai Font Asset")]
    public static void Regenerate()
    {
        string ttfPath = "Assets/Fonts/LayijiMahaniyom-Bao-1.2.ttf";
        string assetPath = "Assets/Fonts/LayijiMahaniyom-Bao-1.asset";

        Font font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
        if (font == null)
        {
            EditorUtility.DisplayDialog("Error", $"Could not find source TTF font at {ttfPath}. Please make sure the file exists.", "OK");
            return;
        }

        // We will create the Font Asset with Dynamic mode.
        // This makes sure it generates character glyphs on the fly as they are displayed, including Thai characters.
        TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
            font,
            90, // Sampling point size
            9,  // Padding
            UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
            512, // Atlas width
            512, // Atlas height
            AtlasPopulationMode.Dynamic,
            true // Enable multi-atlas support
        );

        if (fontAsset != null)
        {
            // Do NOT delete the existing asset or .meta file to preserve GUID references!
            // AssetDatabase.CreateAsset will safely overwrite the file content while preserving the GUID.
            AssetDatabase.CreateAsset(fontAsset, assetPath);

            // In Dynamic mode, the atlas texture needs to be saved as a sub-asset
            if (fontAsset.atlasTexture != null)
            {
                AssetDatabase.AddObjectToAsset(fontAsset.atlasTexture, fontAsset);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"<color=green><b>Successfully regenerated Thai Font Asset at {assetPath}! GUID preserved.</b></color>");
            EditorUtility.DisplayDialog("Success", $"Successfully regenerated Thai Font Asset at:\n{assetPath}\n\nAll existing UI references have been preserved!", "OK");
        }
        else
        {
            Debug.LogError("Failed to create TMP Font Asset!");
            EditorUtility.DisplayDialog("Error", "Failed to generate TMP Font Asset. See Console for details.", "OK");
        }
    }
}
