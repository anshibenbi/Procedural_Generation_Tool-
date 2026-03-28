using System;

/// <summary>
/// LlamaManager 的配置数据类，纯 C#，无任何框架依赖
/// </summary>
public class LlamaConfig
{
    /// <summary>
    /// 模型文件的完整路径，例如 /path/to/models/Qwen3-8B-Q4_K_M.gguf
    /// 调用方负责拼接路径，库本身不假设任何目录结构
    /// </summary>
    public string ModelPath { get; set; }

    /// <summary>
    /// 上下文窗口大小，默认 4096
    /// </summary>
    public uint ContextSize { get; set; } = 4096;

    /// <summary>
    /// 卸载到 GPU 的层数，0 = 纯 CPU 推理
    /// </summary>
    public int GpuLayerCount { get; set; } = 0;

    /// <summary>
    /// 单次推理最大 token 数，默认 512
    /// </summary>
    public int MaxTokens { get; set; } = 512;

    /// <summary>
    /// 温度，控制输出随机性，默认 0.8
    /// </summary>
    public float Temperature { get; set; } = 0.8f;

    /// <summary>
    /// 重复惩罚，抑制模型重复输出，默认 1.3
    /// </summary>
    public float RepeatPenalty { get; set; } = 1.3f;
}
