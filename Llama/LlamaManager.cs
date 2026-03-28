using LLama;
using LLama.Common;
using LLama.Sampling;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 本地模型推理管理器
/// 纯 C#，无任何 Unity 或平台依赖
/// 线程调度由调用方负责，库本身不做任何线程切换
/// </summary>
public class LlamaManager : IDisposable
{
    // ─────────────────────────────────────────────
    //  私有成员
    // ─────────────────────────────────────────────

    private readonly LlamaConfig _config;
    private readonly Action<string> _onLog;    // 普通日志，由调用方决定如何输出
    private readonly Action<string> _onError;  // 错误日志，由调用方决定如何输出

    private LLamaWeights _model;
    private ModelParams _modelParams;
    private bool _isReady = false;
    private bool _disposed = false;

    // ─────────────────────────────────────────────
    //  公开属性
    // ─────────────────────────────────────────────

    public bool IsReady => _isReady;

    // ─────────────────────────────────────────────
    //  构造函数
    // ─────────────────────────────────────────────

    /// <param name="config">模型配置</param>
    /// <param name="onLog">普通日志回调，传 null 则静默</param>
    /// <param name="onError">错误日志回调，传 null 则静默</param>
    public LlamaManager(
        LlamaConfig config,
        Action<string> onLog = null,
        Action<string> onError = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _onLog = onLog ?? (_ => { });
        _onError = onError ?? (_ => { });
    }

    // ─────────────────────────────────────────────
    //  初始化：调用方手动调用，替代原来的 Start()
    // ─────────────────────────────────────────────

    /// <summary>
    /// 异步加载模型，调用方在合适的时机主动调用
    /// 例如 Unity 的 Start()，或 WPF 的 Window.Loaded 事件
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isReady)
        {
            _onLog("[LlamaManager] 模型已加载，无需重复初始化");
            return;
        }

        try
        {
            _onLog($"[LlamaManager] 开始加载模型: {_config.ModelPath}");

            _modelParams = new ModelParams(_config.ModelPath)
            {
                ContextSize = _config.ContextSize,
                GpuLayerCount = _config.GpuLayerCount
            };

            _model = await LLamaWeights.LoadFromFileAsync(_modelParams, cancellationToken);
            _isReady = true;

            _onLog("[LlamaManager] 模型加载完成");
        }
        catch (Exception ex)
        {
            _onError($"[LlamaManager] 模型加载失败: {ex.Message}");
            if (ex.InnerException != null)
                _onError($"[LlamaManager] 内部异常: {ex.InnerException.Message}");
            throw; // 重新抛出，让调用方决定如何处理
        }
    }

    // ─────────────────────────────────────────────
    //  非流式接口
    // ─────────────────────────────────────────────

    /// <summary>
    /// 发送消息并等待完整回复
    /// </summary>
    /// <returns>模型回复内容；失败时抛出异常</returns>
    public async Task<string> ChatAsync(
        List<LlamaMessage> messages,
        CancellationToken cancellationToken = default)
    {
        EnsureReady();

        try
        {
            using var context = _model.CreateContext(_modelParams);
            var executor = new InstructExecutor(context);
            string prompt = BuildQwenPrompt(messages);

            var response = new StringBuilder();
            await foreach (var token in executor.InferAsync(
                prompt,
                BuildInferenceParams(),
                cancellationToken
            ))
            {
                response.Append(token);
            }

            return CleanResponse(response.ToString());
        }
        catch (OperationCanceledException)
        {
            _onLog("[LlamaManager] 推理已取消");
            throw;
        }
        catch (Exception ex)
        {
            _onError($"[LlamaManager] 推理失败: {ex.Message}");
            throw;
        }
    }

    // ─────────────────────────────────────────────
    //  流式接口
    // ─────────────────────────────────────────────

    /// <summary>
    /// 流式推理，每生成一个 token 触发 onChunk
    /// 注意：回调在推理线程上调用，调用方负责切换到 UI 线程
    /// </summary>
    public async Task ChatStreamAsync(
        List<LlamaMessage> messages,
        Action<string> onChunk,
        Action<string> onComplete = null,
        Action<string> onError = null,
        CancellationToken cancellationToken = default)
    {
        EnsureReady();

        try
        {
            using var context = _model.CreateContext(_modelParams);
            var executor = new InstructExecutor(context);
            string prompt = BuildQwenPrompt(messages);

            var fullText = new StringBuilder();
            await foreach (var token in executor.InferAsync(
                prompt,
                BuildInferenceParams(),
                cancellationToken
            ))
            {
                if (cancellationToken.IsCancellationRequested) break;

                fullText.Append(token);
                onChunk?.Invoke(token);
            }

            string finalText = CleanResponse(fullText.ToString());
            onComplete?.Invoke(finalText);
        }
        catch (OperationCanceledException)
        {
            _onLog("[LlamaManager] 流式推理已取消");
            throw;
        }
        catch (Exception ex)
        {
            string errorMsg = $"推理失败: {ex.Message}";
            _onError($"[LlamaManager] {errorMsg}");
            onError?.Invoke(errorMsg);
            throw;
        }
    }

    // ─────────────────────────────────────────────
    //  IDisposable
    // ─────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _model?.Dispose();
        _disposed = true;
        _onLog("[LlamaManager] 已释放模型资源");
    }

    // ─────────────────────────────────────────────
    //  私有工具方法
    // ─────────────────────────────────────────────

    private void EnsureReady()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(LlamaManager));
        if (!_isReady)
            throw new InvalidOperationException("模型尚未加载，请先调用 InitializeAsync()");
    }

    private InferenceParams BuildInferenceParams() => new InferenceParams
    {
        MaxTokens = _config.MaxTokens,
        AntiPrompts = new List<string>
        {
            "<|im_end|>",
            "<|im_start|>",
            "<|endoftext|>"
        },
        SamplingPipeline = new DefaultSamplingPipeline
        {
            Temperature = _config.Temperature,
            RepeatPenalty = _config.RepeatPenalty
        }
    };

    /// <summary>
    /// 拼接 Qwen3 的 ChatML 格式 Prompt
    /// </summary>
    private string BuildQwenPrompt(List<LlamaMessage> messages)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            switch (msg.Role)
            {
                case "system":
                    sb.Append($"<|im_start|>system\n{msg.Content}<|im_end|>\n");
                    break;
                case "user":
                    // 最后一条 user 消息加 /no_think，关闭 Qwen3 思维链
                    string content = (i == messages.Count - 1)
                        ? $"{msg.Content} /no_think"
                        : msg.Content;
                    sb.Append($"<|im_start|>user\n{content}<|im_end|>\n");
                    break;
                case "assistant":
                    sb.Append($"<|im_start|>assistant\n{msg.Content}<|im_end|>\n");
                    break;
            }
        }
        // 引导模型开始回复
        sb.Append("<|im_start|>assistant\n");
        return sb.ToString();
    }

    /// <summary>
    /// 清理模型输出中的多余内容
    /// </summary>
    private string CleanResponse(string response)
    {
        // 去掉 <think>...</think> 整个块
        int thinkStart = response.IndexOf("<think>", StringComparison.Ordinal);
        int thinkEnd = response.IndexOf("</think>", StringComparison.Ordinal);
        if (thinkStart >= 0 && thinkEnd >= 0)
            response = response[(thinkEnd + "</think>".Length)..];

        // 去掉停止符
        response = response
            .Replace("<|im_end|>", "")
            .Replace("<|im_start|>", "")
            .Replace("<|endoftext|>", "")
            .Trim();

        // 截断模型自我续写的对话
        var cutoffs = new[] { "\nUser:", "\nAI:", "\n用户：", "\nAI：" };
        foreach (var cutoff in cutoffs)
        {
            int idx = response.IndexOf(cutoff, StringComparison.Ordinal);
            if (idx >= 0)
                response = response[..idx];
        }

        return response.Trim();
    }
}
