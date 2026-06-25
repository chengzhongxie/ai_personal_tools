using System.ComponentModel;
using PersonalAssistant.Core.Interfaces;
using PersonalAssistant.Features.Scheduler.Models;
using PersonalAssistant.Features.Scheduler.Services;

namespace PersonalAssistant.Features.Plugins.SchedulerPlugin;

/// <summary>
/// 定时任务工具方法静态实现：add_schedule、list_schedules、delete_schedule
/// </summary>
internal static class SchedulerToolMethods
{
    [Description(
        "Create a scheduled task inside this app (NOT a Windows Task Scheduler task).\n" +
        "Use this when the user wants to schedule something to run on a recurring basis.\n" +
        "The app must be running for the task to execute.\n" +
        "Parameters:\n" +
        "  time: HH:mm format for daily tasks (e.g., '09:00', '14:30'), OR a cron expression for flexible schedules:\n" +
        "        5-field cron: minute hour day month weekday (e.g., '0 9 * * 1-5' = weekdays at 9am, '*/30 * * * *' = every 30 min)\n" +
        "  toolName: one of the available tools (e.g., 'run_shell', 'run_command', 'web_fetch', etc.)\n" +
        "  args: the arguments to pass to the tool")]
    public static string AddSchedule(
        [Description("Time in HH:mm format (e.g. '09:00') or 5-field cron expression (e.g. '0 9 * * 1-5' for weekdays at 9am)")] string time,
        [Description("Name of the tool to execute, e.g. 'run_shell', 'run_command'")] string toolName,
        [Description("Arguments to pass to the tool")] string args,
        SchedulerStorageService storage,
        IToolPluginHost pluginHost)
    {
        // Check if input is a cron expression (contains * or spaces)
        bool isCron = time.Contains('*') || time.Count(c => c == ' ') >= 4;
        if (!isCron && (!TimeSpan.TryParse(time, out var ts) || ts.TotalMinutes < 0 || ts.TotalDays >= 1))
            return $"无效的时间格式: \"{time}\"。请使用 HH:mm 格式（如 09:00）或 5 字段 cron 表达式（如 \"0 9 * * 1-5\"）。";

        // Validate tool exists
        var allTools = pluginHost.GetAllTools();
        var knownNames = allTools.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!knownNames.Contains(toolName))
            return $"未知工具: \"{toolName}\"。已知工具: {string.Join(", ", knownNames.OrderBy(t => t))}";

        var safeTime = time.Replace(":", "").Replace(" ", "_").Replace("*", "star");
        var taskName = $"{toolName}_{safeTime}";

        var task = new ScheduledTask
        {
            Name = taskName,
            TimeOfDay = isCron ? "" : time,
            CronExpression = isCron ? time : null,
            ToolName = toolName,
            ToolArgs = args,
            IsEnabled = true,
            CreatedAt = DateTime.Now
        };
        storage.Save(task);

        var scheduleDesc = isCron ? $"cron: {time}" : $"每天 {time}";
        return $"定时任务已创建: {scheduleDesc} 执行 {toolName} {args}\n任务名称: {taskName}\n使用 list_schedules 查看所有任务，delete_schedule(\"{taskName}\") 删除。";
    }

    [Description("List all scheduled daily tasks")]
    public static string ListSchedules(SchedulerStorageService storage)
    {
        var names = storage.ListAll();
        if (names.Count == 0)
            return "没有已保存的定时任务。使用 add_schedule(time, toolName, args) 创建。";

        var lines = new List<string> { "已保存的定时任务:" };
        foreach (var taskName in names)
        {
            var task = storage.Load(taskName);
            if (task is null) continue;

            var status = task.IsEnabled ? "启用" : "禁用";
            var lastRun = task.LastRunTimestamp is not null
                ? $"上次运行: {task.LastRunTimestamp}"
                : "从未运行";
            var schedule = task.GetScheduleDescription();
            lines.Add($"  [{status}] {task.Name} — {schedule} → {task.ToolName} {task.ToolArgs} ({lastRun})");
        }
        return string.Join("\n", lines);
    }

    [Description("Delete a scheduled daily task by name")]
    public static string DeleteSchedule(
        [Description("Name of the scheduled task to delete")] string name,
        SchedulerStorageService storage,
        IDangerousToolPolicy policy)
    {
        if (!policy.ConfirmDangerous("delete_schedule", name))
            return $"用户取消了删除定时任务 \"{name}\" 的操作。";
        var deleted = storage.Delete(name);
        return deleted
            ? $"定时任务 \"{name}\" 已删除。"
            : $"定时任务 \"{name}\" 未找到。使用 list_schedules 查看已保存列表。";
    }
}
