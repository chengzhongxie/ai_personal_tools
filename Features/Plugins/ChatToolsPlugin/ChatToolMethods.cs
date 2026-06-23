using System.ComponentModel;
using PersonalAssistant.Infrastructure.Common.Services;

namespace PersonalAssistant.Features.Plugins.ChatToolsPlugin;

/// <summary>
/// 聊天工具方法静态实现：clear_chat、notify
/// </summary>
internal static class ChatToolMethods
{
    [Description("Clear the current conversation history and reset the pattern detector.")]
    public static string ClearChat(Action? onClearChat)
    {
        onClearChat?.Invoke();
        return "对话历史已清空，模式检测器已重置。";
    }

    [Description(
        "Show a Windows toast balloon notification from the system tray.\n" +
        "Use this to notify the user when a long-running task completes, or to deliver a reminder.\n" +
        "Max message length: 256 characters (auto-truncated).\n" +
        "Example: notify(\"编译完成\", \"项目已成功构建，0 个错误\")")]
    public static string Notify(
        [Description("Notification title")] string title,
        [Description("Notification body text")] string message,
        TrayService trayService)
    {
        trayService.ShowNotification(title, message);
        return $"已发送通知: {title}";
    }
}
