using System.Text.Json.Serialization;

namespace PersonalAssistant.Features.Scheduler.Models;

/// <summary>
/// 每日定时任务定义，持久化到 %APPDATA%\PersonalAssistant\schedules\
/// </summary>
public class ScheduledTask
{
    /// <summary>任务名称（唯一标识）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>每日触发时间，格式 HH:mm</summary>
    public string TimeOfDay { get; set; } = string.Empty;

    /// <summary>要执行的工具名称</summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>工具参数</summary>
    public string ToolArgs { get; set; } = string.Empty;

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>上次运行日期（用于同一天防重复）。null 表示从未运行。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastRunDate { get; set; }
}
