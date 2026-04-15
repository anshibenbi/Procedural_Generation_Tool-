using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// RAG 系统主入口，纯 C#，无任何 Unity 依赖
///
/// 使用方式：
///   var rag = new RAGManager(new RAGConfig { KnowledgeBaseDir = "path/to/docs" });
///   await rag.InitializeAsync();
///
///   // 只做检索，发送给模型是调用方自己的工作
///   string context = rag.RetrieveContext("用户的问题");
/// </summary>
public class RAGManager
{
    // ─────────────────────────────────────────────────────
    //  配置
    // ─────────────────────────────────────────────────────

    private readonly RAGConfig _config;

    // ─────────────────────────────────────────────────────
    //  状态
    // ─────────────────────────────────────────────────────

    public bool IsReady { get; private set; }

    // ─────────────────────────────────────────────────────
    //  核心组件
    // ─────────────────────────────────────────────────────

    private IEmbeddingService _embedding;
    private VectorStore       _vectorStore;
    private TextSplitter      _splitter;
    private readonly Action<string> _logger;

    // ─────────────────────────────────────────────────────
    //  构造
    // ─────────────────────────────────────────────────────

    /// <param name="config">RAG 配置项</param>
    /// <param name="logger">日志回调，传 null 则不输出；传 Console.WriteLine 输出到控制台</param>
    public RAGManager(RAGConfig config, Action<string> logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────
    //  初始化（三级加载优先级）
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// 初始化向量库。调用方 await 此方法后 IsReady 为 true 即可使用。
    ///
    /// 加载优先级：
    ///   ① PrebuiltStorePath 预构建文件（开发者提前生成，发布时随包附带）
    ///   ② CacheStorePath    本地缓存（本机上次构建的产物）
    ///   ③ 全量构建          兜底，第一次且无预构建文件时触发
    /// </summary>
    public async Task InitializeAsync()
    {
        IsReady = false;
        Log("[RAGManager] 开始初始化...");

        _splitter    = new TextSplitter(_config.ChunkSize, _config.ChunkOverlap, _logger);
        _vectorStore = new VectorStore(_config.CacheStorePath, _logger);

        if (!_config.ForceRebuild)
        {
            // ── ① 预构建文件 ──────────────────────────────
            if (!string.IsNullOrEmpty(_config.PrebuiltStorePath) &&
                _vectorStore.TryLoadFromPath(_config.PrebuiltStorePath))
            {
                Log("[RAGManager] ✅ 加载预构建向量库成功，重建词汇表...");
                await RebuildEmbeddingAsync(_vectorStore.GetAllChunks());
                IsReady = true;
                Log($"[RAGManager] 初始化完成（预构建），共 {_vectorStore.Count} 个块");
                return;
            }

            // ── ② 本地缓存 ────────────────────────────────
            if (_config.UseCachedStore && _vectorStore.TryLoad())
            {
                Log("[RAGManager] ✅ 加载本地缓存成功，重建词汇表...");
                await RebuildEmbeddingAsync(_vectorStore.GetAllChunks());
                IsReady = true;
                Log($"[RAGManager] 初始化完成（本地缓存），共 {_vectorStore.Count} 个块");
                return;
            }
        }

        // ── ③ 全量构建 ────────────────────────────────────
        Log("[RAGManager] 未找到预构建或缓存，开始全量构建...");
        Log("[RAGManager] ⚠️ 建议提前运行预构建工具生成 PrebuiltStorePath 文件");

        var documents = await DocumentLoader.LoadAllAsync(_config.KnowledgeBaseDir, _logger);
        if (documents.Count == 0)
        {
            Log("[RAGManager] KnowledgeBase 目录为空，RAG 不可用");
            return;
        }

        var allChunks = new List<DocumentChunk>();
        foreach (var (fileName, content) in documents)
            allChunks.AddRange(_splitter.Split(fileName, content));

        Log($"[RAGManager] 切块完成，共 {allChunks.Count} 个块");

        _embedding = new TFIDFEmbeddingService(_config.MaxVocabSize, _logger);
        await Task.Run(() => _embedding.Fit(allChunks.Select(c => c.Content)));
        await Task.Run(() =>
        {
            foreach (var chunk in allChunks)
                chunk.Vector = _embedding.GetEmbedding(chunk.Content);
        });

        _vectorStore.AddChunks(allChunks);

        if (_config.UseCachedStore)
            _vectorStore.Save();

        IsReady = true;
        Log($"[RAGManager] 全量构建完成，{_vectorStore.Count} 个块，维度 {_embedding.Dimensions}");
    }

    // ─────────────────────────────────────────────────────
    //  检索接口（对外暴露的核心方法）
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// 检索并返回拼接好的上下文字符串。
    /// 没有相关内容时返回空字符串，调用方判断是否注入。
    /// </summary>
    public string RetrieveContext(string question)
    {
        if (!IsReady) return string.Empty;

        float[] queryVector = _embedding.GetEmbedding(question);
        var     results     = _vectorStore.Search(queryVector, _config.TopK, _config.MinScore);

        if (results.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine($"[片段 {i + 1}（来源：{r.Chunk.SourceFile}，相似度：{r.Score:F3}）]");
            sb.AppendLine(r.Chunk.Content);
            sb.AppendLine();
        }

        Log($"[RAGManager] 检索到 {results.Count} 个块，最高分：{results[0].Score:F3}");
        return sb.ToString().Trim();
    }

    /// <summary>
    /// 返回详细检索结果（含分数，用于调试或展示来源）
    /// </summary>
    public List<RetrievalResult> RetrieveDetailed(string question)
    {
        if (!IsReady) return new List<RetrievalResult>();

        float[] queryVector = _embedding.GetEmbedding(question);
        return _vectorStore.Search(queryVector, _config.TopK, _config.MinScore);
    }

    // ─────────────────────────────────────────────────────
    //  内部工具
    // ─────────────────────────────────────────────────────

    private async Task RebuildEmbeddingAsync(List<DocumentChunk> chunks)
    {
        _embedding = new TFIDFEmbeddingService(_config.MaxVocabSize, _logger);
        await Task.Run(() => _embedding.Fit(chunks.Select(c => c.Content)));
    }

    private void Log(string msg) => _logger?.Invoke(msg);
}
