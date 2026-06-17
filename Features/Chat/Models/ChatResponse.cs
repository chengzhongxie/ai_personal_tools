namespace PersonalAssistant.Features.Chat.Models;

/// <summary>
/// AI 聊天服务的响应结果
/// </summary>
public class ChatResponse
{
    /// <summary>AI 回复的文本内容</summary>
    public string Content { get; set; } = string.Empty;
    /// <summary>本次回复中调用的工具名称列表</summary>
    public List<string> ToolCalls { get; set; } = new();
    /// <summary>是否发生错误</summary>
    public bool IsError { get; set; }
    /// <summary>错误描述信息（仅当 IsError 为 true 时有效）</summary>
    public string ErrorMessage { get; set; } = string.Empty;
}
