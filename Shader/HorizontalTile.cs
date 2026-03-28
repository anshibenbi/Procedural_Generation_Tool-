using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[RequireComponent(typeof(RawImage))]
public class HorizontalTile : MonoBehaviour
{
    [Range(0f, 2f)]
    public float spacing = 1f; // 1 = 无间距，< 1 = 有间距，> 1 = 图片之间重叠

    private RawImage _raw;
    private float _lastWidth;
    private float _lastSpacing;

    void OnEnable()
    {
        _raw = GetComponent<RawImage>();
        UpdateTiling();
    }

    void Update()
    {
        float currentWidth = _raw.rectTransform.rect.width;
        if (!Mathf.Approximately(currentWidth, _lastWidth) ||
            !Mathf.Approximately(spacing, _lastSpacing))
        {
            _lastWidth = currentWidth;
            _lastSpacing = spacing;
            UpdateTiling();
        }
    }

    void UpdateTiling()
    {
        if (_raw == null || _raw.texture == null || _raw.material == null) return;

        var rect = _raw.rectTransform.rect;
        var tex = _raw.texture;

        float texAspect = (float)tex.width / tex.height;
        float rectAspect = rect.width / rect.height;

        // spacing < 1 时每个重复单元之间留有空白
        float tilingX = (rectAspect / texAspect) * spacing;

        _raw.material.SetTextureScale("_MainTex", new Vector2(tilingX, 1f));
    }
}