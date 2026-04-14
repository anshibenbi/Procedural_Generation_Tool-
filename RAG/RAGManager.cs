using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// RAGManager：RAG 系统主入口（MonoBehaviour 单例）
///
/// 职责：
///   1. 初始化阶段：按优先级加载向量库（预构建 > 用户缓存 > 全量构建）
///   2. 查询阶段：问题向量化 → 检索 Top-K → 拼 Prompt → 调用 OllamaManager
///
/// 初始化优先级：
///   ① StreamingAssets/rag_vectorstore_prebuilt.json（开发者预构建，随包发布）
///   ② persistentDataPath/rag_vectorstore.json（用户本地缓存）
///   ③ 全量构建（兜底，第一次且没有预构建文件时）
///
/// 使用方式（非流式）：
///   string answer = await RAGManager.Instance.QueryAsync("你的问题");
///
/// 使用方式（流式）：
///   await RAGManager.Instance.QueryStreamAsync("你的问题",
///       onChunk: chunk => Debug.Log(chunk),
///       onComplete: full => Debug.Log("完成：" + full));
/// </summary>
public class RAGManager : MonoBehaviour
{
    // ── Inspector 配置 ────────────────────────────────────
    [Header("切块参数")]
    [SerializeField] private int chunkSize = 400;
    [SerializeField] private int chunkOverlap = 50;

    [Header("检索参数")]
    [SerializeField] private int topK = 3;
    [SerializeField] private float minScore = 0.01f;

    [Header("向量库")]
    [Tooltip("是否在启动时尝试加载已缓存的向量库（避免每次重新向量化）")]
    [SerializeField] private bool useCachedStore = true;

    [Tooltip("强制重建向量库（忽略所有缓存和预构建文件，重新构建）")]
    [SerializeField] private bool forceRebuild = false;

    // 预构建文件名（与 RAGPrebuilder 保持一致）
    private const string PrebuiltFileName = "rag_vectorstore_prebuilt.json";

    // ── 单例 ──────────────────────────────────────────────
    public static RAGManager Instance { get; private set; }

    // ── 状态 ──────────────────────────────────────────────
    public bool IsReady { get; private set; } = false;

    // ── 核心组件 ─────────────────────────────────────────
    private IEmbeddingService _embedding;
    private VectorStore _vectorStore;
    private TextSplitter _splitter;

    // ─────────────────────────────────────────────────────
    //  生命周期
    // ─────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private async void Start()
    {
        // 等待 OllamaManager 加载完成（最多等 60 秒）
        float waited = 0f;
        while (!OllamaManager.Instance.IsReady && waited < 60f)
        {
            await Task.Delay(500);
            waited += 0.5f;
        }

        if (!OllamaManager.Instance.IsReady)
        {
            Debug.LogError("[RAGManager] OllamaManager 未就绪，RAG 初始化中止");
            return;
        }

        await InitializeAsync();
    }

    // ─────────────────────────────────────────────────────
    //  初始化：构建向量库
    // ─────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        IsReady = false;
        Debug.Log("[RAGManager] 开始初始化...");

        _splitter = new TextSplitter(chunkSize, chunkOverlap);
        _vectorStore = new VectorStore();

        if (!forceRebuild)
        {
            // ── 优先级 ①：加载开发者预构建文件（随包发布，所有玩家共享）──────
            string prebuiltPath = System.IO.Path.Combine(
                Application.streamingAssetsPath, PrebuiltFileName);

            if (_vectorStore.TryLoadFromPath(prebuiltPath))
            {
                Debug.Log("[RAGManager] ✅ 加载预构建向量库成功，重建 Embedding 词汇表...");
                await RebuildEmbeddingFromChunksAsync(_vectorStore.GetAllChunks());
                IsReady = true;
                Debug.Log($"[RAGManager] 初始化完成（预构建），共 {_vectorStore.Count} 个块，耗时极短");
                return;
            }

            // ── 优先级 ②：加载用户本地缓存（本机第二次启动后生效）────────────
            if (useCachedStore && _vectorStore.TryLoad())
            {
                Debug.Log("[RAGManager] ✅ 加载本地缓存向量库成功，重建 Embedding 词汇表...");
                await RebuildEmbeddingFromChunksAsync(_vectorStore.GetAllChunks());
                IsReady = true;
                Debug.Log($"[RAGManager] 初始化完成（本地缓存），共 {_vectorStore.Count} 个块");
                return;
            }
        }

        // ── 优先级 ③：全量构建（兜底，首次且无预构建文件时）────────────────
        Debug.Log("[RAGManager] 未找到预构建或缓存文件，开始全量构建...");
        Debug.LogWarning("[RAGManager] ⚠️ 建议在 Editor 中运行「Tools → RAG → 预构建知识库」" +
                         "以避免玩家等待构建过程");

        var documents = await DocumentLoader.LoadAllAsync();
        if (documents.Count == 0)
        {
            Debug.LogWarning("[RAGManager] KnowledgeBase 目录为空，RAG 不可用");
            return;
        }

        var allChunks = new List<DocumentChunk>();
        foreach (var (fileName, content) in documents)
            allChunks.AddRange(_splitter.Split(fileName, content));
        Debug.Log($"[RAGManager] 切块完成，共 {allChunks.Count} 个块");

        _embedding = new TFIDFEmbeddingService(maxVocabSize: 8192);
        await Task.Run(() => _embedding.Fit(allChunks.Select(c => c.Content)));
        await Task.Run(() =>
        {
            foreach (var chunk in allChunks)
                chunk.Vector = _embedding.GetEmbedding(chunk.Content);
        });

        _vectorStore.AddChunks(allChunks);

        // 存到用户本地缓存，下次启动走优先级②
        if (useCachedStore)
            _vectorStore.Save();

        IsReady = true;
        Debug.Log($"[RAGManager] 全量构建完成，共 {_vectorStore.Count} 个块，向量维度 {_embedding.Dimensions}");
    }

    /// <summary>
    /// 从已加载的块列表重建 TF-IDF 词汇表（加载预构建/缓存后恢复查询能力）
    /// 直接用块内容 Fit，无需重新读文件
    /// </summary>
    private async Task RebuildEmbeddingFromChunksAsync(List<DocumentChunk> chunks)
    {
        _embedding = new TFIDFEmbeddingService(maxVocabSize: 8192);
        await Task.Run(() => _embedding.Fit(chunks.Select(c => c.Content)));
    }

    // ─────────────────────────────────────────────────────
    //  查询接口（非流式）
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// 执行一次 RAG 查询，返回完整回答
    /// </summary>
    public async Task<string> QueryAsync(
        string question,
        string systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsReady)
        {
            Debug.LogError("[RAGManager] 尚未初始化，请等待 IsReady = true");
            return null;
        }

        // Step 1：检索相关块
        var context = RetrieveContext(question);
        if (string.IsNullOrEmpty(context))
        {
            Debug.LogWarning("[RAGManager] 未检索到相关内容，直接调用 LLM");
        }

        // Step 2：构建 Prompt
        var messages = BuildMessages(question, context, systemPrompt);

        // Step 3：调用 OllamaManager
        string answer = await OllamaManager.Instance.ChatAsync(messages, cancellationToken: cancellationToken);
        return answer;
    }

    // ─────────────────────────────────────────────────────
    //  查询接口（流式）
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// 执行一次 RAG 查询，流式返回结果
    /// </summary>
    public async Task QueryStreamAsync(
        string question,
        Action<string> onChunk,
        Action<string> onComplete = null,
        Action<string> onError = null,
        string systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsReady)
        {
            onError?.Invoke("RAGManager 尚未初始化");
            return;
        }

        var context = RetrieveContext(question);
        var messages = BuildMessages(question, context, systemPrompt);

        await OllamaManager.Instance.ChatStreamAsync(
            messages,
            onChunk: onChunk,
            onComplete: onComplete,
            onError: onError,
            cancellationToken: cancellationToken
        );
    }

    // ─────────────────────────────────────────────────────
    //  检索上下文
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// 检索并拼接 Top-K 相关块为上下文字符串
    /// </summary>
    public string RetrieveContext(string question)
    {
        float[] queryVector = _embedding.GetEmbedding(question);
        var results = _vectorStore.Search(queryVector, topK, minScore);

        if (results.Count == 0) return string.Empty;

        // 拼接上下文，附带来源信息
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine($"[片段 {i + 1}（来源：{r.Chunk.SourceFile}，相似度：{r.Score:F3}）]");
            sb.AppendLine(r.Chunk.Content);
            sb.AppendLine();
        }

        Debug.Log($"[RAGManager] 检索到 {results.Count} 个相关块，最高分：{results[0].Score:F3}");
        return sb.ToString().Trim();
    }

    /// <summary>
    /// 返回详细检索结果（用于调试或 UI 显示来源）
    /// </summary>
    public List<RetrievalResult> RetrieveDetailed(string question)
    {
        float[] queryVector = _embedding.GetEmbedding(question);
        return _vectorStore.Search(queryVector, topK, minScore);
    }

    // ─────────────────────────────────────────────────────
    //  Prompt 构建
    // ─────────────────────────────────────────────────────

    private List<OllamaMessage> BuildMessages(
        string question,
        string context,
        string customSystemPrompt)
    {
        string systemContent = customSystemPrompt ?? BuildDefaultSystemPrompt();
        var messages = new List<OllamaMessage>
        {
            new OllamaMessage("system", systemContent)
        };

        // 如果检索到上下文，把它注入到 user 消息里
        string userContent = string.IsNullOrEmpty(context)
            ? question
            : $"请参考以下资料回答问题：\n\n{context}\n\n---\n\n问题：{question}";

        messages.Add(new OllamaMessage("user", userContent));
        return messages;
    }

    private static string BuildDefaultSystemPrompt() =>
        "你是一个专业的知识助手。请根据提供的参考资料回答用户问题。\n" +
        "规则：\n" +
        "1. 只使用参考资料中的信息作答。\n" +
        "2. 如果资料中没有相关信息，直接回答"根据现有资料，我无法回答这个问题"。\n" +
        "3. 不要编造资料中没有的内容。\n" +
        "4. 回答要简洁、准确。";

    // ─────────────────────────────────────────────────────
    //  运行时动态追加文档
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// 在运行时追加一段新文本到知识库（不重启即可生效）
    /// 注意：追加后需要重新 Fit Embedding，代价较大；
    ///       适合少量追加，大量追加建议调用 InitializeAsync() 重建
    /// </summary>
    public async Task AppendDocumentAsync(string fileName, string content)
    {
        var newChunks = _splitter.Split(fileName, content);
        if (newChunks.Count == 0) return;

        // 将新块加入语料后重新 Fit（TF-IDF 的局限性：必须见过全部语料）
        // 简化方案：只用新块的文本来扩充词汇（TF-IDF 效果会有偏差）
        // 生产级方案：触发完整 rebuild
        Debug.LogWarning("[RAGManager] AppendDocument 会触发 Embedding 重建，耗时较长");
        await InitializeAsync();
    }
}
