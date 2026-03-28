# AI NPC 开发总结

> 技术栈：Unity + LLamaSharp + Qwen3-8B + 本地推理

------

## 一、架构设计

### 问题

如何让本地 7B 模型同时满足"有趣的游戏回复"和"精确触发游戏逻辑"两个需求。

### 解决方案

**结构化输出分区**：强制模型按固定格式输出，将叙述内容和触发指令物理隔离。

```
narrative: [叙述内容]
actions: [触发符号，无触发则留空]
```

### 知识点

- 7B 模型对 Prompt 格式极其敏感，结构化输出比自由输出可靠得多
- Few-shot 示例（在 Prompt 里给出2-3个完整输入输出示例）可显著提升格式遵循率
- 创意生成（有趣）和规则遵循（精确）是相反的需求，需要通过格式分区解耦

------

## 二、RAG 知识库集成

### 问题

游戏百科文字量达几千到上万字，无法全部塞进 System Prompt（7B 模型上下文长度有限，且内容过多会"迷失在中间"）。

### 解决方案

**轻量 RAG（关键词检索）**：每次只把命中的条目塞进 Prompt，而不是整本百科。

```
玩家输入 → 关键词匹配百科条目 → 只把命中内容拼入 Prompt → 模型回答
def retrieve(user_input):
    results = []
    for key, content in wiki.items():
        if key in user_input:
            results.append(f"{key}：{content}")
    return "\n".join(results)
```

### 知识点

- RAG（检索增强生成）本质是"先查资料再问模型"，模型本身不变
- 知识库从头到尾不进入模型，只在 Prompt 组装阶段被引用
- 条目需要维护 `aliases`（别名）字段，因为玩家说法千变万化
- 几千字用关键词检索足够，上万字以上才需要向量检索

------

## 三、分类器设计

### 问题

同一个入口需要处理两种完全不同的请求：游戏操作 vs 百科查询。

### 解决方案

**规则 + 模型兜底**的分类层：

```python
def classify(user_input):
    # 高置信度直接判断
    if any(kw in user_input for kw in ["是什么", "介绍", "解释"]):
        return "wiki"
    if any(kw in user_input for kw in ["我要", "我想", "攻击"]):
        return "game"
    # 模糊的交给模型，temperature=0 保证确定性
    return classify_by_model(user_input)
```

### 知识点

- 分类是独立的极简任务，用单独的 Prompt 让模型只输出 `game` 或 `wiki`
- Temperature 设 0 让分类结果更确定
- 规则处理明确情况，模型处理模糊情况，两者结合最稳

------

## 四、从 Ollama HTTP 迁移到 LLamaSharp

### 问题

游戏打包后必须在玩家机器上预装 Ollama 才能使用 AI 功能，不符合客户端产品要求。

### 解决方案

用 **LLamaSharp**（llama.cpp 的 C# 绑定）替换 Ollama HTTP 通信，模型直接在游戏进程内运行。

```
迁移前：游戏 → HTTP → Ollama进程 → 模型
迁移后：游戏 → LLamaSharp → 模型（同一进程）
```

**关键原则**：只换 `OllamaManager` 内部实现，对外接口签名 `ChatAsync` / `ChatStreamAsync` 保持完全一致，调用方零改动。

### 知识点

- `StreamingAssets` 是 Unity 唯一会原样保留文件的目录，`.gguf` 模型必须放这里
- `BuildPayload` 和 `EscapeJson` 是专门为 HTTP JSON 服务的工具方法，迁移后可删除
- `PostToMainThread` 仍然需要保留，LLamaSharp 推理也在后台线程
- 模型加载需要几秒到几十秒，必须异步加载，UI 需要等 `IsReady` 为 true 再开放功能
- 类名应命名为 `ChatMessage` 而非 `OllamaMessage`，避免与底层实现耦合

------

## 五、GPU 自动检测

### 问题

不同玩家机器硬件配置不同，需要自动判断使用 GPU 还是 CPU 推理。

### 解决方案

用 Unity 内置 API 检测显卡信息，根据显存大小决定 GPU 卸载层数：

```csharp
private int GetOptimalGpuLayers()
{
    string gpuName = SystemInfo.graphicsDeviceName.ToLower();
    int vramMB = SystemInfo.graphicsMemorySize;

    bool isNvidia = gpuName.Contains("nvidia") || gpuName.Contains("rtx");
    bool isAppleSilicon = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Metal 
                          && gpuName.Contains("apple");

    if (isAppleSilicon) return 99;  // 统一内存全部卸载
    if (isNvidia && vramMB >= 8000) return 35;
    if (isNvidia && vramMB >= 6000) return 20;
    if (isNvidia && vramMB >= 4000) return 10;
    return 0;  // CPU 推理
}
```

### 知识点

- 7B 模型约有 32 层，`GpuLayerCount` 控制卸载到 GPU 的层数
- 设 99 是常见做法，LLamaSharp 会自动按实际层数上限处理
- Apple Silicon 使用统一内存，全部卸载效率最高
- 没有安装 CUDA Toolkit 的机器即使有 Nvidia 显卡也无法使用 CUDA backend

------

## 六、ChatSession 无法正确传递上下文

### 问题

使用 `ChatSession` + `InteractiveExecutor` 时，System Prompt 和用户输入没有正确传给模型，模型自称"通义千问"而非角色名，且输出与玩家输入无关。

### 解决方案

放弃 `ChatSession`，**手动拼接 Qwen 的 ChatML 格式 Prompt**，直接用 `InstructExecutor.InferAsync`：

```csharp
private string BuildQwenPrompt(List<OllamaMessage> messages)
{
    var sb = new StringBuilder();
    for (int i = 0; i < messages.Count; i++)
    {
        var msg = messages[i];
        switch (msg.role)
        {
            case "system":
                sb.Append($"<|im_start|>system\n{msg.content}<|im_end|>\n");
                break;
            case "user":
                // 最后一条加 /no_think 关闭思维链
                string content = (i == messages.Count - 1)
                    ? $"{msg.content} /no_think"
                    : msg.content;
                sb.Append($"<|im_start|>user\n{content}<|im_end|>\n");
                break;
            case "assistant":
                sb.Append($"<|im_start|>assistant\n{msg.content}<|im_end|>\n");
                break;
        }
    }
    sb.Append("<|im_start|>assistant\n");  // 引导模型开始回复
    return sb.ToString();
}
```

### 知识点

- 每个模型有自己的对话模板（ChatML），必须按对应格式拼接才能正确识别角色
- Qwen 系列使用 `<|im_start|>` / `<|im_end|>` 作为分隔符
- `InteractiveExecutor` 是有状态的，适合单次长对话；每次传完整历史时应用 `InstructExecutor`
- 每次调用用 `using var context` 创建新上下文，用完自动释放，避免历史重复累积

------

## 七、Qwen3 思维链输出

### 问题

Qwen3 模型默认开启思维链，会输出大段 `<think>...</think>` 内容，导致 `MaxTokens` 耗尽时实际回复为空。

### 解决方案

两种方式关闭思维链：

**方式一（推荐）**：在最后一条 user 消息末尾加 `/no_think`

```csharp
$"{msg.content} /no_think"
```

**方式二**：代码里清理掉 `<think>` 块

```csharp
int thinkStart = response.IndexOf("<think>");
int thinkEnd = response.IndexOf("</think>");
if (thinkStart >= 0 && thinkEnd >= 0)
    response = response[(thinkEnd + "</think>".Length)..];
```

### 知识点

- Qwen3 有两种模式：thinking（带推理过程）和 non-thinking（直接回复）
- `/no_think` 是 Qwen3 官方支持的指令，写在 user 消息里即可切换模式
- 思维链会消耗大量 token，游戏对话场景性价比很低，建议关闭

------

## 八、模型输出携带角色扮演前缀

### 问题

模型输出包含 `User:` / `AI:` 前缀，且自我续写对话，导致回复内容重复或格式混乱。

### 解决方案

**输出清理**：返回前统一清理多余内容：

```csharp
private string CleanResponse(string response)
{
    // 去掉思维链
    int thinkStart = response.IndexOf("<think>");
    int thinkEnd = response.IndexOf("</think>");
    if (thinkStart >= 0 && thinkEnd >= 0)
        response = response[(thinkEnd + "</think>".Length)..];

    // 去掉停止符
    response = response
        .Replace("<|im_end|>", "")
        .Replace("<|im_start|>", "")
        .Replace("<|endoftext|>", "")
        .Trim();

    // 截断自我续写（要求有换行前缀，避免误截正文）
    var cutoffs = new[] { "\nUser:", "\nAI:", "\n用户：", "\nAI：" };
    foreach (var cutoff in cutoffs)
    {
        int idx = response.IndexOf(cutoff);
        if (idx >= 0)
            response = response[..idx];
    }

    return response.Trim();
}
```

同时确保存回历史的 `assistant` 消息是清理后的版本，否则下一轮模型会学着输出同样的前缀。

### 知识点

- AntiPrompts 只能截断生成，无法去掉已生成的内容，需要后处理清理
- 截断词不能省略换行前缀（`\n`），否则会误截正文中出现的正常词汇
- RepeatPenalty 调高（1.3 左右）可以抑制重复输出

------

## 九、游戏命令参数解析失败

### 问题

模型输出 `%cmd_AutoVineGrow#0.35\n>` 末尾带有换行和 `>` 符号，`Single.Parse` 解析失败。

### 解决方案

用字符过滤替代 `Trim()`，只保留数字和小数点：

```csharp
string rawParam = methodInfo[1];
string cleanParam = new string(
    rawParam.Where(c => char.IsDigit(c) || c == '.').ToArray()
);
if (float.TryParse(cleanParam, 
    System.Globalization.NumberStyles.Float,
    System.Globalization.CultureInfo.InvariantCulture, 
    out float parsedFloat))
{
    m.Invoke(TerrainChange.Instance, new object[] { parsedFloat, false });
}
```

### 知识点

- `Trim()` 只能去掉空白字符，`>` 不是空白字符，需要更彻底的清理
- 用 `InvariantCulture` 避免不同系统小数点格式不一致（部分系统用逗号作小数点）
- 用 `TryParse` 替代 `Parse` + `try/catch`，代码更简洁

------

## 十、打包后原生库加载失败

### 问题

编辑器下正常，打包后报 `The native library cannot be correctly loaded`。

### 原因分析

1. LLamaSharp 依赖的原生 dll（`ggml.dll`、`ggml-cuda.dll` 等）没有被正确包含进打包目录
2. 安装了 CUDA backend 但机器上没有安装 CUDA Toolkit

### 解决方案

**短期**：在 Unity Inspector 里禁用 CUDA backend dll，只启用 CPU backend dll，强制走 CPU 推理：

- 禁用：`LLamaSharp.Backend.Cuda12.Windows` 包里的所有 dll
- 启用：`LLamaSharp.Backend.Cpu` 包里 `win-x64/native/avx2/` 下的所有 dll
- 代码里 `GetOptimalGpuLayers()` 返回 `0`

**长期**：需要支持 CUDA 时，要求玩家安装 CUDA Toolkit 12，或将 CUDA 运行时 dll 一起打包（`cublas64_12.dll`、`cudart64_12.dll` 等）。

### 知识点

- Unity 编辑器会自动搜索 Packages 目录，打包后路径规则不同
- LLamaSharp 的 native dll 必须在 Inspector 里正确设置平台（Windows x86_64）才会被打包
- CUDA backend 依赖 CUDA Toolkit，没有安装则无法使用，需要回退 CPU backend
- 可用 PowerShell 的 `Get-ChildItem` 替代 Windows 下的 `dumpbin` 查找文件

------

## 十一、模型文件管理

### 问题

- `.gguf` 文件 4-5GB，Git 无法正常上传，队友拉下来的文件不完整
- 每次打包都把模型文件打进包里，导致打包时间过长

### 解决方案

**Git 大文件**：使用 Git LFS 管理 `.gguf` 文件

```bash
git lfs install
git lfs track "*.gguf"
git add .gitattributes
git commit -m "track gguf with lfs"
```

**打包体积**：模型文件移出 `StreamingAssets`，改用 `Application.persistentDataPath`，首次启动时下载：

```csharp
var modelPath = System.IO.Path.Combine(
    Application.persistentDataPath,
    "models",
    modelFileName
);

if (!System.IO.File.Exists(modelPath))
{
    // 通知 UI 显示下载界面
    return;
}
```

### 知识点

- Git 默认不支持超过 100MB 的文件，需要 Git LFS 处理大文件
- `Application.streamingAssetsPath` 打包时只读，适合只读资源
- `Application.persistentDataPath` 打包后可读写，适合运行时下载的文件
  - Windows 路径：`C:\Users\用户名\AppData\LocalLow\公司名\游戏名\`
- 模型文件应该作为独立资源分发，而不是打进游戏包里

------

## 附：核心文件结构

```
Assets/
  GameScripts/HotFix/GameLogic/AINPC/
    OllamaManager.cs     # 模型推理管理器
    EntityNpc.cs         # NPC 实体，处理对话请求和命令解析
  GameScripts/HotFix/GameLogic/UI/
    UIChatForm.cs        # 聊天 UI
  StreamingAssets/
    models/              # 开发阶段模型放这里（打包前移走）
  Packages/
    LLamaSharp.0.26.0/
    LLamaSharp.Backend.Cpu.0.26.0/
    LLamaSharp.Backend.Cuda12.Windows.0.26.0/
```

## 附：推理参数参考

| 参数          | 推荐值 | 说明                                   |
| ------------- | ------ | -------------------------------------- |
| Temperature   | 0.8    | 保留创意感，不要太高否则输出乱         |
| RepeatPenalty | 1.3    | 抑制重复输出                           |
| MaxTokens     | 512    | 关闭思维链后够用，开启思维链需要 2048+ |
| ContextSize   | 4096   | 一般对话够用                           |
| GpuLayerCount | 0~35   | 根据显存自动检测                       |