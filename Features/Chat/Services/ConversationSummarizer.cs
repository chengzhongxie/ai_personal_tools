using System.Text;
using PersonalAssistant.Features.Chat.Models;
using PersonalAssistant.Features.Chat.Models.Enums;
using Serilog;

namespace PersonalAssistant.Features.Chat.Services;

/// <summary>
/// 对话摘要器：当对话轮数超过阈值时，用本地模型生成摘要注入会话上下文。
/// 资源成本：仅触发时消耗本地 CPU（约 2-3s），空闲时零开销。
/// </summary>
public class ConversationSummarizer
{
    private readonly LocalModelService _localModel;
    private int _roundCount;
    private string? _latestSummary;

    private const int SummarizeThreshold = 30;
    private const int RoundsToSummarize = 10;
    private const string SummarizePrompt =
        "You are a conversation summarizer. Below is a conversation between a user and an AI assistant. " +
        "Summarize the key topics, decisions, and important information in 2-3 concise sentences. " +
        "Write in the same language as the conversation. Focus on what the user asked and what was accomplished.\n\n" +
        "Conversation:\n{0}\n\nSummary:";

    public ConversationSummarizer(LocalModelService localModel)
    {
        _localModel = localModel;
    }

    /// <summary>当前轮数</summary>
    public int RoundCount => _roundCount;

    /// <summary>最新摘要（用于注入系统提示词）</summary>
    public string? LatestSummary => _latestSummary;

    /// <summary>是否需要触发摘要</summary>
    public bool ShouldSummarize => _roundCount >= SummarizeThreshold;

    /// <summary>每轮对话后递增计数器</summary>
    public void IncrementRound()
    {
        _roundCount++;
    }

    /// <summary>重置（新会话时调用）</summary>
    public void Reset()
    {
        _roundCount = 0;
        _latestSummary = null;
    }

    /// <summary>
    /// 从聊天消息列表中提取最旧的 N 轮，用本地模型生成摘要。
    /// </summary>
    /// <param name="messages">完整的消息列表</param>
    /// <returns>摘要文本，失败返回 null</returns>
    public async Task<string?> SummarizeAsync(IReadOnlyList<ChatMessage> messages)
    {
        if (messages.Count == 0)
            return null;

        try
        {
            // 取最旧的 RoundsToSummarize 轮（非系统消息）
            var userAssist = messages
                .Where(m => m.Role is MessageRole.User or MessageRole.Assistant)
                .Take(RoundsToSummarize * 2)
                .ToList();

            if (userAssist.Count == 0)
                return null;

            var conversation = new StringBuilder();
            foreach (var msg in userAssist)
            {
                var role = msg.Role == MessageRole.User ? "用户" : "AI";
                var content = msg.Content.Length > 200
                    ? msg.Content[..200] + "..."
                    : msg.Content;
                conversation.AppendLine($"{role}: {content}");
            }

            var prompt = string.Format(SummarizePrompt, conversation.ToString());
            var result = await _localModel.InferAsync(prompt, maxTokens: 128);

            if (string.IsNullOrWhiteSpace(result) || result.Length < 10)
            {
                Log.Debug("[Summarizer] 摘要生成失败（结果过短）");
                return null;
            }

            _latestSummary = result.Trim();
            _roundCount = 0; // 重置计数器
            Log.Information("[Summarizer] 摘要完成 ({Len} chars), {Removed} rounds summarized",
                _latestSummary.Length, RoundsToSummarize);

            return _latestSummary;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Summarizer] 摘要生成异常");
            return null;
        }
    }
}
