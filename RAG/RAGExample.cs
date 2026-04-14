using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// RAG 使用示例：挂到场景中的任意 GameObject 上
/// 演示非流式查询和流式查询两种用法
/// </summary>
public class RAGExample : MonoBehaviour
{
    [Header("UI（可选，不连也能跑）")]
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private TMP_Text answerText;
    [SerializeField] private Button askButton;
    [SerializeField] private Button askStreamButton;

    private CancellationTokenSource _cts;

    private void Start()
    {
        askButton?.onClick.AddListener(OnAskClick);
        askStreamButton?.onClick.AddListener(OnAskStreamClick);
    }

    private void OnDestroy()
    {
        _cts?.Cancel();
    }

    // ─────────────────────────────────────────────────────
    //  非流式查询
    // ─────────────────────────────────────────────────────

    private async void OnAskClick()
    {
        if (!RAGManager.Instance.IsReady)
        {
            Debug.LogWarning("RAG 尚未就绪");
            return;
        }

        string question = inputField != null ? inputField.text : "这份文档的主要内容是什么？";
        if (answerText) answerText.text = "思考中...";

        _cts = new CancellationTokenSource();
        string answer = await RAGManager.Instance.QueryAsync(question, cancellationToken: _cts.Token);

        if (answerText) answerText.text = answer ?? "（无回答）";
        Debug.Log($"[RAGExample] 回答：{answer}");
    }

    // ─────────────────────────────────────────────────────
    //  流式查询（逐 token 更新 UI）
    // ─────────────────────────────────────────────────────

    private async void OnAskStreamClick()
    {
        if (!RAGManager.Instance.IsReady)
        {
            Debug.LogWarning("RAG 尚未就绪");
            return;
        }

        string question = inputField != null ? inputField.text : "这份文档的主要内容是什么？";
        if (answerText) answerText.text = "";

        _cts = new CancellationTokenSource();

        await RAGManager.Instance.QueryStreamAsync(
            question,
            onChunk: chunk =>
            {
                // 每收到一个 token 就更新 UI（已在主线程回调）
                if (answerText) answerText.text += chunk;
            },
            onComplete: fullAnswer =>
            {
                Debug.Log($"[RAGExample] 流式完成：{fullAnswer}");
            },
            onError: err =>
            {
                Debug.LogError($"[RAGExample] 错误：{err}");
                if (answerText) answerText.text = $"错误：{err}";
            },
            cancellationToken: _cts.Token
        );
    }

    // ─────────────────────────────────────────────────────
    //  代码调用示例（不依赖 UI）
    // ─────────────────────────────────────────────────────

    [ContextMenu("测试：非流式查询")]
    private async void TestQueryAsync()
    {
        string question = "文档中提到了什么？";
        Debug.Log($"[RAGExample] 发出问题：{question}");

        string answer = await RAGManager.Instance.QueryAsync(question);
        Debug.Log($"[RAGExample] 回答：{answer}");
    }

    [ContextMenu("测试：查看检索结果")]
    private void TestRetrieve()
    {
        if (!RAGManager.Instance.IsReady) return;

        var results = RAGManager.Instance.RetrieveDetailed("文档主题");
        foreach (var r in results)
        {
            Debug.Log($"[Score={r.Score:F3}][{r.Chunk.SourceFile}] {r.Chunk.Content[..Mathf.Min(80, r.Chunk.Content.Length)]}...");
        }
    }
}
