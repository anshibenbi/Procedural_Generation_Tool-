/// <summary>
/// 对话消息，纯 C#，无任何框架依赖
/// role: "system" | "user" | "assistant"
/// </summary>
public class LlamaMessage
{
    public string Role { get; }
    public string Content { get; }

    public LlamaMessage(string role, string content)
    {
        Role = role;
        Content = content;
    }
}
