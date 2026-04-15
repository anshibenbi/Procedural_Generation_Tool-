using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

// ─────────────────────────────────────────────────────────
//  检索结果
// ─────────────────────────────────────────────────────────

public class RetrievalResult
{
    public DocumentChunk Chunk { get; }
    public float         Score { get; }

    public RetrievalResult(DocumentChunk chunk, float score)
    {
        Chunk = chunk;
        Score = score;
    }
}

// ─────────────────────────────────────────────────────────
//  VectorStore
// ─────────────────────────────────────────────────────────

public class VectorStore
{
    private readonly List<DocumentChunk> _chunks  = new();
    private readonly string              _savePath;
    private readonly Action<string>      _logger;
    private bool                         _isReady;

    public int  Count   => _chunks.Count;
    public bool IsReady => _isReady;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented            = true,
        PropertyNameCaseInsensitive = true
    };

    /// <param name="savePath">向量库 JSON 文件的完整路径</param>
    /// <param name="logger">日志输出，传 null 则不输出</param>
    public VectorStore(string savePath, Action<string> logger = null)
    {
        _savePath = savePath;
        _logger   = logger;
    }

    // ─────────────────────────────────────────────────────
    //  添加 / 清空
    // ─────────────────────────────────────────────────────

    public void AddChunks(List<DocumentChunk> chunks)
    {
        foreach (var chunk in chunks)
        {
            if (chunk.Vector == null || chunk.Vector.Length == 0)
            {
                _logger?.Invoke($"[VectorStore] 块 {chunk.Id} 没有向量，跳过");
                continue;
            }
            _chunks.Add(chunk);
        }
        _isReady = _chunks.Count > 0;
        _logger?.Invoke($"[VectorStore] 已添加 {chunks.Count} 个块，总计 {_chunks.Count} 个");
    }

    public void Clear()
    {
        _chunks.Clear();
        _isReady = false;
    }

    public List<DocumentChunk> GetAllChunks() => new List<DocumentChunk>(_chunks);

    // ─────────────────────────────────────────────────────
    //  检索
    // ─────────────────────────────────────────────────────

    public List<RetrievalResult> Search(float[] queryVector, int topK = 3, float minScore = 0.01f)
    {
        if (!_isReady)
        {
            _logger?.Invoke("[VectorStore] 向量库为空");
            return new List<RetrievalResult>();
        }

        return _chunks
            .Select(c => new RetrievalResult(c, CosineSimilarity(queryVector, c.Vector)))
            .Where(r => r.Score >= minScore)
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();
    }

    // ─────────────────────────────────────────────────────
    //  持久化
    // ─────────────────────────────────────────────────────

    public void Save()
    {
        try
        {
            string dir = Path.GetDirectoryName(_savePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(_chunks, JsonOpts);
            File.WriteAllText(_savePath, json, Encoding.UTF8);
            _logger?.Invoke($"[VectorStore] 已保存 {_chunks.Count} 个块 → {_savePath}");
        }
        catch (Exception ex)
        {
            _logger?.Invoke($"[VectorStore] 保存失败：{ex.Message}");
        }
    }

    public bool TryLoad()
    {
        if (!File.Exists(_savePath))
        {
            _logger?.Invoke("[VectorStore] 未找到缓存文件，将重新构建");
            return false;
        }

        return LoadFromPath(_savePath);
    }

    public bool TryLoadFromPath(string filePath)
    {
        if (!File.Exists(filePath))
        {
            _logger?.Invoke($"[VectorStore] 未找到预构建文件：{filePath}");
            return false;
        }

        return LoadFromPath(filePath);
    }

    private bool LoadFromPath(string filePath)
    {
        try
        {
            string json   = File.ReadAllText(filePath, Encoding.UTF8);
            var    chunks = JsonSerializer.Deserialize<List<DocumentChunk>>(json, JsonOpts);

            if (chunks == null || chunks.Count == 0)
            {
                _logger?.Invoke("[VectorStore] 文件为空或格式错误");
                return false;
            }

            _chunks.Clear();
            _chunks.AddRange(chunks);
            _isReady = true;
            _logger?.Invoke($"[VectorStore] 已加载 {_chunks.Count} 个块 ← {filePath}");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.Invoke($"[VectorStore] 加载失败：{ex.Message}");
            return false;
        }
    }

    public void DeleteSavedStore()
    {
        if (File.Exists(_savePath))
        {
            File.Delete(_savePath);
            _logger?.Invoke("[VectorStore] 已删除缓存文件");
        }
    }

    // ─────────────────────────────────────────────────────
    //  数学
    // ─────────────────────────────────────────────────────

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return 0f;
        float dot = 0f;
        for (int i = 0; i < a.Length; i++) dot += a[i] * b[i];
        return dot;
    }
}
