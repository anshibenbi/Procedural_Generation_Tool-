using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

// ─────────────────────────────────────────────────────────
//  DocumentLoader：从 StreamingAssets 加载 .txt 文档
// ─────────────────────────────────────────────────────────

public static class DocumentLoader
{
    /// <summary>
    /// 加载 StreamingAssets/KnowledgeBase/ 目录下所有 .txt 文件
    /// 返回 (文件名, 文本内容) 列表
    /// </summary>
    public static async Task<List<(string fileName, string content)>> LoadAllAsync()
    {
        var results = new List<(string, string)>();
        string dir = Path.Combine(Application.streamingAssetsPath, "KnowledgeBase");

        if (!Directory.Exists(dir))
        {
            Debug.LogWarning($"[DocumentLoader] 目录不存在，已自动创建：{dir}");
            Directory.CreateDirectory(dir);
            return results;
        }

        var files = Directory.GetFiles(dir, "*.txt", SearchOption.AllDirectories);
        Debug.Log($"[DocumentLoader] 找到 {files.Length} 个 .txt 文件");

        foreach (var filePath in files)
        {
            try
            {
                // 使用 StreamReader 异步读取，避免阻塞主线程
                string content;
                using (var reader = new StreamReader(filePath,
                    System.Text.Encoding.UTF8))
                {
                    content = await reader.ReadToEndAsync();
                }

                string fileName = Path.GetFileName(filePath);
                results.Add((fileName, content));
                Debug.Log($"[DocumentLoader] 已加载：{fileName}（{content.Length} 字符）");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DocumentLoader] 读取失败 {filePath}: {ex.Message}");
            }
        }

        return results;
    }

    /// <summary>
    /// 加载单个文件（用于动态追加知识库）
    /// </summary>
    public static async Task<string> LoadFileAsync(string absolutePath)
    {
        using var reader = new StreamReader(absolutePath, System.Text.Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }
}

// ─────────────────────────────────────────────────────────
//  TextSplitter：将长文本切分为带重叠的小块
// ─────────────────────────────────────────────────────────

public class TextSplitter
{
    /// <summary>每块最大字符数</summary>
    private readonly int _chunkSize;

    /// <summary>相邻块之间重叠的字符数（防止上下文在边界断裂）</summary>
    private readonly int _chunkOverlap;

    /// <summary>
    /// 段落分隔符优先级（先尝试在自然边界切分）
    /// </summary>
    private static readonly string[] Separators =
    {
        "\n\n",   // 空行（段落）
        "\n",     // 换行
        "。",     // 中文句号
        "！",     // 中文感叹号
        "？",     // 中文问号
        ". ",     // 英文句号+空格
        "! ",     // 英文感叹号
        "? ",     // 英文问号
        " ",      // 空格
        ""        // 最后降级：逐字符切
    };

    public TextSplitter(int chunkSize = 400, int chunkOverlap = 50)
    {
        if (chunkOverlap >= chunkSize)
            throw new ArgumentException("chunkOverlap 必须小于 chunkSize");

        _chunkSize = chunkSize;
        _chunkOverlap = chunkOverlap;
    }

    /// <summary>
    /// 将文档切分为 DocumentChunk 列表
    /// </summary>
    public List<DocumentChunk> Split(string fileName, string content)
    {
        var chunks = new List<DocumentChunk>();
        if (string.IsNullOrWhiteSpace(content)) return chunks;

        // 先把文本切成片段
        var rawChunks = SplitText(content);

        for (int i = 0; i < rawChunks.Count; i++)
        {
            var (text, startIdx) = rawChunks[i];
            chunks.Add(new DocumentChunk(
                id: $"{fileName}_{i}",
                sourceFile: fileName,
                content: text,
                startIndex: startIdx
            ));
        }

        Debug.Log($"[TextSplitter] {fileName} → {chunks.Count} 个块");
        return chunks;
    }

    // ─────────────────────────────────────────────────────
    //  核心切分逻辑
    // ─────────────────────────────────────────────────────

    private List<(string text, int startIndex)> SplitText(string text)
    {
        var result = new List<(string, int)>();
        int pos = 0; // 当前处理位置

        while (pos < text.Length)
        {
            int end = Math.Min(pos + _chunkSize, text.Length);

            // 如果还没到文本末尾，尝试在自然边界回退
            if (end < text.Length)
                end = FindNaturalBoundary(text, pos, end);

            string chunk = text[pos..end].Trim();
            if (!string.IsNullOrWhiteSpace(chunk))
                result.Add((chunk, pos));

            // 下一块从 (end - overlap) 开始，保留上下文
            pos = Math.Max(pos + 1, end - _chunkOverlap);
        }

        return result;
    }

    /// <summary>
    /// 在 [start, end] 范围内向前搜索最近的自然分隔符位置
    /// 找不到则返回原始 end
    /// </summary>
    private int FindNaturalBoundary(string text, int start, int end)
    {
        foreach (var sep in Separators)
        {
            if (sep.Length == 0) break; // 降级到逐字符，直接返回原始 end

            // 从 end 向前搜索分隔符
            int searchFrom = Math.Max(start, end - _chunkSize / 2);
            int idx = text.LastIndexOf(sep, end - 1, end - searchFrom);

            if (idx > start)
                return idx + sep.Length; // 切在分隔符之后
        }

        return end;
    }
}
