namespace PersonalAssistant.Core.Plugins;

/// <summary>
/// 工具参数定义 — 纯 DTO，零外部依赖。
/// </summary>
public sealed class PluginParameter
{
    /// <summary>参数名，如 "name", "url"</summary>
    public string Name { get; init; } = "";

    /// <summary>参数说明</summary>
    public string Description { get; init; } = "";

    /// <summary>参数类型: "string"|"number"|"boolean"，默认 "string"</summary>
    public string Type { get; init; } = "string";

    /// <summary>是否必填，默认 true</summary>
    public bool Required { get; init; } = true;
}

/// <summary>
/// 工具定义 — 纯 DTO，零外部依赖。
/// </summary>
public sealed class PluginToolDefinition
{
    /// <summary>工具名，如 "translate_text"，AI 模型通过此名称调用工具</summary>
    public string Name { get; init; } = "";

    /// <summary>AI 模型看到的工具描述，越详细越准确</summary>
    public string Description { get; init; } = "";

    /// <summary>参数列表，null 表示无参数工具</summary>
    public IReadOnlyList<PluginParameter>? Parameters { get; init; }
}

/// <summary>
/// 外部插件基类 — 插件作者唯一需要继承的类。
/// 只需重写 4 个成员：Name, Description, GetToolDefinitions(), ExecuteToolAsync()。
/// 零外部依赖 — 插件作者只用 System / System.Threading.Tasks / System.Text.Json。
/// </summary>
public abstract class PluginBase
{
    /// <summary>插件名称，用于日志和调试</summary>
    public abstract string Name { get; }

    /// <summary>插件描述，用于启动日志</summary>
    public abstract string Description { get; }

    /// <summary>返回此插件提供的所有工具定义（纯元数据声明）</summary>
    public abstract PluginToolDefinition[] GetToolDefinitions();

    /// <summary>
    /// 执行指定工具。
    /// </summary>
    /// <param name="toolName">工具名称（来自 GetToolDefinitions 中定义的 Name）</param>
    /// <param name="args">工具参数，JSON 字符串格式。参数结构应符合 GetToolDefinitions 中的 Parameters 定义。</param>
    /// <returns>执行结果文本，或 null 表示工具名不匹配</returns>
    public abstract Task<string?> ExecuteToolAsync(string toolName, string args);

    /// <summary>
    /// 返回此插件提供的系统提示词片段，用于聚合到 AI 系统提示词中。
    /// 返回 null 表示无提示词贡献。
    /// </summary>
    public virtual string? GetPromptFragment() => null;

    /// <summary>
    /// 外部插件的源文件路径。由 PluginLoader 在加载时设置。
    /// 内置插件的此属性为 null。
    /// </summary>
    public string? SourceFilePath { get; set; }
}
