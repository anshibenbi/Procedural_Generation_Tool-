#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public class LLamaSharpPluginFixer
{
    // 有 Nvidia 显卡改为 "cuda12"，否则保持 "avx2"
    private const string EDITOR_BACKEND = "avx2";

    [MenuItem("Tools/Fix LLamaSharp Plugin Conflicts")]
    public static void FixPlugins()
    {
        // 正确：搜索 dll 文件
        string[] allDlls = AssetDatabase.FindAssets("t:DefaultAsset", 
            new[] { "Assets/Packages" });

        int fixedCount = 0;

        foreach (string guid in allDlls)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            // 只处理 LLamaSharp native DLL
            if (!path.EndsWith(".dll")) continue;
            if (!path.Contains("LLamaSharp")) continue;
            if (!path.Contains("/native/")) continue;

            PluginImporter importer = AssetImporter.GetAtPath(path) as PluginImporter;
            if (importer == null) continue;

            bool shouldBeEditorCompatible = path.Contains($"/native/{EDITOR_BACKEND}/");
            bool currentlyEditorCompatible = importer.GetCompatibleWithEditor();

            if (currentlyEditorCompatible != shouldBeEditorCompatible)
            {
                importer.SetCompatibleWithEditor(shouldBeEditorCompatible);
                importer.SaveAndReimport();
                Debug.Log($"[LLamaSharp Fix] {(shouldBeEditorCompatible ? "✓ 启用" : "✗ 禁用")} Editor: {path}");
                fixedCount++;
            }
        }

        AssetDatabase.Refresh();
        Debug.Log($"[LLamaSharp Fix] 完成！共修改了 {fixedCount} 个插件配置。");
    }
}
#endif