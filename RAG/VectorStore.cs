using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// ─────────────────────────────────────────────────────────
//  检索结果
// ─────────────────────────────────────────────────────────

public class RetrievalResult
{
    public DocumentChunk Chunk;
    public float Score; // 余弦相似度，范围 [0, 1]

    public RetrievalResult(DocumentChunk chunk, float score)
    {
        Chunk = chunk;
        Score = score;
    }
}

// ─────────────────────────────────────────────────────────
//  VectorStore：向量存储 + 相似度检索
// ─────────────────────────────────────────────────────────

public class VectorStore
{
    // ── 内存中的所有块（含向量）────────────────────────
    private readonly List<DocumentChunk> _chunks = new();

    // ── 持久化路径 ──────────────────────────────────────
    private readonly string _savePath;

    // ── 是否已初始化（Fit 过 Embedding） ────────────────
    private bool _isReady = false;

    public int Count => _chunks.Count;
    public bool IsReady => _isReady;

    public VectorStore(string saveFileName = "rag_vectorstore.json")
    {
        _savePath = Path.Combine(Application.persistentDataPath, saveFileName);
    }

    // ─────────────────────────────────────────────────────
    //  添加块（带向量）
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// 添加一批已向量化的块
    /// </summary>
    public void AddChunks(List<DocumentChunk> chunks)
    {
        foreach (var chunk in chunks)
        {
            if (chunk.Vector == null || chunk.Vector.Length == 0)
            {
                Debug.LogWarning($"[VectorStore] 块 {chunk.Id} 没有向量，跳过");
                continue;
            }
            _chunks.Add(chunk);
        }
        _isReady = _chunks.Count > 0;
        Debug.Log($"[VectorStore] 已添加 {chunks.Count} 个块，总计 {_chunks.Count} 个");
    }

    /// <summary>
    /// 清空所有块
    /// </summary>
    public void Clear()
    {
        _chunks.Clear();
        _isReady = false;
    }

    // ─────────────────────────────────────────────────────
    //  检索 Top-K
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// 用查询向量检索最相关的 Top-K 块
    /// </summary>
    /// <param name="queryVector">查询文本的 Embedding 向量</param>
    /// <param name="topK">返回最相关的块数量</param>
    /// <param name="minScore">最低相似度阈值，低于此值不返回</param>
    public List<RetrievalResult> Search(float[] queryVector, int topK = 3, float minScore = 0.01f)
    {
        if (!_isReady)
        {
            Debug.LogWarning("[VectorStore] 向量库为空，请先添加文档");
            return new List<RetrievalResult>();
        }

        // 对所有块计算余弦相似度，取 Top-K
        return _chunks
            .Select(chunk => new RetrievalResult(chunk, CosineSimilarity(queryVector, chunk.Vector)))
            .Where(r => r.Score >= minScore)
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();
    }

    // ─────────────────────────────────────────────────────
    //  持久化 / 加载
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// 将向量库保存为 JSON（路径：Application.persistentDataPath）
    /// </summary>
    public void Save()
    {
        try
        {
            var wrapper = new ChunkListWrapper { Chunks = _chunks };
            string json = JsonUtility.ToJson(wrapper, prettyPrint: true);
            File.WriteAllText(_savePath, json, System.Text.Encoding.UTF8);
            Debug.Log($"[VectorStore] 已保存 {_chunks.Count} 个块 → {_savePath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[VectorStore] 保存失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 从 JSON 加载向量库
    /// </summary>
    /// <returns>是否成功加载</returns>
    public bool TryLoad()
    {
        if (!File.Exists(_savePath))
        {
            Debug.Log("[VectorStore] 未找到已保存的向量库，将重新构建");
            return false;
        }

        try
        {
            string json = File.ReadAllText(_savePath, System.Text.Encoding.UTF8);
            var wrapper = JsonUtility.FromJson<ChunkListWrapper>(json);

            if (wrapper?.Chunks == null || wrapper.Chunks.Count == 0)
            {
                Debug.LogWarning("[VectorStore] 加载的向量库为空");
                return false;
            }

            _chunks.Clear();
            _chunks.AddRange(wrapper.Chunks);
            _isReady = true;
            Debug.Log($"[VectorStore] 已从缓存加载 {_chunks.Count} 个块");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[VectorStore] 加载失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 从指定路径加载向量库（用于加载 StreamingAssets 中的预构建文件）
    /// </summary>
    public bool TryLoadFromPath(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Debug.Log($"[VectorStore] 未找到预构建文件：{filePath}");
            return false;
        }

        try
        {
            string json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            var wrapper = JsonUtility.FromJson<ChunkListWrapper>(json);

            if (wrapper?.Chunks == null || wrapper.Chunks.Count == 0)
            {
                Debug.LogWarning("[VectorStore] 预构建文件为空或格式错误");
                return false;
            }

            _chunks.Clear();
            _chunks.AddRange(wrapper.Chunks);
            _isReady = true;
            Debug.Log($"[VectorStore] 已从预构建文件加载 {_chunks.Count} 个块");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[VectorStore] 加载预构建文件失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 返回所有块的只读列表（用于重建 Embedding 词汇表）
    /// </summary>
    public List<DocumentChunk> GetAllChunks() => new List<DocumentChunk>(_chunks);
    {
        if (File.Exists(_savePath))
        {
            File.Delete(_savePath);
            Debug.Log("[VectorStore] 已删除缓存文件");
        }
    }

    // ─────────────────────────────────────────────────────
    //  数学工具
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// 余弦相似度：两个向量都已经 L2 归一化时，等于点积
    /// 结果范围：[-1, 1]，越接近 1 越相似
    /// </summary>
    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return 0f;

        float dot = 0f;
        for (int i = 0; i < a.Length; i++) dot += a[i] * b[i];
        return dot;
        // 注意：如果向量未归一化，需要除以 ||a||*||b||
        // 因为 TFIDFEmbeddingService 已做 L2 归一化，这里直接用点积即可
    }

    // ─────────────────────────────────────────────────────
    //  JsonUtility 序列化辅助（JsonUtility 不支持裸 List）
    // ─────────────────────────────────────────────────────

    [Serializable]
    private class ChunkListWrapper
    {
        public List<DocumentChunk> Chunks;
    }
}
