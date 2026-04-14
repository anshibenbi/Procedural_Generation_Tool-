# Unity UI Layout 核心知识点

---

## 1. Content Size Fitter

### 作用

Content Size Fitter 挂载在某个物体上，根据内容自动调整**自身**的宽或高，不影响子元素。

### 参数说明

| 参数 | 选项 | 说明 |
|---|---|---|
| Horizontal Fit | Unconstrained | 不自动调整宽度（默认，手动控制） |
| | Preferred Size | 宽度收缩到内容的 preferred width |
| | Min Size | 宽度收缩到内容的 min width |
| Vertical Fit | 同上 | 同理，控制高度 |

### 常见使用场景

- 聊天气泡：宽高随文字内容自动伸缩
- 文本容器：高度随行数增加
- 下拉菜单：高度随选项数量变化

### 注意事项

Content Size Fitter 查询的是子元素的 **Preferred Size**，而不是子元素 RectTransform 的实际尺寸。两者在某些情况下不同（见第 3 节）。

---

## 2. Horizontal / Vertical Layout Group

### 作用

将子元素按横向或纵向自动排列，并可控制子元素的宽高分配。

### 关键参数

| 参数 | 说明 |
|---|---|
| Padding | 内边距，影响子元素与父级边缘的距离 |
| Spacing | 子元素之间的间距 |
| Control Child Size Width/Height | 勾选后，Layout Group 接管子元素的宽/高，子元素自身设置的值无效 |
| Child Force Expand Width/Height | 勾选后，子元素强制拉伸填满父级剩余空间 |

### Content Size Fitter + Layout Group 的计算规则

当父级同时挂有 Content Size Fitter 和 Layout Group 时，父级宽度计算为：

```
父级宽度 = max(Layout Group 算出的宽, 所有 ILayoutElement 报告的 Preferred Width)
```

Layout Group 算出的宽：

```
Padding.Left + 所有参与子元素的 Preferred Width 之和 + Padding.Right
```

---

## 3. ILayoutElement 与 Preferred Size

Unity 中凡是实现了 `ILayoutElement` 接口的组件，都会向 Layout 系统报告自己的 Preferred Size，主要包括：

- **TextMeshPro**：报告文字不换行时所需的理想宽度
- **Image（Simple）**：报告 Sprite 原图尺寸（除以 PPU）
- **Image（Sliced）**：报告 `Border.L + Border.R`（水平），`Border.T + Border.B`（垂直）

### Sliced Image 的特殊行为

Sliced（九宫格）图片的 Preferred Width = `Border.L + Border.R`。

这是 Unity 的**保护机制**：九宫格左右两侧是不可拉伸的固定区域，宽度必须至少等于 L+R，否则两侧会重叠导致显示异常。

因此，当父级挂有 Content Size Fitter 时，Sliced Image 的 Border 值会直接决定父级的最小宽度。

---

## 4. 九宫格（9-Slice）原理

将一张图片用两条横线、两条竖线切成 9 块：

```
┌──────┬──────────────┬──────┐  ← T（Top Border）
│ 左上 │   上边缘     │ 右上 │
│ 不动 │ 只横向拉伸   │ 不动 │
├──────┼──────────────┤──────┤
│      │              │      │
│ 左边 │   中间       │ 右边 │
│ 只纵 │ 双向拉伸     │ 只纵 │
│ 向拉 │              │ 向拉 │
├──────┼──────────────┤──────┤
│ 左下 │   下边缘     │ 右下 │
│ 不动 │ 只横向拉伸   │ 不动 │
└──────┴──────────────┴──────┘  ← B（Bottom Border）
↑                              ↑
L（Left Border）          R（Right Border）
```

| 区域 | 拉伸方式 |
|---|---|
| 四个角 | 不拉伸，保持原始像素 |
| 四条边 | 只在一个方向拉伸 |
| 中间 | 双向拉伸 |

### Border 的单位

Border 值的单位是**像素（px）**，对应 Sprite 原图的像素坐标。在 Unity 的 Sprite Editor 中设置。

### 如何和美术沟通

> "这张图是 512×256 的，左边切线设在距左边 X px，右边切线设在距右边 Y px，左上角的装饰图形需要包含在不拉伸区域内。"

---

## 5. Pixels Per Unit Multiplier

### 概念

Image 组件上的 Pixels Per Unit Multiplier 会影响九宫格 Border 在屏幕上的**实际渲染尺寸**：

```
Border 实际渲染像素 = Border 原始像素 ÷ Multiplier
```

| Multiplier | 效果 |
|---|---|
| 1（默认） | Border 正常大小渲染 |
| 大于 1 | Border 渲染变小，中间可拉伸区域变多 |
| 小于 1 | Border 渲染变大 |

### 注意

Pixels Per Unit Multiplier 只影响**渲染尺寸**，不影响 Layout 系统使用的 Preferred Size（Layout 系统始终使用原始 Border 值）。

用大 Multiplier 来修正显示问题是临时方案，治本方法是重新设计 Sprite 的 Border 值。

---

## 6. Layout Element（布局元素覆盖）

在物体上添加 Layout Element 组件，可以**手动覆盖**该物体向 Layout 系统报告的 Preferred Size，优先级高于 Image、TMP 等组件的默认报告值。

| 参数 | 说明 |
|---|---|
| Ignore Layout | 勾选后，该物体完全不参与 Layout 计算 |
| Min Width/Height | 覆盖最小尺寸 |
| Preferred Width/Height | 覆盖理想尺寸 |
| Flexible Width/Height | 设置弹性权重，值越大占据剩余空间越多 |

### 常用场景

- 让某个子元素占据剩余所有空间：设置 `Flexible Width = 1`
- 固定某个子元素的宽度：设置 `Preferred Width = 固定值`
- 让某个装饰元素不影响布局：勾选 `Ignore Layout`

---

## 7. Pivot（轴心点）

### 作用

Pivot 是物体自身的参考原点，取值范围 0~1（归一化坐标）。物体的**位置、旋转、缩放**都以 Pivot 为基准，Content Size Fitter 扩张时也以 Pivot 为基准向外延伸。

### X 轴方向

| Pivot X | 扩张方向 |
|---|---|
| 0 | 以左边为基准，只向右扩张 |
| 0.5 | 以中心为基准，向两边扩张 |
| 1 | 以右边为基准，只向左扩张 |

### Y 轴方向

| Pivot Y | 扩张方向 |
|---|---|
| 0 | 以底部为基准，向上扩张 |
| 0.5 | 以中心为基准，向上下扩张 |
| 1 | 以顶部为基准，向下扩张 |

> ⚠️ Unity 中 Y=0 是底部，Y=1 是顶部，与屏幕像素坐标方向相反。

### Pivot 与 Pos X/Y 的关系

Inspector 中显示的 Pos X/Y，是 **Pivot 点**到**Anchor 点**的距离，而不是物体边缘的位置。

---

## 8. Anchors（锚点）

### 作用

Anchors 定义物体"挂靠"到父级的哪个位置，由 Min 和 Max 两个点组成（各有 X/Y 两个值，范围 0~1）。

### 点锚点（Min = Max）

当 Min 和 Max 相同时，物体固定在父级的某个相对位置，**不随父级尺寸变化而拉伸**，只跟随父级整体移动。

| 常用设置 | 效果 |
|---|---|
| Min(0.5, 0.5) Max(0.5, 0.5) | 固定在父级中心 |
| Min(0, 0.5) Max(0, 0.5) | 固定在父级左侧中间 |
| Min(1, 0.5) Max(1, 0.5) | 固定在父级右侧中间 |
| Min(0, 0) Max(0, 0) | 固定在父级左下角 |

### 拉伸锚点（Min ≠ Max）

当 Min 和 Max 不同时，物体会随父级尺寸变化**自动拉伸**，Inspector 中的 Width/Height 变为 Left/Right（或 Top/Bottom）边距。

| 常用设置 | 效果 |
|---|---|
| Min X=0, Max X=1 | 横向拉伸，填满父级，Left/Right 控制两侧边距 |
| Min Y=0, Max Y=1 | 纵向拉伸，填满父级，Top/Bottom 控制上下边距 |
| Min(0,0), Max(1,1) | 四向拉伸，完全跟随父级，四边距固定 |

### Pivot 与 Anchors 的配合

| 需求 | Pivot | Anchor |
|---|---|---|
| 只向右扩张 | X = 0 | Min X = 0, Max X = 0 |
| 只向左扩张 | X = 1 | Min X = 1, Max X = 1 |
| 贴右下角 | X = 1, Y = 0 | Min(1, 0), Max(1, 0) |
| 横向撑满父级 | X = 0.5 | Min X = 0, Max X = 1 |

---

## 快速排查清单

遇到 UI 布局异常时，按以下顺序检查：

1. **Content Size Fitter** 的 Horizontal/Vertical Fit 是否设置了不需要的 Preferred Size？
2. **Image 的 Border**（Sliced 模式下）是否过大，导致 Preferred Width 锁死父级尺寸？
3. **Pixels Per Unit Multiplier** 是否被调大以掩盖 Border 过大的问题？
4. **Pivot** 是否设置正确，扩张方向是否符合预期？
5. **Anchor** 是否与 Pivot 配合，保证位置不会随父级变化偏移？
6. **Layout Element** 的 Ignore Layout 是否意外勾选，导致子元素不参与布局计算？
