using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PersonalAssistant.Core.Interfaces;
using PersonalAssistant.Core.Services;
using PersonalAssistant.Infrastructure.Common.Services;

namespace PersonalAssistant.Features.Plugins.ChatToolsPlugin;

/// <summary>
/// 聊天工具插件：提供 clear_chat、notify 两个 AI 工具。
/// 资源成本：1个单例，notify 触发托盘气泡（瞬时操作），clear_chat 重置会话。
/// </summary>
public class ChatToolsPlugin : IToolPlugin
{
    private readonly TrayService _trayService;
    private readonly PluginSharedState _sharedState;

    public string Name => "ChatTools";
    public string Description => "提供 2 个聊天工具：清空对话历史 和 系统托盘气泡通知";

    public ChatToolsPlugin(TrayService trayService, PluginSharedState sharedState)
    {
        _trayService = trayService;
        _sharedState = sharedState;
    }

    public AIFunction[] GetTools()
    {
        return new[]
        {
            AIFunctionFactory.Create(new Func<string>(ClearChat), name: "clear_chat"),
            AIFunctionFactory.Create(new Func<string, string, string>(NotifyWrapper), name: "notify"),
        };
    }

    public Task<string?> TryExecuteToolAsync(string toolName, string args)
    {
        string? result = toolName switch
        {
            "clear_chat" => ChatToolMethods.ClearChat(() => _sharedState.RaiseClearChat()),
            "notify" => ChatToolMethods.Notify(args, "", _trayService),
            _ => null
        };

        return Task.FromResult<string?>(result);
    }

    public string? GetPromptFragment() => null;

    [Description("Clear the current conversation history and reset the pattern detector.")]
    private string ClearChat() => ChatToolMethods.ClearChat(() => _sharedState.RaiseClearChat());

    [Description(
        "Show a Windows toast balloon notification from the system tray.\n" +
        "Use this to notify the user when a long-running task completes, or to deliver a reminder.\n" +
        "Max message length: 256 characters (auto-truncated).\n" +
        "Example: notify(\"编译完成\", \"项目已成功构建，0 个错误\")")]
    private string NotifyWrapper(
        [Description("Notification title")] string title,
        [Description("Notification body text")] string message) =>
        ChatToolMethods.Notify(title, message, _trayService);
}
