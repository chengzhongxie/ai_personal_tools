using System.IO;
using Microsoft.Extensions.AI;
using PersonalAssistant.Core.Interfaces;
using PersonalAssistant.Core.Plugins;
using PersonalAssistant.Features.Plugins;
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
/// 支持插件热重载：订阅 PluginFileWatcher 事件，文件变更时自动重载外部插件。
/// 资源成本：1个单例，持有 2 个 List&lt;IToolPlugin&gt;，ExecuteToolStepAsync 线性扫描 O(n)。
/// </summary>
public class PluginAggregator : IToolPluginHost, IDangerousToolPolicy
{
    private readonly List<IToolPlugin> _allPlugins;
    private List<IToolPlugin> _activePlugins;
    private readonly PluginStateService _pluginState;
    private readonly PluginSharedState _sharedState;
    private readonly WorkflowRecorder _recorder;
    private readonly PluginLoader _pluginLoader;
    private readonly PluginFileWatcher? _fileWatcher;
    private readonly object _pluginsLock = new();

    /// <summary>插件变更事件（ChatAgentService 订阅以重建 MAF Session）</summary>
    public event Action? PluginsChanged;

    // 缓存：避免每次 GetAllTools() 都遍历所有插件重建 AIFunction 数组
    private AIFunction[]? _cachedTools;
    private bool _toolsCacheValid;

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
        PluginStateService pluginState, PluginFileWatcher fileWatcher,
        PluginSharedState sharedState)
    {
        Log.Information("[PluginAggregator] 构造开始");
        _recorder = recorder;
        _pluginState = pluginState;
        _pluginLoader = pluginLoader;
        _fileWatcher = fileWatcher;
        _sharedState = sharedState;

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

        // 3. 订阅插件文件变更事件（热重载）
        _fileWatcher.PluginFileChanged += OnExternalPluginFileChanged;
        Log.Information("[PluginAggregator] 构造完成");
    }

    /// <summary>外部插件文件变更时触发热重载</summary>
    private void OnExternalPluginFileChanged(string filePath)
    {
        try
        {
            var fileName = Path.GetFileName(filePath);

            // 查找对应的 ExternalPluginAdapter（SourcePlugin.SourceFilePath 匹配）
            var oldAdapter = _allPlugins
                .OfType<ExternalPluginAdapter>()
                .FirstOrDefault(a =>
                    string.Equals(a.SourcePlugin.SourceFilePath, filePath, StringComparison.OrdinalIgnoreCase));

            // 重新编译加载
            var newBase = _pluginLoader.ReloadPlugin(filePath);

            lock (_pluginsLock)
            {
                if (newBase is not null)
                {
                    var newAdapter = new ExternalPluginAdapter(newBase);
                    if (oldAdapter is not null)
                    {
                        var idx = _allPlugins.IndexOf(oldAdapter);
                        _allPlugins[idx] = newAdapter;
                    }
                    else
                    {
                        // 新文件，插入到外部插件列表末尾
                        _allPlugins.Insert(0, newAdapter); // 外部插件优先
                        Log.Information("[PluginAggregator] 新插件已添加: {File}", fileName);
                    }
                }
                else if (oldAdapter is not null)
                {
                    // 编译失败，移除旧插件
                    _allPlugins.Remove(oldAdapter);
                    Log.Information("[PluginAggregator] 插件已移除（编译失败）: {File}", fileName);
                }

                // 刷新活跃列表
                _activePlugins = _allPlugins
                    .Where(p => _pluginState.IsEnabled(p.Name))
                    .ToList();
                _toolsCacheValid = false;
            }

            // 通知 ChatAgentService 重建 Session
            PluginsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PluginAggregator] 热重载处理异常: {File}", filePath);
        }
    }

    /// <summary>所有插件（含禁用的），供管理窗口枚举</summary>
    public IReadOnlyList<IToolPlugin> AllPlugins => _allPlugins.AsReadOnly();

    // IToolPluginHost

    /// <summary>遍历启用的插件收集 AIFunction（带缓存，插件列表变更后自动重建）</summary>
    public AIFunction[] GetAllTools()
    {
        lock (_pluginsLock)
        {
            if (_cachedTools is not null && _toolsCacheValid)
                return _cachedTools;

            _cachedTools = _activePlugins.SelectMany(p => p.GetTools()).ToArray();
            _toolsCacheValid = true;
            return _cachedTools;
        }
    }

    /// <summary>
    /// 遍历启用的插件调用 TryExecuteToolAsync，返回首个非 null 结果。
    /// 自动录制系统工具到 WorkflowRecorder。
    /// 每个插件独立 try-catch，一个插件出错不影响其他插件。
    /// </summary>
    public async Task<string> ExecuteToolStepAsync(string toolName, string args)
    {
        // 透明录制：系统工具录制，管理工具和 local_llm 不录制
        if (!NonRecordableTools.Contains(toolName))
            _recorder.RecordStep(toolName, args);

        // 快照活跃插件列表，避免热重载时集合被替换导致 foreach 异常
        List<IToolPlugin> activeSnapshot;
        lock (_pluginsLock) { activeSnapshot = _activePlugins; }

        foreach (var plugin in activeSnapshot)
        {
            try
            {
                var result = await plugin.TryExecuteToolAsync(toolName, args);
                if (result is not null)
                {
                    // 记录工具调用到共享状态（供 UI 展示）
                    var displayResult = result.Length > 200 ? result[..200] + "..." : result;
                    lock (_sharedState.CurrentRoundToolCalls)
                        _sharedState.CurrentRoundToolCalls.Add((toolName, displayResult));
                    return result;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[PluginAggregator] 插件 {Plugin} 执行 {Tool} 异常",
                    plugin.Name, toolName);
            }
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

    /// <summary>
    /// 刷新活跃插件列表（根据 PluginStateService 当前状态过滤）。
    /// 供 PluginManagementWindow 在用户切换启用/禁用后调用，免重启生效。
    /// </summary>
    public void RefreshActivePlugins()
    {
        lock (_pluginsLock)
        {
            _activePlugins = _allPlugins
                .Where(p => _pluginState.IsEnabled(p.Name))
                .ToList();
            _toolsCacheValid = false;
        }
    }
}
