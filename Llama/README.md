# LlamaManager

本地大语言模型推理封装库，基于 [LLamaSharp](https://github.com/SciSharp/LLamaSharp) 构建。

纯 C#，无任何 Unity 或平台依赖，可运行在 Unity、WPF、MAUI、控制台等任意 .NET 环境中。

---

## 文件结构

```
LlamaConfig.cs      // 配置数据类
LlamaMessage.cs     // 消息数据类
LlamaManager.cs     // 核心推理类
```

---

## 依赖

NuGet 包：

```
LLamaSharp
LLamaSharp.Backend.Cpu          // CPU 推理（必选其一）
LLamaSharp.Backend.Cuda12       // Nvidia GPU 推理（可选）
```

模型格式：`.gguf`（推荐 Q4_K_M 量化版本）

---

## 快速开始

### 1. 初始化

```csharp
var config = new LlamaConfig
{
    ModelPath    = "/path/to/Qwen3-8B-Q4_K_M.gguf",
    ContextSize  = 4096,
    GpuLayerCount = 0,       // 无显卡设 0，Nvidia 独显可设 20-35
    MaxTokens    = 512,
    Temperature  = 0.8f,
    RepeatPenalty = 1.3f
};

var manager = new LlamaManager(
    config,
    onLog:   msg => Console.WriteLine(msg),
    onError: msg => Console.Error.WriteLine(msg)
);

await manager.InitializeAsync();
```

### 2. 非流式调用

```csharp
var messages = new List<LlamaMessage>
{
    new("system", "你是一个奇幻世界的叙述者，语言生动有趣。"),
    new("user",   "我要控制这片大地")
};

string reply = await manager.ChatAsync(messages);
Console.WriteLine(reply);
```

### 3. 流式调用

```csharp
await manager.ChatStreamAsync(
    messages,
    onChunk:    token => Console.Write(token),   // 每生成一个 token 触发
    onComplete: full  => Console.WriteLine(),    // 全部完成后触发，携带完整文本
    onError:    err   => Console.Error.WriteLine(err)
);
```

### 4. 释放资源

```csharp
manager.Dispose();
// 或使用 using
using var manager = new LlamaManager(config);
```

---

## 在 Unity 中使用

LlamaManager 本身不依赖 Unity，建议用一个薄的 MonoBehaviour 包装层负责生命周期管理和线程切换。

```csharp
public class AIServiceBridge : MonoBehaviour
{
    public static LlamaManager AI { get; private set; }

    async void Start()
    {
        var config = new LlamaConfig
        {
            ModelPath = Path.Combine(
                Application.streamingAssetsPath,
                "models",
                "Qwen3-8B-Q4_K_M.gguf"
            )
        };

        AI = new LlamaManager(
            config,
            onLog:   msg => Debug.Log(msg),
            onError: msg => Debug.LogError(msg)
        );

        await AI.InitializeAsync();
    }

    void OnDestroy() => AI?.Dispose();
}
```

流式调用时，回调在推理线程触发，需要自行切回主线程再更新 UI：

```csharp
await AIServiceBridge.AI.ChatStreamAsync(
    messages,
    onChunk: token =>
    {
        // 切回主线程
        UnityMainThreadDispatcher.Instance.Enqueue(() =>
        {
            dialogueText.text += token;
        });
    },
    onComplete: full =>
    {
        // 解析 narrative 和 actions
    }
);
```

> 模型文件放在 `Assets/StreamingAssets/models/` 目录下，Unity 打包时会自动包含。

---

## 配置项说明

| 属性 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `ModelPath` | string | 必填 | 模型文件完整路径 |
| `ContextSize` | uint | 4096 | 上下文窗口大小 |
| `GpuLayerCount` | int | 0 | 卸载到 GPU 的层数，0 为纯 CPU |
| `MaxTokens` | int | 512 | 单次推理最大 token 数 |
| `Temperature` | float | 0.8 | 输出随机性，越低越保守 |
| `RepeatPenalty` | float | 1.3 | 重复惩罚，抑制重复输出 |

---

## 注意事项

**模型加载**
模型冷启动需要数秒，建议在应用启动阶段异步加载，通过 `IsReady` 属性判断是否可用。

**线程安全**
`ChatAsync` 和 `ChatStreamAsync` 不是线程安全的，同一时间只应有一个推理任务在运行。流式回调在推理线程上触发，调用方负责切换到 UI 线程。

**资源释放**
`LlamaManager` 实现了 `IDisposable`，使用完毕后调用 `Dispose()` 或通过 `using` 管理生命周期。

**模型格式**
当前 Prompt 格式针对 Qwen3 的 ChatML 格式实现，使用其他模型系列（Llama、Mistral 等）需要修改 `BuildQwenPrompt()` 方法中的模板。
