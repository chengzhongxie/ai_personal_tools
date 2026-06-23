using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PersonalAssistant.Infrastructure.Common.Helpers;
using WinForms = System.Windows.Forms;

namespace PersonalAssistant.Infrastructure.Common.Services;

/// <summary>
/// 系统托盘服务：托盘图标、右键菜单（显示主窗口、设置、退出）
/// </summary>
public class TrayService : IDisposable
{
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly IServiceProvider _serviceProvider;

    public TrayService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;

        var contextMenu = new WinForms.ContextMenuStrip();
        contextMenu.Items.Add("显示主窗口", null, OnShowWindow);
        contextMenu.Items.Add("设置", null, OnOpenSettings);
        contextMenu.Items.Add("插件管理", null, OnOpenPluginManagement);
        contextMenu.Items.Add(new WinForms.ToolStripSeparator());
        contextMenu.Items.Add("退出", null, OnExit);

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = AppIconGenerator.CreateTrayIcon(),
            Text = "个人 AI 助手",
            ContextMenuStrip = contextMenu,
            Visible = true
        };
        _notifyIcon.DoubleClick += OnShowWindow;
    }

    /// <summary>
    /// 在系统托盘弹出气泡通知（供 AI notify 工具调用）。
    /// </summary>
    /// <param name="title">通知标题</param>
    /// <param name="message">通知内容（最多 256 字符，超出自动截断）</param>
    public void ShowNotification(string title, string message)
    {
        if (message.Length > 256)
            message = message[..253] + "...";
        _notifyIcon.ShowBalloonTip(5000, title, message, WinForms.ToolTipIcon.Info);
    }

    private void OnShowWindow(object? sender, EventArgs e)
    {
        _serviceProvider.GetRequiredService<MainWindow>().ShowWindow();
    }

    private void OnOpenSettings(object? sender, EventArgs e)
    {
        var settingsWindow = _serviceProvider.GetRequiredService<
            Features.Settings.SettingsWindow>();
        settingsWindow.ShowDialog();
    }

    private void OnOpenPluginManagement(object? sender, EventArgs e)
    {
        var window = _serviceProvider.GetRequiredService<
            Features.Plugins.PluginManagementWindow>();
        window.ShowDialog();
    }

    private void OnExit(object? sender, EventArgs e)
    {
        _notifyIcon.Visible = false;
        _serviceProvider.GetRequiredService<MainWindow>().FlagShutdown();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
