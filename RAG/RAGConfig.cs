/// <summary>
/// RAGManager 的所有配置项，通过构造函数传入
/// </summary>
public class RAGConfig
{
    // ── 路径 ─────────────────────────────────────────────

    /// <summary>
    /// 知识库 .txt 文件所在目录（必填）
    /// 例：Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KnowledgeBase")
    /// </summary>
    public string KnowledgeBaseDir { get; set; }

    /// <summary>
    /// 预构建向量库 JSON 文件路径（可选）
    /// 开发者提前生成，随程序一起发布，所有用户共享
    /// 例：Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rag_prebuilt.json")
    /// </summary>
    public string PrebuiltStorePath { get; set; }

    /// <summary>
    /// 本地缓存向量库 JSON 文件路径
    /// 全量构建完成后自动保存，下次启动直接加载
    /// 例：Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyApp", "rag_cache.json")
    /// </summary>
    public string CacheStorePath { get; set; }

    // ── 切块参数 ─────────────────────────────────────────

    /// <summary>每块最大字符数，默认 400</summary>
    public int ChunkSize    { get; set; } = 400;

    /// <summary>相邻块重叠字符数，默认 50</summary>
    public int ChunkOverlap { get; set; } = 50;

    // ── 检索参数 ─────────────────────────────────────────

    /// <summary>每次检索返回的最相关块数量，默认 3</summary>
    public int TopK { get; set; } = 3;

    /// <summary>余弦相似度最低阈值，低于此值不返回，默认 0.01</summary>
    public float MinScore { get; set; } = 0.01f;

    // ── Embedding 参数 ───────────────────────────────────

    /// <summary>TF-IDF 词汇表上限，默认 8192</summary>
    public int MaxVocabSize { get; set; } = 8192;

    // ── 行为控制 ─────────────────────────────────────────

    /// <summary>是否使用本地缓存（UseCachedStore=false 时全量构建结果不保存）</summary>
    public bool UseCachedStore { get; set; } = true;

    /// <summary>强制全量重建，忽略预构建和缓存（调试用）</summary>
    public bool ForceRebuild   { get; set; } = false;
}
