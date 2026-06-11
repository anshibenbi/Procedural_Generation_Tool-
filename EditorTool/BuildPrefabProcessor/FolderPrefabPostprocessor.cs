using System.IO;
using UnityEditor;
using UnityEngine;

public class FolderPrefabPostprocessor : AssetPostprocessor
{
    private const string ConfigPath =
        "Assets/GameScripts/Editor/Postprocessor/BuildPrefabProcessor/FolderTagLayerConfig.asset";

    static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromPaths)
    {
        var config = AssetDatabase.LoadAssetAtPath<FolderTagLayerConfig>(ConfigPath);
        if (config == null)
        {
            Debug.LogWarning($"[FolderPrefabPostprocessor] 找不到配置文件：{ConfigPath}");
            return;
        }

        // ★ 核心修复：检测到配置文件本身被保存时，全量扫描所有配置目录
        bool configModified = System.Array.Exists(importedAssets, p => p == ConfigPath);
        if (configModified)
        {
            Debug.Log("[FolderPrefabPostprocessor] 配置已变更，开始全量扫描...");
            ApplyAllMappings(config);
            return; // 全量扫描完毕，不再走单文件逻辑
        }

        // 常规逻辑：仅处理本次新导入 / 移动的 prefab
        var allPaths = new System.Collections.Generic.List<string>(importedAssets);
        allPaths.AddRange(movedAssets);

        foreach (var assetPath in allPaths)
        {
            if (Path.GetExtension(assetPath) != ".prefab") continue;

            foreach (var mapping in config.mappings)
            {
                if (string.IsNullOrEmpty(mapping.folderPath)) continue;

                var folder = mapping.folderPath.Replace('\\', '/').TrimEnd('/') + "/";
                if (!assetPath.StartsWith(folder, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                ApplyTagAndLayer(assetPath, mapping);
                break;
            }
        }
    }

    // ★ 全量扫描：遍历每条规则对应目录下所有 prefab
    static void ApplyAllMappings(FolderTagLayerConfig config)
    {
        foreach (var mapping in config.mappings)
        {
            if (string.IsNullOrEmpty(mapping.folderPath)) continue;

            var folder = mapping.folderPath.Replace('\\', '/').TrimEnd('/');
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });

            foreach (var guid in guids)
            {
                var prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                ApplyTagAndLayer(prefabPath, mapping);
            }
        }
    }

    // ★ 菜单项：手动触发全量刷新（方便首次使用或批量修改后刷新）
    [MenuItem("Tools/Folder Prefab Postprocessor/Force Apply All")]
    static void ForceApplyAll()
    {
        var config = AssetDatabase.LoadAssetAtPath<FolderTagLayerConfig>(ConfigPath);
        if (config == null)
        {
            Debug.LogError($"[FolderPrefabPostprocessor] 找不到配置文件：{ConfigPath}");
            return;
        }
        ApplyAllMappings(config);
        Debug.Log("[FolderPrefabPostprocessor] 全量应用完成");
    }

    static void ApplyTagAndLayer(string assetPath, FolderTagLayerConfig.FolderMapping mapping)
    {
        if (!string.IsNullOrEmpty(mapping.tag))
            EnsureTagExists(mapping.tag);

        int layerIndex = -1;
        if (!string.IsNullOrEmpty(mapping.layer))
        {
            EnsureLayerExists(mapping.layer);
            layerIndex = LayerMask.NameToLayer(mapping.layer);
        }

        var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (prefabAsset == null) return;

        var prefab = PrefabUtility.LoadPrefabContents(assetPath);
        bool changed = false;

        var targets = mapping.applyToChildren
            ? prefab.GetComponentsInChildren<Transform>(includeInactive: true)
            : new[] { prefab.transform };

        foreach (var t in targets)
        {
            var go = t.gameObject;
            if (mapping.excludeChildren.Contains(go.name)) continue;

            if (!string.IsNullOrEmpty(mapping.tag) && go.tag != mapping.tag)
            {
                go.tag = mapping.tag;
                changed = true;
            }

            if (layerIndex >= 0 && go.layer != layerIndex)
            {
                go.layer = layerIndex;
                changed = true;
            }
        }

        if (changed)
        {
            PrefabUtility.SaveAsPrefabAsset(prefab, assetPath);
            Debug.Log($"[FolderPrefabPostprocessor] 已更新：{assetPath} → Tag={mapping.tag} Layer={mapping.layer}");
        }

        PrefabUtility.UnloadPrefabContents(prefab);
    }

    static void EnsureTagExists(string tag)
    {
        var tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var tagsProperty = tagManager.FindProperty("tags");
        for (int i = 0; i < tagsProperty.arraySize; i++)
            if (tagsProperty.GetArrayElementAtIndex(i).stringValue == tag) return;

        tagsProperty.InsertArrayElementAtIndex(tagsProperty.arraySize);
        tagsProperty.GetArrayElementAtIndex(tagsProperty.arraySize - 1).stringValue = tag;
        tagManager.ApplyModifiedProperties();
        Debug.Log($"[FolderPrefabPostprocessor] 自动注册 Tag：{tag}");
    }

    static void EnsureLayerExists(string layerName)
    {
        var tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        for (int i = 8; i <= 31; i++)
        {
            var prop = tagManager.FindProperty($"layers[{i}]");
            if (prop == null) continue;
            if (prop.stringValue == layerName) return;
        }
        for (int i = 8; i <= 31; i++)
        {
            var prop = tagManager.FindProperty($"layers[{i}]");
            if (prop == null || !string.IsNullOrEmpty(prop.stringValue)) continue;
            prop.stringValue = layerName;
            tagManager.ApplyModifiedProperties();
            Debug.Log($"[FolderPrefabPostprocessor] 自动注册 Layer[{i}]：{layerName}");
            return;
        }
        Debug.LogWarning($"[FolderPrefabPostprocessor] Layer 槽位已满，无法注册：{layerName}");
    }
}