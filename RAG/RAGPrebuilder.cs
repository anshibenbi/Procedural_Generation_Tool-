#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// RAG 知识库预构建工具（仅在 Editor 中运行）
///
/// 使用方式：
///   Unity 菜单栏 → Tools → RAG → 预构建知识库
///
/// 作用：
///   在开发阶段提前构建向量库，将结果保存到
///   Assets/StreamingAssets/rag_vectorstore_prebuilt.json
///   打包时此文件会随游戏一起发布。
///   玩家启动游戏时直接加载，无需等待构建过程。
/// </summary>
public class RAGPrebuilder : EditorWindow
{
    // ── 参数（与 RAGManager Inspector 保持一致）─────────────
    private int _chunkSize = 400;
    private int _chunkOverlap = 50;
    private int _maxVocabSize = 8192;

    // ── 状态 ────────────────────────────────────────────────
    private string _statusMessage = "点击"构建"开始预构建知识库";
    private bool _isBuilding = false;
    private int _chunkCount = 0;
    private int _vocabSize = 0;

    // ── 预构建产物的保存路径（StreamingAssets 内）───────────
    private const string OutputFileName = "rag_vectorstore_prebuilt.json";

    // ─────────────────────────────────────────────────────────
    //  菜单入口
    // ─────────────────────────────────────────────────────────

    [MenuItem("Tools/RAG/预构建知识库 &r")]
    public static void OpenWindow()
    {
        var window = GetWindow<RAGPrebuilder>("RAG 预构建工具");
        window.minSize = new Vector2(400, 280);
        window.Show();
    }

    // ─────────────────────────────────────────────────────────
    //  Editor UI
    // ─────────────────────────────────────────────────────────

    private void OnGUI()
    {
        GUILayout.Space(10);
        EditorGUILayout.LabelField("RAG 知识库预构建工具", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "此工具在开发阶段将知识库提前向量化，生成的 JSON 文件会随游戏一起打包。\n" +
            "玩家启动游戏时直接加载，无需等待。\n\n" +
            "每次修改 KnowledgeBase 目录中的文档后，重新运行此工具。",
            MessageType.Info
        );

        GUILayout.Space(10);
        EditorGUILayout.LabelField("构建参数", EditorStyles.boldLabel);

        _chunkSize    = EditorGUILayout.IntField("Chunk Size（每块字符数）", _chunkSize);
        _chunkOverlap = EditorGUILayout.IntField("Chunk Overlap（重叠字符数）", _chunkOverlap);
        _maxVocabSize = EditorGUILayout.IntField("Max Vocab Size（词汇表上限）", _maxVocabSize);

        GUILayout.Space(5);
        EditorGUILayout.LabelField("输出路径", EditorStyles.boldLabel);

        string outputPath = Path.Combine(Application.streamingAssetsPath, OutputFileName);
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.TextField("保存到", outputPath);
        EditorGUI.EndDisabledGroup();

        GUILayout.Space(15);

        // 状态显示
        var statusStyle = new GUIStyle(EditorStyles.helpBox)
        {
            fontSize = 12,
            wordWrap = true
        };
        EditorGUILayout.LabelField(_statusMessage, statusStyle, GUILayout.MinHeight(40));

        // 构建结果
        if (_chunkCount > 0)
        {
            EditorGUILayout.LabelField($"✅ 块数量：{_chunkCount}　词汇表：{_vocabSize} 词");
        }

        GUILayout.Space(10);

        EditorGUI.BeginDisabledGroup(_isBuilding);
        if (GUILayout.Button(_isBuilding ? "构建中..." : "▶  开始预构建", GUILayout.Height(36)))
        {
            BuildAsync();
        }
        EditorGUI.EndDisabledGroup();

        if (File.Exists(outputPath))
        {
            GUILayout.Space(5);
            if (GUILayout.Button("🗑  删除已有预构建文件（强制下次重建）"))
            {
                File.Delete(outputPath);
                string metaPath = outputPath + ".meta";
                if (File.Exists(metaPath)) File.Delete(metaPath);
                AssetDatabase.Refresh();
                _statusMessage = "已删除预构建文件";
                _chunkCount = 0;
                _vocabSize = 0;
            }
        }
    }

    // ─────────────────────────────────────────────────────────
    //  预构建流程
    // ─────────────────────────────────────────────────────────

    private async void BuildAsync()
    {
        _isBuilding = true;
        _statusMessage = "正在读取文档...";
        _chunkCount = 0;
        _vocabSize = 0;
        Repaint();

        try
        {
            // Step 1：加载文档
            var documents = await DocumentLoader.LoadAllAsync();
            if (documents.Count == 0)
            {
                _statusMessage = "❌ KnowledgeBase 目录为空，请先放入 .txt 文件";
                _isBuilding = false;
                Repaint();
                return;
            }

            _statusMessage = $"已加载 {documents.Count} 个文档，正在切块...";
            Repaint();

            // Step 2：切块
            var splitter = new TextSplitter(_chunkSize, _chunkOverlap);
            var allChunks = new List<DocumentChunk>();
            foreach (var (fileName, content) in documents)
                allChunks.AddRange(splitter.Split(fileName, content));

            _statusMessage = $"切块完成（{allChunks.Count} 块），正在建立词汇表...";
            Repaint();

            // Step 3：Fit TF-IDF
            var embedding = new TFIDFEmbeddingService(_maxVocabSize);
            await Task.Run(() => embedding.Fit(allChunks.Select(c => c.Content)));

            _statusMessage = $"词汇表构建完成（{embedding.Dimensions} 词），正在向量化...";
            Repaint();

            // Step 4：向量化
            await Task.Run(() =>
            {
                foreach (var chunk in allChunks)
                    chunk.Vector = embedding.GetEmbedding(chunk.Content);
            });

            _statusMessage = "向量化完成，正在保存...";
            Repaint();

            // Step 5：保存到 StreamingAssets
            string outputPath = Path.Combine(Application.streamingAssetsPath, OutputFileName);
            Directory.CreateDirectory(Application.streamingAssetsPath);

            // 用 Newtonsoft 或自定义序列化来处理 float[]
            // 这里用一个简单的自定义 JSON 序列化避免引入依赖
            string json = SerializeChunks(allChunks);
            await File.WriteAllTextAsync(outputPath, json, System.Text.Encoding.UTF8);

            // 刷新 AssetDatabase，让 Unity 识别新文件
            AssetDatabase.Refresh();

            _chunkCount = allChunks.Count;
            _vocabSize = embedding.Dimensions;

            long fileSizeKB = new FileInfo(outputPath).Length / 1024;
            _statusMessage = $"✅ 构建成功！\n{documents.Count} 个文档 → {allChunks.Count} 个块\n" +
                             $"词汇表：{embedding.Dimensions} 词\n" +
                             $"文件大小：{fileSizeKB} KB\n" +
                             $"路径：{outputPath}";

            Debug.Log($"[RAGPrebuilder] 预构建完成：{allChunks.Count} 块，{embedding.Dimensions} 词，{fileSizeKB} KB");
        }
        catch (System.Exception ex)
        {
            _statusMessage = $"❌ 构建失败：{ex.Message}";
            Debug.LogError($"[RAGPrebuilder] {ex}");
        }
        finally
        {
            _isBuilding = false;
            Repaint();
        }
    }

    // ─────────────────────────────────────────────────────────
    //  自定义序列化（避免 JsonUtility 对 float[] 的限制）
    //  格式：JSON 数组，每个元素是一个 chunk 对象
    // ─────────────────────────────────────────────────────────

    private static string SerializeChunks(List<DocumentChunk> chunks)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("{\"Chunks\":[");

        for (int i = 0; i < chunks.Count; i++)
        {
            var c = chunks[i];
            sb.Append("{");
            sb.Append($"\"Id\":{EscapeJson(c.Id)},");
            sb.Append($"\"SourceFile\":{EscapeJson(c.SourceFile)},");
            sb.Append($"\"Content\":{EscapeJson(c.Content)},");
            sb.Append($"\"StartIndex\":{c.StartIndex},");
            sb.Append("\"Vector\":[");
            for (int j = 0; j < c.Vector.Length; j++)
            {
                sb.Append(c.Vector[j].ToString("G6",
                    System.Globalization.CultureInfo.InvariantCulture));
                if (j < c.Vector.Length - 1) sb.Append(',');
            }
            sb.Append("]}");
            if (i < chunks.Count - 1) sb.Append(',');
        }

        sb.Append("]}");
        return sb.ToString();
    }

    private static string EscapeJson(string s)
    {
        if (s == null) return "null";
        return "\"" + s
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t")
            + "\"";
    }
}
#endif
