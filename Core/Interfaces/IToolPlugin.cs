using Microsoft.Extensions.AI;

namespace PersonalAssistant.Core.Interfaces;

/// <summary>
/// 插件契约：每个插件提供一组 AI 工具方法、提示词片段和工具执行能力。
/// 实现此接口的类会被 DI 自动扫描并注册为 Singleton。
/// </summary>
public interface IToolPlugin
{
    /// <summary>插件名称，用于日志和调试</summary>
    string Name { get; }

    /// <summary>返回此插件提供的所有 AIFunction</summary>
    AIFunction[] GetTools();

    /// <summary>
    /// 尝试执行指定工具。如果工具不属于此插件，返回 null。
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="args">工具参数（JSON 字符串）</param>
    /// <returns>执行结果，或 null 表示不处理此工具</returns>
    Task<string?> TryExecuteToolAsync(string toolName, string args);

    /// <summary>
    /// 返回此插件提供的系统提示词片段，用于聚合到完整提示词中。
    /// 返回 null 表示无提示词贡献。
    /// </summary>
    string? GetPromptFragment();
}
