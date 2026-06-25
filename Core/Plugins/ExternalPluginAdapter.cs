using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PersonalAssistant.Core.Interfaces;
using Serilog;

namespace PersonalAssistant.Core.Plugins;

/// <summary>
/// 外部插件适配器：将 PluginBase 包装为 IToolPlugin。
/// 使用 AIFunctionFactory.Create 在运行时生成 AIFunction[]。
/// AIFunction[] 在首次 GetTools() 调用时构建并缓存。
/// 资源成本：1个单例 + 缓存的 AIFunction[] 数组。空闲时零开销。
/// </summary>
public class ExternalPluginAdapter : IToolPlugin
{
    private readonly PluginBase _plugin;
    private AIFunction[]? _cachedTools;
    private readonly object _lock = new();

    public string Name => _plugin.Name;
    public string Description => _plugin.Description;

    /// <summary>暴露内部 PluginBase 实例，供插件管理窗口等外部使用</summary>
    public PluginBase SourcePlugin => _plugin;

    public ExternalPluginAdapter(PluginBase plugin)
    {
        _plugin = plugin;
    }

    public AIFunction[] GetTools()
    {
        if (_cachedTools is not null)
            return _cachedTools;

        lock (_lock)
        {
            if (_cachedTools is not null)
                return _cachedTools;

            var toolDefs = _plugin.GetToolDefinitions();
            _cachedTools = toolDefs.Select(CreateAIFunction).ToArray();
            return _cachedTools;
        }
    }

    public async Task<string?> TryExecuteToolAsync(string toolName, string args)
    {
        try
        {
            var result = await _plugin.ExecuteToolAsync(toolName, args);
            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ExternalPluginAdapter] 插件 {Name} 工具 {Tool} 执行异常",
                _plugin.Name, toolName);
            return $"插件执行错误: {ex.Message}";
        }
    }

    public string? GetPromptFragment() => _plugin.GetPromptFragment();

    /// <summary>
    /// 根据 PluginToolDefinition 运行时生成 AIFunction。
    /// 无参数工具使用 Func&lt;Task&lt;string&gt;&gt; 委托；
    /// 有参数工具使用 Func&lt;string, Task&lt;string&gt;&gt; 委托（单一 JSON args 参数）。
    /// </summary>
    private AIFunction CreateAIFunction(PluginToolDefinition toolDef)
    {
        var description = BuildDescription(toolDef);

        if (toolDef.Parameters is null || toolDef.Parameters.Count == 0)
        {
            // 无参数工具
            var fn = new Func<Task<string>>(async () =>
                await _plugin.ExecuteToolAsync(toolDef.Name, "{}") ?? "OK");
            return AIFunctionFactory.Create(fn, name: toolDef.Name, description: description);
        }
        else
        {
            // 有参数工具：单一 string 参数接收 JSON args
            var fn = new Func<string, Task<string>>(async (argsJson) =>
                await _plugin.ExecuteToolAsync(toolDef.Name, argsJson) ?? "OK");
            return AIFunctionFactory.Create(fn, name: toolDef.Name, description: description);
        }
    }

    /// <summary>
    /// 构建工具描述文本，包含参数说明。
    /// </summary>
    private static string BuildDescription(PluginToolDefinition toolDef)
    {
        if (toolDef.Parameters is null || toolDef.Parameters.Count == 0)
            return toolDef.Description;

        var paramDescs = toolDef.Parameters.Select(p =>
        {
            var req = p.Required ? "(必填)" : "(可选)";
            return $"  - {p.Name} ({p.Type}) {req}: {p.Description}";
        });

        return toolDef.Description +
               "\n\n参数 (JSON 格式，作为 args_json 传入):\n" +
               string.Join("\n", paramDescs);
    }
}
