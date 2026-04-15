using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

// ─────────────────────────────────────────────────────────
//  DocumentLoader
// ─────────────────────────────────────────────────────────

public static class DocumentLoader
{
    /// <summary>
    /// 加载指定目录下所有 .txt 文件
    /// </summary>
    /// <param name="directory">知识库目录的绝对路径</param>
    /// <param name="logger">日志输出，传 null 则不输出</param>
    public static async Task<List<(string fileName, string content)>> LoadAllAsync(
        string directory,
        Action<string> logger = null)
    {
        var results = new List<(string, string)>();

        if (!Directory.Exists(directory))
        {
            logger?.Invoke($"[DocumentLoader] 目录不存在：{directory}");
            Directory.CreateDirectory(directory);
            return results;
        }

        var files = Directory.GetFiles(directory, "*.txt", SearchOption.AllDirectories);
        logger?.Invoke($"[DocumentLoader] 找到 {files.Length} 个 .txt 文件");

        foreach (var filePath in files)
        {
            try
            {
                string content  = await File.ReadAllTextAsync(filePath, System.Text.Encoding.UTF8);
                string fileName = Path.GetFileName(filePath);
                results.Add((fileName, content));
                logger?.Invoke($"[DocumentLoader] 已加载：{fileName}（{content.Length} 字符）");
            }
            catch (Exception ex)
            {
                logger?.Invoke($"[DocumentLoader] 读取失败 {filePath}：{ex.Message}");
            }
        }

        return results;
    }
}

// ─────────────────────────────────────────────────────────
//  TextSplitter
// ─────────────────────────────────────────────────────────

public class TextSplitter
{
    private readonly int      _chunkSize;
    private readonly int      _chunkOverlap;
    private readonly Action<string> _logger;

    private static readonly string[] Separators =
    {
        "\n\n", "\n", "。", "！", "？", ". ", "! ", "? ", " ", ""
    };

    /// <param name="chunkSize">每块最大字符数</param>
    /// <param name="chunkOverlap">相邻块重叠字符数</param>
    /// <param name="logger">日志输出，传 null 则不输出</param>
    public TextSplitter(int chunkSize = 400, int chunkOverlap = 50, Action<string> logger = null)
    {
        if (chunkOverlap >= chunkSize)
            throw new ArgumentException("chunkOverlap 必须小于 chunkSize");

        _chunkSize   = chunkSize;
        _chunkOverlap = chunkOverlap;
        _logger      = logger;
    }

    public List<DocumentChunk> Split(string fileName, string content)
    {
        var chunks = new List<DocumentChunk>();
        if (string.IsNullOrWhiteSpace(content)) return chunks;

        var rawChunks = SplitText(content);
        for (int i = 0; i < rawChunks.Count; i++)
        {
            var (text, startIdx) = rawChunks[i];
            chunks.Add(new DocumentChunk(
                id:         $"{fileName}_{i}",
                sourceFile: fileName,
                content:    text,
                startIndex: startIdx
            ));
        }

        _logger?.Invoke($"[TextSplitter] {fileName} → {chunks.Count} 个块");
        return chunks;
    }

    private List<(string text, int startIndex)> SplitText(string text)
    {
        var result = new List<(string, int)>();
        int pos = 0;

        while (pos < text.Length)
        {
            int end = Math.Min(pos + _chunkSize, text.Length);
            if (end < text.Length)
                end = FindNaturalBoundary(text, pos, end);

            string chunk = text[pos..end].Trim();
            if (!string.IsNullOrWhiteSpace(chunk))
                result.Add((chunk, pos));

            pos = Math.Max(pos + 1, end - _chunkOverlap);
        }

        return result;
    }

    private int FindNaturalBoundary(string text, int start, int end)
    {
        foreach (var sep in Separators)
        {
            if (sep.Length == 0) break;
            int searchFrom = Math.Max(start, end - _chunkSize / 2);
            int idx = text.LastIndexOf(sep, end - 1, end - searchFrom);
            if (idx > start) return idx + sep.Length;
        }
        return end;
    }
}
