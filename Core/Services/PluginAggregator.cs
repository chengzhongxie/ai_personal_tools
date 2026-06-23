using Microsoft.Extensions.AI;
using PersonalAssistant.Core.Interfaces;
using PersonalAssistant.Core.Plugins;
using PersonalAssistant.Features.Workflow.Models;
using PersonalAssistant.Features.Workflow.Services;
using PersonalAssistant.Infrastructure.Common.Services;
using Serilog;

namespace PersonalAssistant.Core.Services;

/// <summary>
/// 插件聚合器：中心枢纽，替代 ChatAgentService 中的 27 元素 AIFunction 数组和 27-case switch。
/// 通过 DI IEnumerable&lt;IToolPlugin&gt; 自动发现所有内置插件 + PluginLoader 加载外部插件。
/// 外部插件优先于内置插件（允许覆盖同名工具）。
/// 内建 WorkflowRecorder 集成（透明录制，插件无需手动调用 RecordStep）。
/// 实现 IDangerousToolPolicy（维护危险工具集合 + Confirm 回调）。
/// 双列表架构：_allPlugins（完整列表供管理窗口枚举）vs _activePlugins（仅启用的，供查询使用）。
/// 资源成本：1个单例，持有 2 个 List&lt;IToolPlugin&gt;，ExecuteToolStepAsync 线性扫描 O(n)。
/// </summary>
public class PluginAggregator : IToolPluginHost, IDangerousToolPolicy
{
    private readonly List<IToolPlugin> _allPlugins;
    private readonly List<IToolPlugin> _activePlugins;
    private readonly PluginStateService _pluginState;
    private readonly WorkflowRecorder _recorder;

    /// <summary>高危工具集合</summary>
    private static readonly HashSet<string> DangerousTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "run_shell", "write_file", "delete_workflow", "delete_schedule"
    };

    /// <summary>不录制的工具（管理工具和 local_llm 不录制到 WorkflowRecorder）</summary>
    private static readonly HashSet<string> NonRecordableTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "list_workflows", "run_workflow", "delete_workflow", "save_workflow",
        "list_schedules", "delete_schedule", "clear_chat",
        "read_clipboard", "write_clipboard", "notify", "local_llm"
    };

    /// <summary>待确认的模式建议</summary>
    public PatternMatch? PendingSuggestion { get; set; }

    // IDangerousToolPolicy
    public Func<string, string, bool>? DangerConfirmation { get; set; }

    public bool ConfirmDangerous(string toolName, string argsSummary)
    {
        if (!IsDangerous(toolName))
            return true;
        if (DangerConfirmation is null)
            return true;
        return DangerConfirmation(toolName, argsSummary);
    }

    public bool IsDangerous(string toolName) => DangerousTools.Contains(toolName);

    public PluginAggregator(IEnumerable<IToolPlugin> builtInPlugins,
        PluginLoader pluginLoader, WorkflowRecorder recorder,
        PluginStateService pluginState)
    {
        _recorder = recorder;
        _pluginState = pluginState;

        // 1. 加载外部插件并包装为 ExternalPluginAdapter
        var externalBases = pluginLoader.LoadPlugins();
        var externalAdapters = externalBases.Select(p => new ExternalPluginAdapter(p)).ToList();

        // 合并：外部插件优先 → 内置插件其次
        _allPlugins = new List<IToolPlugin>();
        _allPlugins.AddRange(externalAdapters);
        _allPlugins.AddRange(builtInPlugins);

        // 根据启用状态过滤活跃插件
        _activePlugins = _allPlugins
            .Where(p => _pluginState.IsEnabled(p.Name))
            .ToList();

        // 2. 检测工具名冲突（外部插件覆盖内置工具）
        var builtInToolNames = builtInPlugins
            .SelectMany(p => p.GetTools())
            .Select(t => t.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var externalToolNames = externalAdapters
            .SelectMany(p => p.GetTools())
            .Select(t => t.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var conflicts = externalToolNames.Intersect(builtInToolNames).ToList();
        foreach (var conflict in conflicts)
            Log.Warning("[PluginAggregator] 外部插件覆盖内置工具: {ToolName}", conflict);

        var disabledCount = _allPlugins.Count - _activePlugins.Count;
        if (disabledCount > 0)
            Log.Information("[PluginAggregator] {Count} 个插件已禁用", disabledCount);
    }

    /// <summary>所有插件（含禁用的），供管理窗口枚举</summary>
    public IReadOnlyList<IToolPlugin> AllPlugins => _allPlugins;

    // IToolPluginHost

    /// <summary>遍历启用的插件收集 AIFunction</summary>
    public AIFunction[] GetAllTools()
    {
        return _activePlugins.SelectMany(p => p.GetTools()).ToArray();
    }

    /// <summary>
    /// 遍历启用的插件调用 TryExecuteToolAsync，返回首个非 null 结果。
    /// 自动录制系统工具到 WorkflowRecorder。
    /// </summary>
    public async Task<string> ExecuteToolStepAsync(string toolName, string args)
    {
        // 透明录制：系统工具录制，管理工具和 local_llm 不录制
        if (!NonRecordableTools.Contains(toolName))
            _recorder.RecordStep(toolName, args);

        foreach (var plugin in _activePlugins)
        {
            var result = await plugin.TryExecuteToolAsync(toolName, args);
            if (result is not null)
                return result;
        }

        return $"未知工具: {toolName}";
    }

    /// <summary>聚合启用的插件的提示词片段</summary>
    public string GetAggregatedPrompt()
    {
        var fragments = _activePlugins
            .Select(p => p.GetPromptFragment())
            .Where(f => f is not null)
            .Select(f => f!);

        return string.Join("\n", fragments);
    }
}
