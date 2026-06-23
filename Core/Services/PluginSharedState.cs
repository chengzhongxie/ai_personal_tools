using PersonalAssistant.Features.Workflow.Models;

namespace PersonalAssistant.Core.Services;

/// <summary>
/// 插件间共享状态容器。
/// 用于 ChatAgentService、ChatToolsPlugin、WorkflowPlugin 之间的状态传递。
/// </summary>
public class PluginSharedState
{
    /// <summary>待确认的模式建议（ChatAgentService 设置，WorkflowPlugin.SaveWorkflow 读取）</summary>
    public PatternMatch? PendingSuggestion { get; set; }

    /// <summary>清空对话回调（ChatAgentService 设置，ChatToolsPlugin.ClearChat 调用）</summary>
    public Action? OnClearChat { get; set; }
}
