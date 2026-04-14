using System.Collections.Generic;

/// <summary>
/// Embedding 服务接口：所有向量化方案都实现此接口
/// 当前实现：TFIDFEmbeddingService
/// 未来可替换为：LLamaEmbeddingService、OpenAIEmbeddingService 等
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// 向量维度（不同实现维度不同）
    /// </summary>
    int Dimensions { get; }

    /// <summary>
    /// 用语料库训练 / 初始化（TF-IDF 需要；神经网络 Embedding 无需此步）
    /// </summary>
    /// <param name="corpus">所有文档块的文本集合</param>
    void Fit(IEnumerable<string> corpus);

    /// <summary>
    /// 将一段文本转换为向量
    /// </summary>
    float[] GetEmbedding(string text);
}
