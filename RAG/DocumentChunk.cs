using System;

/// <summary>
/// 表示一个文档切块，是 RAG 系统中的最小检索单元
/// </summary>
[Serializable]
public class DocumentChunk
{
    /// <summary>唯一 ID</summary>
    public string Id;

    /// <summary>来源文件名</summary>
    public string SourceFile;

    /// <summary>块的文本内容</summary>
    public string Content;

    /// <summary>在原文档中的起始字符索引</summary>
    public int StartIndex;

    /// <summary>TF-IDF 或其他 Embedding 向量（存 JSON 用）</summary>
    public float[] Vector;

    public DocumentChunk() { }

    public DocumentChunk(string id, string sourceFile, string content, int startIndex)
    {
        Id = id;
        SourceFile = sourceFile;
        Content = content;
        StartIndex = startIndex;
    }
}
