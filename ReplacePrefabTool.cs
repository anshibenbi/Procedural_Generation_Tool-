using UnityEngine;
using UnityEditor;

public class ReplacePrefabTool : EditorWindow
{
    public GameObject prefab;
    public Transform parent; // 放这些20016、20017...的父对象

    [MenuItem("Tools/Replace With Prefab")]
    static void Open()
    {
        GetWindow<ReplacePrefabTool>("Replace Prefab");
    }

    void OnGUI()
    {
        prefab = (GameObject)EditorGUILayout.ObjectField("目标 Prefab", prefab, typeof(GameObject), false);
        parent = (Transform)EditorGUILayout.ObjectField("父对象", parent, typeof(Transform), true);

        if (GUILayout.Button("批量替换") && prefab != null && parent != null)
        {
            Replace();
        }
    }

    void Replace()
    {
        var children = new System.Collections.Generic.List<Transform>();
        foreach (Transform child in parent)
            children.Add(child);

        for (int i = 0; i < children.Count; i++)
        {
            var old = children[i];
            var oldRect = old.GetComponent<RectTransform>();

            // 记录原对象的完整 RectTransform 信息
            Vector3 oldPos = oldRect.localPosition;
            Vector3 oldScale = oldRect.localScale;
            Quaternion oldRot = oldRect.localRotation;
            Vector2 oldSize = oldRect.sizeDelta;      // ✨ 宽高
            Vector2 oldAnchorMin = oldRect.anchorMin;     // ✨ 锚点
            Vector2 oldAnchorMax = oldRect.anchorMax;
            Vector2 oldPivot = oldRect.pivot;
            int siblingIndex = old.GetSiblingIndex();
            string oldName = old.name;

            var newObj = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            var newRect = newObj.GetComponent<RectTransform>();

            newObj.transform.SetSiblingIndex(siblingIndex);
            newObj.name = oldName;

            // 还原所有 RectTransform 值
            newRect.anchorMin = oldAnchorMin;
            newRect.anchorMax = oldAnchorMax;
            newRect.pivot = oldPivot;
            newRect.localPosition = oldPos;
            newRect.localScale = oldScale;
            newRect.localRotation = oldRot;
            newRect.sizeDelta = oldSize;

            DestroyImmediate(old.gameObject);
        }

        Debug.Log("替换完成");
    }
}