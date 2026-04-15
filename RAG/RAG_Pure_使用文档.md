# RAG 系统使用文档（纯 C# 版）

> 无任何 Unity 依赖，可在任意 .NET 6+ / .NET Standard 2.1 环境中使用  
> 不负责发送给模型，只负责检索——把上下文返回给调用方自行处理

---

## 目录

1. [文件清单](#1-文件清单)
2. [核心概念](#2-核心概念)
3. [快速开始](#3-快速开始)
4. [RAGConfig 配置项说明](#4-ragconfig-配置项说明)
5. [初始化与加载优先级](#5-初始化与加载优先级)
6. [检索接口](#6-检索接口)
7. [在对话系统中接入](#7-在对话系统中接入)
8. [预构建向量库](#8-预构建向量库)
9. [日志系统](#9-日志系统)
10. [注意事项](#10-注意事项)
11. [常见问题](#11-常见问题)

---

## 1. 文件清单

```
RAG_Pure/
├── RAGConfig.cs                   # 配置类，所有参数从这里传入
├── RAGManager.cs                  # 主入口，对外暴露检索接口
├── VectorStore.cs                 # 向量存储 + 余弦检索 + JSON 持久化
├── DocumentLoader_TextSplitter.cs # 文档加载 + 文本切块
├── TFIDFEmbeddingService.cs       # TF-IDF 向量化实现
├── IEmbeddingService.cs           # Embedding 接口（方便替换实现）
└── DocumentChunk.cs               # 数据模型
```

这 7 个文件是完整的 RAG 系统，除 `System.Text.Json`（.NET 内置）外无任何第三方依赖。

---

## 2. 核心概念

**RAG 在这里只做一件事：给你的用户输入找到相关的文档片段。**

```
用户说了什么
      │
      ▼
RAGManager.RetrieveContext(userInput)
      │
      ├─ 有相关内容 → 返回拼好的上下文字符串
      │
      └─ 没有相关内容 → 返回空字符串 ""
```

拿到 context 之后怎么处理、怎么拼 prompt、发给哪个模型，完全由调用方决定，RAG 不参与。

---

## 3. 快速开始

### Step 1：准备知识库目录

在项目中创建一个目录，放入 `.txt` 文档（UTF-8 编码）：

```
MyProject/
└── KnowledgeBase/
    ├── 产品手册.txt
    ├── FAQ.txt
    └── 世界观设定.txt
```

### Step 2：创建并初始化 RAGManager

```csharp
using System;
using System.IO;
using System.Threading.Tasks;

// 配置
var config = new RAGConfig
{
    KnowledgeBaseDir  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KnowledgeBase"),
    PrebuiltStorePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rag_prebuilt.json"),
    CacheStorePath    = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MyApp", "rag_cache.json")
};

// 创建实例（Console.WriteLine 作为日志输出，传 null 则静默）
var rag = new RAGManager(config, logger: Console.WriteLine);

// 初始化（异步，只需调用一次）
await rag.InitializeAsync();
```

### Step 3：在对话中使用

```csharp
string userInput = "退款政策是什么？";

// 检索
string context = rag.RetrieveContext(userInput);

// 判断是否有相关内容，自己拼 prompt
if (!string.IsNullOrEmpty(context))
{
    string prompt = $"【参考资料】\n{context}\n\n【用户问题】\n{userInput}\n\n请根据以上资料回答：";
    // 把 prompt 发给你的模型...
}
else
{
    // 没有检索到相关内容，直接用原始 userInput 走正常对话
}
```

---

## 4. RAGConfig 配置项说明

### 路径类

| 配置项 | 类型 | 必填 | 说明 |
|--------|------|------|------|
| `KnowledgeBaseDir` | `string` | ✅ | 知识库 `.txt` 文件所在目录的绝对路径 |
| `PrebuiltStorePath` | `string` | ❌ | 预构建向量库 JSON 文件路径，随程序一起发布 |
| `CacheStorePath` | `string` | ❌ | 本地缓存向量库 JSON 文件路径，首次全量构建后自动保存 |

**路径建议：**

```csharp
// KnowledgeBaseDir：和程序同目录下的 KnowledgeBase 文件夹
KnowledgeBaseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KnowledgeBase");

// PrebuiltStorePath：和程序同目录，随包发布
PrebuiltStorePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rag_prebuilt.json");

// CacheStorePath：用户本地数据目录，避免放在程序目录（可能没有写权限）
CacheStorePath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "MyApp",
    "rag_cache.json"
);
```

> ⚠️ `PrebuiltStorePath` 和 `CacheStorePath` 都不填时，每次启动都会全量构建，用户等待时间最长。建议至少配置 `CacheStorePath`。

### 切块参数

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `ChunkSize` | `int` | `400` | 每块最大字符数 |
| `ChunkOverlap` | `int` | `50` | 相邻块重叠字符数，防止关键信息在边界截断 |

**调参建议：**

```
文档类型            ChunkSize    ChunkOverlap
──────────────────────────────────────────────
问答 / FAQ         200 ~ 300    30 ~ 50
技术文档            400 ~ 600    50 ~ 100
叙述性文本          300 ~ 500    50 ~ 80
```

> ⚠️ 修改切块参数后，已有的预构建文件和缓存文件需要**重新生成**，否则新查询的向量维度与库中存储的不匹配，检索结果会错乱。

### 检索参数

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `TopK` | `int` | `3` | 每次检索返回的最相关块数量 |
| `MinScore` | `float` | `0.01f` | 余弦相似度最低阈值，低于此值的块不返回 |

**MinScore 调参建议：**

```
MinScore = 0.01  门槛极低，几乎所有问题都会带上检索内容（噪音多）
MinScore = 0.10  适中，只有有一定相关性才注入
MinScore = 0.20  门槛较高，只有关键词高度匹配才注入
MinScore = 0.30  门槛很高，基本只有精确命中才注入
```

`MinScore` 是"自动判断是否启用 RAG"的核心机制：问无关的问题时，检索分数低于阈值，`RetrieveContext` 返回空字符串，调用方走普通对话。

### Embedding 参数

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `MaxVocabSize` | `int` | `8192` | TF-IDF 词汇表上限，减小可降低内存，但会丢失生僻词 |

### 行为控制

| 配置项 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `UseCachedStore` | `bool` | `true` | 是否读取 / 写入本地缓存 |
| `ForceRebuild` | `bool` | `false` | 强制全量构建，忽略预构建和缓存（调试用） |

---

## 5. 初始化与加载优先级

`InitializeAsync()` 启动时按以下优先级依次尝试，找到可用的立刻停止：

```
调用 InitializeAsync()
        │
        ├─① PrebuiltStorePath 文件存在 且 ForceRebuild = false？
        │         │
        │        YES ──→ 加载预构建文件 ──→ 重建词汇表 ──→ IsReady = true ✅
        │         │                                       （最快，所有用户共享）
        │         NO
        │         │
        ├─② CacheStorePath 文件存在 且 UseCachedStore = true？
        │         │
        │        YES ──→ 加载本地缓存 ──→ 重建词汇表 ──→ IsReady = true ✅
        │         │                                      （本机第二次起有效）
        │         NO
        │         │
        └─③ 全量构建（兜底）
                  │
                  读文档 → 切块 → Fit → 向量化 → IsReady = true ✅
                  │                               （最慢，有日志警告）
                  └──→ 若 UseCachedStore=true，保存到 CacheStorePath
```

**`InitializeAsync()` 是异步方法，必须 `await`：**

```csharp
// ✅ 正确
await rag.InitializeAsync();

// ❌ 错误：忘记 await，IsReady 还是 false 就开始查询了
rag.InitializeAsync();
string context = rag.RetrieveContext("...");  // 会直接返回空字符串
```

**初始化只需要调用一次**，之后同一个实例可以无限次调用 `RetrieveContext`。

---

## 6. 检索接口

### 6.1 RetrieveContext（主要使用）

```csharp
string RetrieveContext(string question)
```

返回拼接好的上下文字符串，没有相关内容时返回 `""`（空字符串）。

**返回格式示例：**

```
[片段 1（来源：FAQ.txt，相似度：0.312）]
自收货之日起 7 日内，产品无质量问题可申请无理由退货...

[片段 2（来源：FAQ.txt，相似度：0.187）]
保修期内非人为损坏，提供免费维修或更换同型号产品...
```

```csharp
string context = rag.RetrieveContext(userInput);

if (!string.IsNullOrEmpty(context))
{
    // 有相关内容，注入到 prompt
    string prompt = BuildPromptWithContext(userInput, context);
    // 发给模型...
}
else
{
    // 无相关内容，走普通对话
    string prompt = userInput;
    // 发给模型...
}
```

### 6.2 RetrieveDetailed（调试 / 展示来源）

```csharp
List<RetrievalResult> RetrieveDetailed(string question)
```

返回详细结果列表，每项包含 `Chunk`（文档块）和 `Score`（相似度分数）。

```csharp
var results = rag.RetrieveDetailed("退款政策");

foreach (var r in results)
{
    Console.WriteLine($"分数：{r.Score:F4}");
    Console.WriteLine($"来源：{r.Chunk.SourceFile}");
    Console.WriteLine($"内容：{r.Chunk.Content}");
    Console.WriteLine();
}
```

用于两个场景：
- **调试**：判断检索质量，分数是否符合预期
- **UI 展示**：在界面上显示"参考来源"，让用户知道回答依据

### 6.3 IsReady 属性

```csharp
bool IsReady { get; }
```

调用任何检索方法前应先判断：

```csharp
if (!rag.IsReady)
{
    // RAG 未就绪，跳过检索，走普通对话
    return;
}

string context = rag.RetrieveContext(userInput);
```

未就绪时调用 `RetrieveContext` 会直接返回空字符串（不会抛异常），但这个判断有助于排查问题。

---

## 7. 在对话系统中接入

### 标准接入模式

以下是在 NPC 对话系统中的完整接入示例：

```csharp
private async Task<string> ProcessDialogueRequest(string userInput)
{
    // ── Step 1：RAG 检索 ─────────────────────────────────
    string contextualInput = userInput;

    if (_rag != null && _rag.IsReady)
    {
        string context = _rag.RetrieveContext(userInput);

        if (!string.IsNullOrEmpty(context))
        {
            // 有相关内容：把参考资料注入到 user 消息里
            contextualInput =
                $"【参考资料】\n{context}\n\n" +
                $"【用户说】\n{userInput}\n\n" +
                $"请以你的角色身份回答，不要提及"参考资料"四个字。";
        }
        // 没有相关内容：contextualInput 保持原样，走普通对话
    }

    // ── Step 2：组装消息，发给模型（调用方自己处理）───────
    var messages = new List<Message>
    {
        new Message("system", _personalityPrompt),  // NPC 人设，完全不动
        new Message("user",   contextualInput)       // 可能含 RAG 内容，也可能是原始输入
    };

    // 发给模型...
    string reply = await _yourLLMClient.ChatAsync(messages);
    return reply;
}
```

### 多轮对话中的注意事项

如果你的对话系统维护了历史消息列表（多轮对话），RAG 注入的参考资料会随着对话历史一起累积在列表里。为了避免历史记录被污染，推荐"临时构建发送列表"的方式：

```csharp
// _messages 是持久化的历史对话列表，只存原始内容
// sendMessages 是每次发送前临时构建的，RAG 内容只存在于这次请求中

private async Task<string> ProcessDialogueRequest(string userInput)
{
    string context = _rag?.IsReady == true
        ? _rag.RetrieveContext(userInput)
        : string.Empty;

    // 临时构建发送列表（不修改 _messages）
    var sendMessages = new List<Message>(_messages);  // 复制历史

    if (!string.IsNullOrEmpty(context))
    {
        // 在 system 之后插入一条临时 system，仅此次请求有效
        sendMessages.Insert(1, new Message("system",
            $"【本轮参考资料】\n{context}\n" +
            "请以角色身份使用以上资料回答，不要提及资料来源。"));
    }

    // 追加本次用户原始输入（不含 RAG 内容）
    sendMessages.Add(new Message("user", userInput));

    // 发送
    string reply = await _yourLLMClient.ChatAsync(sendMessages);

    // 只把原始对话追加到持久历史（RAG 内容不留痕）
    _messages.Add(new Message("user",      userInput));
    _messages.Add(new Message("assistant", reply));

    return reply;
}
```

---

## 8. 预构建向量库

### 为什么需要预构建

向量化是 CPU 密集型操作，文档越多耗时越长。如果在程序启动时现场构建，所有用户第一次启动都要等待。正确做法是开发者提前构建好，把 JSON 文件随程序一起发布，用户启动时直接加载。

### 如何生成预构建文件

写一个独立的控制台程序或脚本，在发布前运行：

```csharp
// BuildPrebuilt.cs（独立运行，不随游戏/程序发布）

var config = new RAGConfig
{
    KnowledgeBaseDir  = @"C:\MyProject\KnowledgeBase",
    CacheStorePath    = @"C:\MyProject\rag_prebuilt.json",  // 复用 CacheStorePath 作为输出
    UseCachedStore    = true,
    ForceRebuild      = true  // 强制全量构建，忽略旧缓存
};

var rag = new RAGManager(config, Console.WriteLine);
await rag.InitializeAsync();

Console.WriteLine("预构建完成，文件已保存到 rag_prebuilt.json");
```

运行后将生成的 `rag_prebuilt.json` 复制到发布目录，在运行时配置里把路径指向它：

```csharp
// 运行时配置
var config = new RAGConfig
{
    KnowledgeBaseDir  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KnowledgeBase"),
    PrebuiltStorePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rag_prebuilt.json"),
    CacheStorePath    = Path.Combine(localAppData, "MyApp", "rag_cache.json")
};
```

### 何时需要重新预构建

- 知识库目录中的文档有增删改
- 修改了 `ChunkSize` 或 `ChunkOverlap`
- 修改了 `MaxVocabSize`

以上任意一项变更后都需要重新运行构建脚本并更新发布的 JSON 文件。

---

## 9. 日志系统

RAGManager 通过构造函数的 `logger` 参数接受一个 `Action<string>` 回调，所有内部日志都通过这个回调输出。

### 直接输出到控制台

```csharp
var rag = new RAGManager(config, logger: Console.WriteLine);
```

### 对接自己的日志框架

```csharp
// 对接 NLog、Serilog 等
var rag = new RAGManager(config, logger: msg => MyLogger.Info(msg));

// 对接 Unity 的 Debug.Log（如果在 Unity 环境使用纯 C# 版）
var rag = new RAGManager(config, logger: msg => UnityEngine.Debug.Log(msg));
```

### 静默模式（不输出任何日志）

```csharp
var rag = new RAGManager(config, logger: null);
```

### 日志前缀对照

| 前缀 | 来源 |
|------|------|
| `[RAGManager]` | 初始化流程、检索结果摘要 |
| `[DocumentLoader]` | 文件读取（全量构建时出现） |
| `[TextSplitter]` | 切块（全量构建时出现） |
| `[TFIDFEmbedding]` | Fit 词汇表 |
| `[VectorStore]` | 加载、保存、检索 |

通过日志可以判断走了哪条加载路径：

```
出现这条 → 走预构建（最优）：
  [VectorStore] 已加载 X 个块 ← .../rag_prebuilt.json

出现这条 → 走本地缓存（次优）：
  [VectorStore] 已加载 X 个块 ← .../rag_cache.json

出现这条 → 走全量构建（最慢，需补预构建）：
  [RAGManager] ⚠️ 建议提前运行预构建工具生成 PrebuiltStorePath 文件
```

---

## 10. 注意事项

### 关于线程安全

`InitializeAsync()` 内部使用 `Task.Run` 将 CPU 密集操作（Fit、向量化）放到线程池执行，不会阻塞调用线程。但 `RAGManager` 实例本身不是线程安全的，不要在多个线程上同时调用同一个实例的方法。如果需要并发检索，每个线程创建独立实例，共享同一份预构建 JSON 文件加载即可。

### 关于内存占用

词汇表大小 × 文档块数 × 4 字节 = 向量库内存占用。举例：

```
词汇表 8192 词 × 500 个块 × 4 字节 ≈ 16 MB
```

文档量较大时可以适当降低 `MaxVocabSize`（如改为 4096）来减少内存占用。

### 关于 TF-IDF 的语义局限

TF-IDF 基于词频匹配，对同义词和语义相近但用词不同的情况支持较差。例如：

```
用户问：「怎么退货」
文档写：「如何申请退款」
→ TF-IDF 可能检索不到，因为"退货"和"退款"是不同的词
```

如果检索质量达不到要求，可以实现 `IEmbeddingService` 接口替换为神经网络 Embedding：

```csharp
public class MyEmbeddingService : IEmbeddingService
{
    public int Dimensions => 768;

    public void Fit(IEnumerable<string> corpus)
    {
        // 神经网络 Embedding 不需要 Fit，留空即可
    }

    public float[] GetEmbedding(string text)
    {
        // 调用本地或远程 Embedding 服务，返回 float[]
        // 实现你自己的逻辑...
    }
}
```

替换时，在 `RAGManager.InitializeAsync()` 的全量构建分支中将 `new TFIDFEmbeddingService(...)` 改为 `new MyEmbeddingService()` 即可，其余代码无需改动。

### 关于文档格式

- 文件必须为 **UTF-8 编码**的 `.txt` 文件，其他格式（PDF、Word 等）需要自行在外部转为 `.txt` 后放入知识库目录
- 切块器会优先在空行（段落）、句号、换行处断开，文档内容尽量用段落组织，避免超长单行

### 关于路径

- 所有路径建议使用绝对路径，避免相对路径在不同工作目录下出问题
- `CacheStorePath` 不要放在程序安装目录，部分系统对该目录没有写权限，应放在用户数据目录（`LocalApplicationData` 或等效位置）

---

## 11. 常见问题

### Q1：`InitializeAsync` 完成后 `IsReady` 仍为 false

可能原因：`KnowledgeBaseDir` 目录不存在或没有 `.txt` 文件；三条加载路径都失败了。查看日志输出确认具体原因。

### Q2：`RetrieveContext` 始终返回空字符串

两种可能：`IsReady = false`（未初始化完成）；或者 `MinScore` 设置过高，所有结果都被过滤掉。可以临时将 `MinScore` 设为 `0` 确认是哪种情况，再用 `RetrieveDetailed` 查看实际分数。

### Q3：修改文档后检索结果没变化

加载的是旧的预构建或缓存文件。解决方案：将 `ForceRebuild` 临时设为 `true` 运行一次，重新生成预构建文件后恢复为 `false`。

### Q4：JSON 文件加载失败

`System.Text.Json` 需要 .NET 6+ 或 `System.Text.Json` NuGet 包（.NET Standard 2.1 需要手动安装）。确认目标框架版本，或在项目文件中添加：

```xml
<PackageReference Include="System.Text.Json" Version="8.0.0" />
```

### Q5：切块参数改了但效果没变

已有的预构建文件和缓存文件是按旧参数生成的，参数修改不会自动更新这些文件。必须删除旧文件或设置 `ForceRebuild = true` 重新构建。

### Q6：检索分数普遍很低

TF-IDF 的局限：查询词和文档用词必须高度重合才有高分。建议：调整文档用词使其贴近用户的自然提问方式；或考虑升级为神经网络 Embedding（见第 10 节）。

---

*文档版本：1.0 | 最后更新：2026-04*
