using System.Text.Json.Serialization;

namespace PersonalAssistant.Features.Scheduler.Models;

/// <summary>
/// 定时任务定义，支持 cron 表达式和旧版 HH:mm 格式。
/// 持久化到 %APPDATA%\PersonalAssistant\schedules\
/// </summary>
public class ScheduledTask
{
    /// <summary>任务名称（唯一标识）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>每日触发时间，格式 HH:mm（向后兼容，新版使用 CronExpression）</summary>
    public string TimeOfDay { get; set; } = string.Empty;

    /// <summary>Cron 表达式（5 字段: 分 时 日 月 星期）。null 时回退到 TimeOfDay。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CronExpression { get; set; }

    /// <summary>获取有效的 cron 表达式：优先 CronExpression，否则从 TimeOfDay 推导</summary>
    public string GetCronExpression()
    {
        if (!string.IsNullOrWhiteSpace(CronExpression))
            return CronExpression;

        // 向后兼容：HH:mm → "m h * * *"
        if (TimeSpan.TryParse(TimeOfDay, out var ts) && ts.TotalMinutes >= 0 && ts.TotalDays < 1)
            return $"{ts.Minutes} {ts.Hours} * * *";

        // 默认：每天凌晨 0:00
        return "0 0 * * *";
    }

    /// <summary>获取人类可读的调度描述</summary>
    public string GetScheduleDescription()
    {
        if (!string.IsNullOrWhiteSpace(CronExpression))
            return CronExpression;
        return $"每天 {TimeOfDay}";
    }

    /// <summary>要执行的工具名称</summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>工具参数</summary>
    public string ToolArgs { get; set; } = string.Empty;

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>上次运行时间戳（用于防重复）。null 表示从未运行。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastRunTimestamp { get; set; }
}
