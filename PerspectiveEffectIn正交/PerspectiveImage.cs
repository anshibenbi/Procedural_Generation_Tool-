// PerspectiveImage.cs
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class PerspectiveImage : BaseMeshEffect
{
    private PerspectivePanel panel;

    protected override void Awake()
    {
        base.Awake();
        panel = GetComponentInParent<PerspectivePanel>();
    }

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!isActiveAndEnabled) return;

        // 延迟获取，避免 Awake 顺序问题
        if (panel == null)
            panel = GetComponentInParent<PerspectivePanel>();

        if (panel == null || panel.WarpedCorners == null) return;


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

        UIVertex vertex = new UIVertex();
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref vertex, i);

            float worldX = vertex.position.x + offset.x;
            float worldY = vertex.position.y + offset.y;

            float u = (worldX - minX) / sizeX;
            float w = (worldY - minY) / sizeY;

            Vector3 bottom = Vector3.Lerp(wbl, wbr, u);
            Vector3 top = Vector3.Lerp(wtl, wtr, u);
            Vector3 newPos = Vector3.Lerp(bottom, top, w);

            vertex.position = newPos - new Vector3(offset.x, offset.y);
            vh.SetUIVertex(vertex, i);
        }
    }

    // 父物体参数改变时刷新
    public void Refresh()
    {
        graphic.SetVerticesDirty();
    }
}