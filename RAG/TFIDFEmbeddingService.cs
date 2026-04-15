using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// 基于 TF-IDF 的 Embedding 实现
/// 纯 C#，无任何第三方依赖
/// </summary>
public class TFIDFEmbeddingService : IEmbeddingService
{
    private Dictionary<string, int> _vocabulary = new();
    private float[]                 _idfWeights;
    private int                     _documentCount;
    private readonly int            _maxVocabSize;
    private readonly Action<string> _logger;

    public int Dimensions => _vocabulary.Count;

    /// <param name="maxVocabSize">词汇表上限，默认 8192</param>
    /// <param name="logger">日志输出，传 null 则不输出</param>
    public TFIDFEmbeddingService(int maxVocabSize = 8192, Action<string> logger = null)
    {
        _maxVocabSize = maxVocabSize;
        _logger       = logger;
    }

    // ─────────────────────────────────────────────────────
    //  Fit
    // ─────────────────────────────────────────────────────

    public void Fit(IEnumerable<string> corpus)
    {
        var docList = corpus.ToList();
        _documentCount = docList.Count;

        var df = new Dictionary<string, int>();

        foreach (var doc in docList)
        {
            var unique = new HashSet<string>(Tokenize(doc));
            foreach (var token in unique)
            {
                df.TryGetValue(token, out int cnt);
                df[token] = cnt + 1;
            }
        }

        var terms = df
            .OrderByDescending(kv => kv.Value)
            .Take(_maxVocabSize)
            .Select(kv => kv.Key)
            .ToList();

        _vocabulary.Clear();
        for (int i = 0; i < terms.Count; i++)
            _vocabulary[terms[i]] = i;

        _idfWeights = new float[_vocabulary.Count];
        foreach (var (term, idx) in _vocabulary)
        {
            float idf = MathF.Log((_documentCount + 1f) / (df[term] + 1f)) + 1f;
            _idfWeights[idx] = idf;
        }

        _logger?.Invoke($"[TFIDFEmbedding] Fit 完成：{_documentCount} 篇文档，词汇表 {_vocabulary.Count} 词");
    }

    // ─────────────────────────────────────────────────────
    //  GetEmbedding
    // ─────────────────────────────────────────────────────

    public float[] GetEmbedding(string text)
    {
        if (_vocabulary.Count == 0)
            throw new InvalidOperationException("[TFIDFEmbedding] 请先调用 Fit()");

        var tokens = Tokenize(text);
        var vector = new float[_vocabulary.Count];
        if (tokens.Count == 0) return vector;

        var tf = new Dictionary<string, int>();
        foreach (var token in tokens)
        {
            if (!_vocabulary.ContainsKey(token)) continue;
            tf.TryGetValue(token, out int cnt);
            tf[token] = cnt + 1;
        }

        float total = tokens.Count;
        foreach (var (term, count) in tf)
        {
            int idx = _vocabulary[term];
            vector[idx] = (count / total) * _idfWeights[idx];
        }

        return L2Normalize(vector);
    }

    // ─────────────────────────────────────────────────────
    //  工具
    // ─────────────────────────────────────────────────────

    private List<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();

        var tokens  = new List<string>();
        var matches = Regex.Matches(text.ToLowerInvariant(), @"[\u4e00-\u9fff]|[a-z0-9]+");

        foreach (Match m in matches)
        {
            var token = m.Value;
            if (token.Length < 2 && !IsChinese(token)) continue;
            if (IsStopWord(token)) continue;
            tokens.Add(token);
        }

        return tokens;
    }

    private static bool IsChinese(string s)
        => s.Length == 1 && s[0] >= '\u4e00' && s[0] <= '\u9fff';

    private static readonly HashSet<string> StopWords = new()
    {
        "the","a","an","is","it","in","on","at","to","of","and","or","but",
        "for","with","this","that","are","was","be","by","from","as","not",
        "have","has","had",
        "的","了","是","在","我","有","和","就","不","人","都","一","一个",
        "上","也","很","到","说","要","去","你","会","着","没有","看","好",
        "自己","这"
    };

    private static bool IsStopWord(string t) => StopWords.Contains(t);

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
