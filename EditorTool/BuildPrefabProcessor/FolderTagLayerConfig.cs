using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Tools/Folder Tag Layer Config")]
public class FolderTagLayerConfig : ScriptableObject
{
    [Serializable]
    public class FolderMapping
    {
        [Tooltip("相对于 Assets/ 的文件夹路径，例如：Assets/Prefabs/Enemy")]
        public string folderPath;

        [Tooltip("要设置的 Tag，留空则不修改")]
        public string tag;

        [Tooltip("要设置的 Layer 名称，留空则不修改")]
        public string layer;

        [Tooltip("是否递归设置所有子对象")]
        public bool applyToChildren = true;

        [Tooltip("填入子物体名称，这些对象不会被修改")]
        public List<string> excludeChildren = new();
    }

    public List<FolderMapping> mappings = new();
}