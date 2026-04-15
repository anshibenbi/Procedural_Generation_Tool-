using System.Collections.Generic;

/// <summary>
/// Embedding 服务接口
/// 当前实现：TFIDFEmbeddingService
/// 未来可替换为任意向量化方案
/// </summary>
public interface IEmbeddingService
{
    /// <summary>向量维度</summary>
    int Dimensions { get; }

    /// <summary>
    /// 用语料库训练（TF-IDF 需要；神经网络 Embedding 可留空）
    /// </summary>
    void Fit(IEnumerable<string> corpus);

    /// <summary>将文本转换为向量</summary>
    float[] GetEmbedding(string text);
}
