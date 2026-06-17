using PersonalAssistant.Features.Chat.Models.Enums;

namespace PersonalAssistant.Features.Chat.Models;

/// <summary>
/// 聊天界面中显示的一条消息
/// </summary>
public class ChatMessage
{
    /// <summary>消息角色（用户/助手/系统/工具）</summary>
    public MessageRole Role { get; set; }
    /// <summary>消息文本内容</summary>
    public string Content { get; set; } = string.Empty;
    /// <summary>消息时间戳</summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;
    /// <summary>本次回复中调用的工具名称列表</summary>
    public List<string> ToolCalls { get; set; } = new();
    /// <summary>是否为错误消息</summary>
    public bool IsError { get; set; }
}
