// PerspectivePanel.cs
using UnityEngine;

public class PerspectivePanel : MonoBehaviour
{
    [Header("透视参数")]
    public float rotationY = 15f;       // 绕Y轴旋转角度
    public float focalLength = 800f;    // 焦距，越小透视越强

    // 变形后的四角坐标（本地空间）
    // 顺序：左下 左上 右上 右下
    public Vector3[] WarpedCorners { get; private set; } = new Vector3[4];

    // 父物体的原始尺寸（用于子物体算 u/v）
    public Rect PanelRect { get; private set; }

    private RectTransform rectTransform;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        Calculate();
    }

    // Panel 尺寸变化时重算
    void OnRectTransformDimensionsChange()
    {
        Calculate();
    }

    // 编辑器调参时重算
    void OnValidate()
    {
        if (rectTransform == null)
            rectTransform = GetComponent<RectTransform>();
        Calculate();

        // 通知所有子物体刷新
        foreach (var tmp in GetComponentsInChildren<PerspectiveTMP>())
            tmp.Refresh();
        foreach (var img in GetComponentsInChildren<PerspectiveImage>())
            img.Refresh();
    }

    public void Calculate()
    {
        Rect rect = rectTransform.rect;
        PanelRect = rect;

        float W = rect.width;
        float H = rect.height;
        float cx = rect.center.x;  // pivot.x=0.5 时是 0
        float cy = rect.center.y;  // pivot.y=0 时是 H/2  ← 关键

        float θ = rotationY * Mathf.Deg2Rad;
        float f = focalLength;

        float zLeft = (W * 0.5f) * Mathf.Sin(θ);
        float zRight = -(W * 0.5f) * Mathf.Sin(θ);

        float scaleL = f / (f + zLeft);
        float scaleR = f / (f + zRight);

        float xLeft = cx - (W * 0.5f) * Mathf.Cos(θ) * scaleL;
        float xRight = cx + (W * 0.5f) * Mathf.Cos(θ) * scaleR;

        WarpedCorners[0] = new Vector3(xLeft, cy - H * 0.5f * scaleL);  // 左下
        WarpedCorners[1] = new Vector3(xLeft, cy + H * 0.5f * scaleL);  // 左上
        WarpedCorners[2] = new Vector3(xRight, cy + H * 0.5f * scaleR);  // 右上
        WarpedCorners[3] = new Vector3(xRight, cy - H * 0.5f * scaleR);  // 右下
    }

    // 编辑器下可视化四角
    void OnDrawGizmos()
    {
        if (WarpedCorners == null) return;
        Gizmos.color = Color.green;
        Vector3 pos = transform.position;
        Gizmos.DrawLine(pos + WarpedCorners[0], pos + WarpedCorners[1]); // 左边
        Gizmos.DrawLine(pos + WarpedCorners[1], pos + WarpedCorners[2]); // 上边
        Gizmos.DrawLine(pos + WarpedCorners[2], pos + WarpedCorners[3]); // 右边
        Gizmos.DrawLine(pos + WarpedCorners[3], pos + WarpedCorners[0]); // 下边
    }
}