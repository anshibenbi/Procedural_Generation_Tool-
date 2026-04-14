using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// 基于 TF-IDF 的 Embedding 实现
/// 纯 C#，无任何第三方依赖，兼容所有 Unity 平台
///
/// 原理：
///   TF  = 词在当前文档中出现的频率
///   IDF = log(文档总数 / 包含该词的文档数)，衡量词的稀有程度
///   TF-IDF = TF × IDF，越高表示这个词对该文档越具有区分性
/// </summary>
public class TFIDFEmbeddingService : IEmbeddingService
{
    // ── 词汇表（词 → 索引） ─────────────────────────────
    private Dictionary<string, int> _vocabulary = new();

    // ── IDF 权重，长度 = 词汇表大小 ─────────────────────
    private float[] _idfWeights;

    // ── 语料库文档总数（用于 IDF 计算） ─────────────────
    private int _documentCount;

    // ── 最大词汇表大小（防止过大占用内存） ──────────────
    private readonly int _maxVocabSize;

    public int Dimensions => _vocabulary.Count;

    public TFIDFEmbeddingService(int maxVocabSize = 8192)
    {
        _maxVocabSize = maxVocabSize;
    }

    // ─────────────────────────────────────────────────────
    //  Fit：用语料库建立词汇表 + 计算 IDF 权重
    // ─────────────────────────────────────────────────────

    public void Fit(IEnumerable<string> corpus)
    {
        var docList = corpus.ToList();
        _documentCount = docList.Count;

        // Step 1：统计每个词在多少篇文档中出现（DF）
        var df = new Dictionary<string, int>();
        var allTokenLists = new List<HashSet<string>>();

        foreach (var doc in docList)
        {
            var tokens = Tokenize(doc);
            var uniqueTokens = new HashSet<string>(tokens);
            allTokenLists.Add(uniqueTokens);

            foreach (var token in uniqueTokens)
            {
                df.TryGetValue(token, out int count);
                df[token] = count + 1;
            }
        }

        // Step 2：按 DF 降序排列，取前 maxVocabSize 个高频词构建词汇表
        //         注意：TF-IDF 中 IDF 越低的词（越常见）区分度越差
        //         保留高频词是为了覆盖更多文档，IDF 权重会自然压低它们的影响
        var sortedTerms = df
            .OrderByDescending(kv => kv.Value)
            .Take(_maxVocabSize)
            .Select(kv => kv.Key)
            .ToList();

        _vocabulary.Clear();
        for (int i = 0; i < sortedTerms.Count; i++)
            _vocabulary[sortedTerms[i]] = i;

        // Step 3：计算 IDF 权重
        //   IDF(t) = log((N + 1) / (df(t) + 1)) + 1
        //   +1 平滑处理，避免除以零，同时让完全不在语料库中的词也有非零权重
        _idfWeights = new float[_vocabulary.Count];
        foreach (var (term, idx) in _vocabulary)
        {
            float idf = MathF.Log((_documentCount + 1f) / (df[term] + 1f)) + 1f;
            _idfWeights[idx] = idf;
        }

        UnityEngine.Debug.Log($"[TFIDFEmbedding] Fit 完成：{_documentCount} 篇文档，词汇表大小 {_vocabulary.Count}");
    }

    // ─────────────────────────────────────────────────────
    //  GetEmbedding：将文本转为 TF-IDF 向量（L2 归一化）
    // ─────────────────────────────────────────────────────

    public float[] GetEmbedding(string text)
    {
        if (_vocabulary.Count == 0)
            throw new InvalidOperationException("[TFIDFEmbedding] 请先调用 Fit() 建立词汇表");

        var tokens = Tokenize(text);
        var vector = new float[_vocabulary.Count];

        if (tokens.Count == 0) return vector;

        // 计算 TF（词频）
        var tf = new Dictionary<string, int>();
        foreach (var token in tokens)
        {
            if (!_vocabulary.ContainsKey(token)) continue;
            tf.TryGetValue(token, out int cnt);
            tf[token] = cnt + 1;
        }

        // TF-IDF = TF/总词数 × IDF
        float totalTokens = tokens.Count;
        foreach (var (term, count) in tf)
        {
            int idx = _vocabulary[term];
            vector[idx] = (count / totalTokens) * _idfWeights[idx];
        }

        // L2 归一化（让余弦相似度 = 点积，提升计算效率）
        return L2Normalize(vector);
    }

    // ─────────────────────────────────────────────────────
    //  工具方法
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// 分词：转小写 → 按非字母数字分割 → 过滤停用词 → 过滤短词
    /// </summary>
    private List<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();

        // 对中文：按字符级别分词（每个汉字作为一个 token）
        // 对英文：按空格/标点分词
        var tokens = new List<string>();

        // 先用正则提取所有"词单元"
        var matches = Regex.Matches(text.ToLowerInvariant(), @"[\u4e00-\u9fff]|[a-z0-9]+");
        foreach (Match m in matches)
        {
            string token = m.Value;
            // 过滤：英文单词长度 < 2 的跳过；中文字符保留
            if (token.Length < 2 && !IsChinese(token)) continue;
            // 过滤通用停用词
            if (IsStopWord(token)) continue;
            tokens.Add(token);
        }

        return tokens;
    }

    private static bool IsChinese(string s)
        => s.Length == 1 && s[0] >= '\u4e00' && s[0] <= '\u9fff';

    private static readonly HashSet<string> StopWords = new()
    {
        // 英文停用词
        "the", "a", "an", "is", "it", "in", "on", "at", "to", "of",
        "and", "or", "but", "for", "with", "this", "that", "are", "was",
        "be", "by", "from", "as", "not", "have", "has", "had",
        // 中文停用词
        "的", "了", "是", "在", "我", "有", "和", "就", "不", "人",
        "都", "一", "一个", "上", "也", "很", "到", "说", "要", "去",
        "你", "会", "着", "没有", "看", "好", "自己", "这"
    };

    private static bool IsStopWord(string token) => StopWords.Contains(token);

    /// <summary>L2 归一化</summary>
    private static float[] L2Normalize(float[] v)
    {
        float norm = 0f;
        for (int i = 0; i < v.Length; i++) norm += v[i] * v[i];
        norm = MathF.Sqrt(norm);
        if (norm < 1e-10f) return v;
        for (int i = 0; i < v.Length; i++) v[i] /= norm;
        return v;
    }
}
