// PerspectivePanel.cs
using UnityEngine;

[ExecuteAlways]
public class PerspectivePanel : MonoBehaviour
{
    [Header("透视参数")]
    public float rotationY = 15f;
    public float fov = 60f;

    public Vector3[] WarpedCorners { get; private set; } = new Vector3[4];
    public Rect PanelRect { get; private set; }

    private RectTransform rectTransform;
    private Canvas canvas;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        canvas = GetComponentInParent<Canvas>();
        Calculate();
    }

    void OnRectTransformDimensionsChange() => Calculate();

    void OnValidate()
    {
        if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        Calculate();
        foreach (var tmp in GetComponentsInChildren<PerspectiveTMP>()) tmp.Refresh();
        foreach (var img in GetComponentsInChildren<PerspectiveImage>()) img.Refresh();
    }

    public void Calculate()
    {
        if (rectTransform == null) return;

        Rect rect = rectTransform.rect;
        PanelRect = rect;

        // Canvas 高度换算焦距（保证和本地坐标单位一致）
        float canvasHeight = canvas != null
            ? canvas.GetComponent<RectTransform>().rect.height
            : Screen.height;
        float f = (canvasHeight * 0.5f) / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);

        // Panel pivot 相对于 Canvas 中心的偏移（世界单位）
        float ox = 0, oy = 0;
        if (canvas != null)
        {
            Vector3 canvasCenter = canvas.transform.position;
            ox = transform.position.x - canvasCenter.x;
            oy = transform.position.y - canvasCenter.y;
        }

        // 以 rect 视觉中心为旋转基准
        Vector3 center = new Vector3(rect.center.x, rect.center.y, 0);
        Vector3[] offsets = new Vector3[]
        {
            new Vector3(rect.xMin, rect.yMin, 0) - center, // 左下
            new Vector3(rect.xMin, rect.yMax, 0) - center, // 左上
            new Vector3(rect.xMax, rect.yMax, 0) - center, // 右上
            new Vector3(rect.xMax, rect.yMin, 0) - center, // 右下
        };

        Quaternion rot = Quaternion.AngleAxis(rotationY, Vector3.up);

        for (int i = 0; i < 4; i++)
        {
            Vector3 ro = rot * offsets[i];
            float denom = f + ro.z;

            // 完整透视投影公式，包含屏幕偏移修正项 (ox * ro.z)
            WarpedCorners[i] = new Vector3(
                (rect.center.x * f + ro.x * f - ox * ro.z) / denom,
                (rect.center.y * f + ro.y * f - oy * ro.z) / denom,
                0
            );
        }
    }

    void OnDrawGizmos()
    {
        if (WarpedCorners == null) return;
        Gizmos.color = Color.green;
        Vector3 pos = transform.position;
        Gizmos.DrawLine(pos + WarpedCorners[0], pos + WarpedCorners[1]);
        Gizmos.DrawLine(pos + WarpedCorners[1], pos + WarpedCorners[2]);
        Gizmos.DrawLine(pos + WarpedCorners[2], pos + WarpedCorners[3]);
        Gizmos.DrawLine(pos + WarpedCorners[3], pos + WarpedCorners[0]);
    }
}