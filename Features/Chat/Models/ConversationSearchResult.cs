namespace PersonalAssistant.Features.Chat.Models;

/// <summary>
/// 对话搜索结果 POCO。
/// </summary>
public sealed class ConversationSearchResult
{
    public string ConversationId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Excerpt { get; set; } = string.Empty;
    public int MessageIndex { get; set; }
}
