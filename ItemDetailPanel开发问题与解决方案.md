# ItemDetailPanel 开发问题与解决方案文档

---

## 1. Panel 跟随鼠标的实现原理

### 核心逻辑

`UpdatePanelPositionWithMouse()` 分三步完成定位：

**坐标转换**：将鼠标的屏幕坐标转换为 Canvas 本地坐标（以 Canvas 中心为原点）。
```csharp
RectTransformUtility.ScreenPointToLocalPointInRectangle(
    canvasRect, mousePos, canvas.worldCamera, out Vector2 localPoint)
```

**初始位置计算**：Panel 顶部对齐鼠标位置。
```csharp
Vector2 targetPos = new Vector2(
    localPoint.x,
    localPoint.y - panelHeight * 0.5f
);
```

**边界修正**：分别处理水平和垂直方向的超出情况，确保 Panel 不超出 Canvas 范围。

### Anchor 对该逻辑的影响

`anchoredPosition` 的参考原点取决于 Anchor 设置。只有当 Anchor 聚合在 Canvas 中心 `(0.5, 0.5)` 时，`anchoredPosition` 的坐标系才与 `ScreenPointToLocalPointInRectangle` 返回的 `localPoint` 完全对齐，代码才能正确运行。

**推荐配置：**
```
Anchor Min = (0.5, 0.5)
Anchor Max = (0.5, 0.5)
Pivot      = (0.5, 0.5)
```

---

## 2. UI 内容不随分辨率变化的问题

### 问题现象

Panel 内部子元素的 Anchor 无法在 Inspector 中调整（显示为灰色锁定状态）。

### 根本原因

PanelItem 上挂载了 **VerticalLayoutGroup** 组件，且勾选了 `Control Child Size: Width / Height`。Unity 的 Layout 系统会接管所有子物体的位置和尺寸，导致子物体的 Anchor 被锁死——这是 Unity Layout 系统的正常设计，并非 Bug。

### 解决方案

分辨率适配依赖 **Canvas Scaler**，而非子物体 Anchor。确认根节点 Canvas 的 Canvas Scaler 配置如下：

```
UI Scale Mode      = Scale With Screen Size
Reference Resolution = 设计分辨率（如 1920 x 1080）
Screen Match Mode  = Match Width Or Height
```

Canvas Scaler 设置正确后，整个 Canvas 下的 UI 会随分辨率等比缩放，VerticalLayoutGroup 内的子物体也会随之正确缩放。

---

## 3. Panel 在屏幕左侧时显示异常（内容被压缩）

### 问题现象

鼠标在屏幕左侧时，Panel 被硬挤到左边缘，导致文字换行更频繁、布局局促，与鼠标在右侧时的显示效果不一致。

### 根本原因

原有边界处理逻辑使用硬编码偏移值（如 `mouseLocalPos.x + 100f`），当鼠标本身已经在左侧时，偏移量不足以将 Panel 移离边缘，最终由二次夹值强行推入，导致 Panel 被"压"在边缘。

### 解决方案

改为**检测剩余空间**决定 Panel 显示方向，而非固定偏移：

```csharp
// 水平方向
private Vector2 HandleHorizontalBoundary(Vector2 targetPos, Vector2 mouseLocalPos,
                                          float panelWidth, float canvasWidth)
{
    float halfWidth = panelWidth / 2f;
    float canvasHalfWidth = canvasWidth / 2f;
    float offset = 80f; // 可调整：Panel 与鼠标的间距

    float spaceOnRight = canvasHalfWidth - mouseLocalPos.x;
    float spaceOnLeft  = mouseLocalPos.x + canvasHalfWidth;

    if (spaceOnRight >= panelWidth + offset)
        targetPos.x = mouseLocalPos.x + halfWidth + offset;   // 右侧空间足够
    else if (spaceOnLeft >= panelWidth + offset)
        targetPos.x = mouseLocalPos.x - halfWidth - offset;   // 左侧空间足够
    else
        targetPos.x = 0f;                                      // 两侧都不够，居中

    targetPos.x = Mathf.Clamp(targetPos.x, -canvasHalfWidth + halfWidth, canvasHalfWidth - halfWidth);
    return targetPos;
}

// 垂直方向（同理）
private Vector2 HandleVerticalBoundary(Vector2 targetPos, Vector2 mouseLocalPos,
                                        float panelHeight, float canvasHeight)
{
    float halfHeight = panelHeight / 2f;
    float canvasHalfHeight = canvasHeight / 2f;
    float offset = 30f; // 可调整

    float spaceOnBottom = mouseLocalPos.y + canvasHalfHeight;
    float spaceOnTop    = canvasHalfHeight - mouseLocalPos.y;

    if (spaceOnBottom >= panelHeight + offset)
        targetPos.y = mouseLocalPos.y - halfHeight - offset;
    else if (spaceOnTop >= panelHeight + offset)
        targetPos.y = mouseLocalPos.y + halfHeight + offset;
    else
        targetPos.y = 0f;

    targetPos.y = Mathf.Clamp(targetPos.y, -canvasHalfHeight + halfHeight, canvasHalfHeight - halfHeight);
    return targetPos;
}
```

---

## 4. 鼠标与 Panel 之间的连线实现

### 方案选型

使用一个**旋转拉伸的 Image** 模拟连线，无需第三方库。

### 场景配置

在 PanelMgr 下（与 PanelItem 同级）新建 Image，命名 `LineConnector`：
- Anchor: (0.5, 0.5)
- Raycast Target: 关闭
- Source Image: 使用带菱形装饰的竖向线条图片

### 连线图片的九宫格配置（Sprite Editor）

连线图片为竖向图片，底部带菱形装饰，需要使用 **9-Slice** 避免菱形拉伸变形。

**边界设置原则：**
```
T = 保护顶部尖端（约 34px）
B = 必须完整包住菱形区域，不足会导致菱形在高度缩小时变形（约 110~140px）
L = 线条半宽即可，不需要太大（约 10px）
R = 同 L
```

**常见错误：**
- B 值切在菱形中间 → 调小 LineConnector 高度时菱形被压缩变形，需持续增大 B 直到绿线低于完整菱形
- L/R 值过大（如 44/45）→ 横向可拉伸区域几乎为零，Image 宽度无法正常缩小

**Image 组件配置：**
```
Image Type  = Sliced
Fill Center = ✓
```

### 连线核心逻辑

图片为竖向，因此与横向图片相比有三处关键差异：

| 属性 | 横向图片（旧） | 竖向图片（新） |
|---|---|---|
| `sizeDelta` | `(distance, 2f)` | `(lineThickness, distance)` |
| `pivot` | `(0.5, 0.5)` 中心 | `(0.5, 0f)` 底部（菱形端） |
| `anchoredPosition` | 两点中点 | `targetLocalPoint`（分割线端） |
| 旋转角 | `angle` | `angle - 90f` |

```csharp
private void UpdateLine(Vector2 mouseLocalPoint)
{
    if (_lineRect == null || _dividerRect == null) return;

    float lineThickness = 30f; // 可调：线的视觉宽度
    float mouseOffset = 40f;   // 可调：起点离鼠标的距离

    Vector3[] dividerCorners = new Vector3[4];
    _dividerRect.GetWorldCorners(dividerCorners);
    // corners[0]=左下, [1]=左上, [2]=右上, [3]=右下

    Vector3 leftWorldPoint  = (dividerCorners[0] + dividerCorners[1]) * 0.5f;
    Vector3 rightWorldPoint = (dividerCorners[2] + dividerCorners[3]) * 0.5f;

    RectTransformUtility.ScreenPointToLocalPointInRectangle(
        _canvasRect,
        RectTransformUtility.WorldToScreenPoint(_canvas.worldCamera, leftWorldPoint),
        _canvas.worldCamera, out Vector2 leftLocalPoint);
    RectTransformUtility.ScreenPointToLocalPointInRectangle(
        _canvasRect,
        RectTransformUtility.WorldToScreenPoint(_canvas.worldCamera, rightWorldPoint),
        _canvas.worldCamera, out Vector2 rightLocalPoint);

    RectTransform panelRect = transform.GetComponent<RectTransform>();
    float panelCenterX = panelRect.anchoredPosition.x;
    float lineEndpointOffset = 10f; // 可调：端点内缩量

    // 菱形端（终点）在分割线端点处
    Vector2 targetLocalPoint = mouseLocalPoint.x > panelCenterX
        ? rightLocalPoint + new Vector2(lineEndpointOffset, 0f)
        : leftLocalPoint  - new Vector2(lineEndpointOffset, 0f);

    Vector2 direction = targetLocalPoint - mouseLocalPoint;
    float distance = direction.magnitude;

    // 起点：从鼠标沿方向偏移，不贴着鼠标
    Vector2 startPoint = mouseLocalPoint + direction.normalized * mouseOffset;
    float lineLength = (targetLocalPoint - startPoint).magnitude;

    // pivot=(0.5,0) 底部（菱形端）锚定在 targetLocalPoint
    _lineRect.pivot = new Vector2(0.5f, 0f);
    _lineRect.anchoredPosition = targetLocalPoint;
    _lineRect.sizeDelta = new Vector2(lineThickness, lineLength);

    // 方向从终点指向起点，菱形朝向分割线端
    Vector2 lineDir = startPoint - targetLocalPoint;
    float lineAngle = Mathf.Atan2(lineDir.y, lineDir.x) * Mathf.Rad2Deg;
    _lineRect.localRotation = Quaternion.Euler(0f, 0f, lineAngle - 90f);
}
```

**连线端点规则：**
- 鼠标在 Panel 右侧 → 连分割线**右端点**，向右偏移 `lineEndpointOffset`，菱形朝向右端
- 鼠标在 Panel 左侧 → 连分割线**左端点**，向左偏移 `lineEndpointOffset`，菱形朝向左端
- 起点（尖端）离开鼠标 `mouseOffset` 距离，在鼠标与分割线的连线上

### 连线不显示的排查清单

| 检查项 | 解决方法 |
|---|---|
| Image 的 Color Alpha 为 0 | 将 Alpha 改为 255 |
| LineConnector 被 PanelItem 遮挡 | 在 Hierarchy 中将 LineConnector 移到 PanelItem 下方 |
| sizeDelta 仍是旧的横向逻辑 | 确认 `sizeDelta = new Vector2(lineThickness, distance)` |
| Scale 为 (0,0,0) | 改为 (1,1,1) |
| distance 为 0 | 加 log 确认坐标转换是否正常 |

---

## 5. 元素主题色方案

### 问题背景

希望连线和分割线的颜色与所指向元素图片的颜色一致。

### 方案评估

| 方案 | 描述 | 优点 | 缺点 |
|---|---|---|---|
| **配置表存储颜色** | TbItem 表增加颜色字段 | 精确可控 | 需手动配置118个元素 |
| **图集采样像素** | 运行时从精灵图取色 | 全自动 | 需开启 Read/Write，内存占用翻倍 |
| **分类字典映射** | 按元素分类维护9种颜色 | 简洁，易维护 | 同类颜色相同，无法区分单个元素 |

### 采用方案：分类字典映射

由于颜色方案尚未最终确定，暂时使用按元素分类映射颜色的方式，后续确定后可直接改配置表。

```csharp
// 9种分类颜色（修改此处即可调整所有同类元素颜色）
// 0=非金属 1=碱金属 2=碱土金属 3=过渡金属 4=后过渡金属
// 5=类金属 6=稀有气体 7=镧系 8=锕系
private static readonly Dictionary<int, Color> ElementColors = new Dictionary<int, Color>
{
    {20004, new Color(0.95f, 0.85f, 0.25f)}, // H  - 非金属（黄）
    // ... 完整映射见源码 ...
};

private Color GetElementColor(int itemId)
{
    return ElementColors.TryGetValue(itemId, out Color color) ? color : Color.white;
}
```

### Sprite Atlas 的 Read/Write 问题

尝试从图集精灵图采样像素时，遭遇报错：
```
UnityException: Texture 'sactx-...-UIRaw_Atlas_Element' is not readable
```

**原因**：图片被打包进 Sprite Atlas 后，单张图片的 Read/Write 设置无效，需要在 **Sprite Atlas 资源本身**上开启 Read/Write。但这会导致整张 2048x2048 图集常驻内存两份（CPU + GPU），内存压力较大，不推荐在颜色方案未定时使用。

---

## 6. 颜色应用位置

在 `UpdatePanelPositionWithMouse()` 中，每帧更新时同步设置分割线、图标和连线颜色：

```csharp
Color themeColor = GetElementColor(currentID);
_dividerRect.GetComponent<Image>().color = themeColor;
_iconRect.GetComponent<Image>().color = themeColor;
_lineRect.GetComponent<Image>().color = themeColor;
```

---

## 附：可调整参数汇总

| 参数 | 位置 | 默认值 | 说明 |
|---|---|---|---|
| `offset`（水平） | `HandleHorizontalBoundary` | `80f` | Panel 与鼠标的水平间距 |
| `offset`（垂直） | `HandleVerticalBoundary` | `30f` | Panel 与鼠标的垂直间距 |
| `lineEndpointOffset` | `UpdateLine` | `10f` | 连线终点相对分割线端点的内缩量 |
| `lineThickness` | `UpdateLine` | `30f` | 连线图片的渲染宽度（需匹配菱形视觉宽度） |
| `mouseOffset` | `UpdateLine` | `40f` | 连线起点离鼠标的距离 |
| `ElementColors` 各项 | `ElementColors` 字典 | 见源码 | 各元素分类的主题色 |
| Sprite Border B | Sprite Editor | 110~140 | 九宫格底部边界，必须完整包住菱形 |
