using PersonalAssistant.Features.Chat.Models;

namespace PersonalAssistant.Features.Chat.Services;

/// <summary>
/// AI 聊天服务接口，封装 DeepSeek API 调用和工具调用循环
/// </summary>
public interface IChatService
{
    /// <summary>
    /// 发送用户消息并获取 AI 回复（含工具调用结果）
    /// </summary>
    /// <param name="userMessage">用户输入的消息文本</param>
    /// <returns>AI 回复结果，包含文本内容和工具调用记录</returns>
    Task<ChatResponse> SendMessageAsync(string userMessage);

    /// <summary>
    /// 清空当前对话历史，保留系统提示
    /// </summary>
    void ClearHistory();
}
