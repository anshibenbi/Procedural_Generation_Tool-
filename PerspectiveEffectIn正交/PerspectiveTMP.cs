// PerspectiveTMP.cs
using UnityEngine;
using TMPro;

[ExecuteAlways]
[RequireComponent(typeof(TMP_Text))]
public class PerspectiveTMP : MonoBehaviour
{
    private TMP_Text tmpText;
    private PerspectivePanel panel;

    void Awake()
    {
        tmpText = GetComponent<TMP_Text>();
        panel = GetComponentInParent<PerspectivePanel>();
        Debug.Log($"Awake - tmpText: {tmpText != null}, panel: {panel != null}"); // ← 这里
        tmpText.OnPreRenderText += OnPreRenderText;
    }

    void OnEnable()
    {
        tmpText = GetComponent<TMP_Text>();
        panel = GetComponentInParent<PerspectivePanel>();
        Refresh(); // 直接刷新一次
    }

    void OnPreRenderText(TMP_TextInfo textInfo)
    {
        Debug.Log("OnPreRenderText 触发了"); // ← 这里
        ApplyWarp(textInfo);
    }

    void OnDestroy()
    {
        tmpText.OnPreRenderText -= OnPreRenderText;
    }

    // 父物体参数改变时，外部调用这个刷新
    public void Refresh()
    {
        if (tmpText == null) tmpText = GetComponent<TMP_Text>();
        if (panel == null) panel = GetComponentInParent<PerspectivePanel>();

        // 1. 让 TMP 先生成好原始网格
        tmpText.ForceMeshUpdate();

        // 2. 再修改顶点并提交
        ApplyWarp(tmpText.textInfo);
    }

    void ApplyWarp(TMP_TextInfo textInfo)
    {
        if (panel == null) return;

        Vector3[] warpedCorners = panel.WarpedCorners;
        Rect panelRect = panel.PanelRect;

        Vector3 wbl = warpedCorners[0];
        Vector3 wtl = warpedCorners[1];
        Vector3 wtr = warpedCorners[2];
        Vector3 wbr = warpedCorners[3];

        float minX = panelRect.xMin;
        float minY = panelRect.yMin;
        float sizeX = panelRect.width;
        float sizeY = panelRect.height;

        RectTransform selfRect = GetComponent<RectTransform>();
        RectTransform panelRect_rt = panel.GetComponent<RectTransform>();
        Vector2 offset = panelRect_rt.InverseTransformPoint(selfRect.position);

        for (int i = 0; i < textInfo.characterCount; i++)
        {
            var charInfo = textInfo.characterInfo[i];
            if (!charInfo.isVisible) continue;

            var verts = textInfo.meshInfo[charInfo.materialReferenceIndex].vertices;
            int vi = charInfo.vertexIndex;

            for (int j = 0; j < 4; j++)
            {
                Vector3 v = verts[vi + j];

                float worldX = v.x + offset.x;
                float worldY = v.y + offset.y;
                float u = (worldX - minX) / sizeX;
                float w = (worldY - minY) / sizeY;

                Vector3 bottom = Vector3.Lerp(wbl, wbr, u);
                Vector3 top = Vector3.Lerp(wtl, wtr, u);
                verts[vi + j] = Vector3.Lerp(bottom, top, w) - new Vector3(offset.x, offset.y);
            }
        }

        // 3. ForceMeshUpdate 之后再提交，不会被覆盖
        for (int i = 0; i < textInfo.meshInfo.Length; i++)
        {
            textInfo.meshInfo[i].mesh.vertices = textInfo.meshInfo[i].vertices;
            tmpText.UpdateGeometry(textInfo.meshInfo[i].mesh, i);
        }
    }
}