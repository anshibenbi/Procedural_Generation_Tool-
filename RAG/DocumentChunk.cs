using System;

/// <summary>
/// 表示一个文档切块，是 RAG 系统中的最小检索单元
/// </summary>
[Serializable]
public class DocumentChunk
{
    public string Id          { get; set; }
    public string SourceFile  { get; set; }
    public string Content     { get; set; }
    public int    StartIndex  { get; set; }
    public float[] Vector     { get; set; }

    public DocumentChunk() { }

    public DocumentChunk(string id, string sourceFile, string content, int startIndex)
    {
        Id          = id;
        SourceFile  = sourceFile;
        Content     = content;
        StartIndex  = startIndex;
    }
}
