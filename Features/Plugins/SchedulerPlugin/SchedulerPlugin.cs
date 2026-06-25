using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using PersonalAssistant.Core.Interfaces;
using PersonalAssistant.Features.Scheduler.Services;

namespace PersonalAssistant.Features.Plugins.SchedulerPlugin;

/// <summary>
/// 定时任务插件：提供 add_schedule、list_schedules、delete_schedule 三个 AI 工具。
/// 资源成本：1个单例，工具调用时磁盘 I/O 按需消耗。
/// </summary>
public class SchedulerPlugin : IToolPlugin
{
    private readonly SchedulerStorageService _storage;
    private readonly IServiceProvider _services;
    private IToolPluginHost? _pluginHost;
    private IDangerousToolPolicy? _policy;
    private IToolPluginHost PluginHost => _pluginHost ??= _services.GetRequiredService<IToolPluginHost>();
    private IDangerousToolPolicy Policy => _policy ??= _services.GetRequiredService<IDangerousToolPolicy>();

    public string Name => "Scheduler";

    public SchedulerPlugin(SchedulerStorageService storage, IServiceProvider services)
    {
        _storage = storage;
        _services = services;
    }

    public AIFunction[] GetTools()
    {
        return new[]
        {
            AIFunctionFactory.Create(new Func<string, string, string, string>(AddScheduleWrapper), name: "add_schedule"),
            AIFunctionFactory.Create(new Func<string>(ListSchedules), name: "list_schedules"),
            AIFunctionFactory.Create(new Func<string, string>(DeleteScheduleWrapper), name: "delete_schedule"),
        };
    }

    public Task<string?> TryExecuteToolAsync(string toolName, string args)
    {
        string? result = toolName switch
        {
            "add_schedule" => SchedulerToolMethods.AddSchedule(args, "", "", _storage, PluginHost),
            "list_schedules" => SchedulerToolMethods.ListSchedules(_storage),
            "delete_schedule" => SchedulerToolMethods.DeleteSchedule(args, _storage, Policy),
            _ => null
        };

        return Task.FromResult(result);
    }

    public string? GetPromptFragment() => null;

    [Description(
        "Create a daily scheduled task inside this app (NOT a Windows Task Scheduler task).\n" +
        "Use this when the user wants to schedule something to run every day at a specific time.\n" +
        "The app must be running for the task to execute. Each task runs at most once per day.\n" +
        "Parameters:\n" +
        "  time: HH:mm format (e.g., '09:00', '14:30')\n" +
        "  toolName: one of the available tools (e.g., 'run_shell', 'run_command', 'web_fetch', etc.)\n" +
        "  args: the arguments to pass to the tool")]
    private string AddScheduleWrapper(
        [Description("Time in HH:mm format, e.g. '09:00'")] string time,
        [Description("Name of the tool to execute, e.g. 'run_shell', 'run_command'")] string toolName,
        [Description("Arguments to pass to the tool")] string args) =>
        SchedulerToolMethods.AddSchedule(time, toolName, args, _storage, PluginHost);

    [Description("List all scheduled daily tasks")]
    private string ListSchedules() => SchedulerToolMethods.ListSchedules(_storage);

    [Description("Delete a scheduled daily task by name")]
    private string DeleteScheduleWrapper(
        [Description("Name of the scheduled task to delete")] string name) =>
        SchedulerToolMethods.DeleteSchedule(name, _storage, Policy);
}
