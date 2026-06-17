namespace PersonalAssistant.Features.Chat.Models.Enums;

/// <summary>
/// 聊天消息的角色类型
/// </summary>
public enum MessageRole
{
    /// <summary>用户消息</summary>
    User,
    /// <summary>AI 助手回复</summary>
    Assistant,
    /// <summary>系统提示消息</summary>
    System,
    /// <summary>工具调用结果</summary>
    Tool
}
