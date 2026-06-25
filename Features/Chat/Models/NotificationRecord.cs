namespace PersonalAssistant.Features.Chat.Models;

/// <summary>
/// 通知历史记录
/// </summary>
public class NotificationRecord
{
    /// <summary>通知标题</summary>
    public string Title { get; init; } = "";

    /// <summary>通知内容</summary>
    public string Message { get; init; } = "";

    /// <summary>触发时间</summary>
    public DateTime Timestamp { get; init; } = DateTime.Now;

    /// <summary>通知来源</summary>
    public string Source { get; init; } = "系统";

    /// <summary>简短时间显示</summary>
    public string TimeDisplay => Timestamp.ToString("MM-dd HH:mm:ss");
}
