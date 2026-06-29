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
    public event Action? OnClearChat;

    /// <summary>触发清空对话事件</summary>
    public void RaiseClearChat() => OnClearChat?.Invoke();

    /// <summary>网络是否不可达（ChatAgentService 探测结果，供插件判断是否跳过 HTTP 调用）</summary>
    public bool IsOffline { get; set; }

    /// <summary>本轮对话中调用的工具记录（PluginAggregator 写入，ChatViewModel 读取并展示）</summary>
    public List<(string ToolName, string Result)> CurrentRoundToolCalls { get; } = new();
}
