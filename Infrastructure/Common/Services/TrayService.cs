using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PersonalAssistant.Features.Chat.Models;
using PersonalAssistant.Features.Chat.Services;
using PersonalAssistant.Infrastructure.Common.Helpers;
using WinForms = System.Windows.Forms;

namespace PersonalAssistant.Infrastructure.Common.Services;

/// <summary>
/// 系统托盘服务：托盘图标、右键菜单（含导出对话、通知历史）
/// </summary>
public class TrayService : IDisposable
{
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>通知历史记录（最多 50 条）</summary>
    public ObservableCollection<NotificationRecord> NotificationRecords { get; } = new();

    private const int MaxNotificationRecords = 50;

    public TrayService(IServiceProvider serviceProvider)
    {
        Serilog.Log.Information("[TrayService] 构造开始");
        _serviceProvider = serviceProvider;

        var contextMenu = new WinForms.ContextMenuStrip();
        contextMenu.Items.Add("显示主窗口", null, OnShowWindow);
        contextMenu.Items.Add(new WinForms.ToolStripSeparator());
        contextMenu.Items.Add("导出对话", null, OnExportChat);
        contextMenu.Items.Add("通知历史", null, OnShowNotificationHistory);
        contextMenu.Items.Add(new WinForms.ToolStripSeparator());
        contextMenu.Items.Add("设置", null, OnOpenSettings);
        contextMenu.Items.Add("插件管理", null, OnOpenPluginManagement);
        contextMenu.Items.Add(new WinForms.ToolStripSeparator());
        contextMenu.Items.Add("切换主题 (深色/浅色)", null, OnToggleTheme);
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
        Serilog.Log.Information("[TrayService] 构造完成");
    }

    /// <summary>
    /// 在系统托盘弹出气泡通知（供 AI notify 工具调用），并记录到通知历史。
    /// </summary>
    /// <param name="title">通知标题</param>
    /// <param name="message">通知内容（最多 256 字符，超出自动截断）</param>
    public void ShowNotification(string title, string message)
    {
        if (message.Length > 256)
            message = message[..253] + "...";
        _notifyIcon.ShowBalloonTip(5000, title, message, WinForms.ToolTipIcon.Info);

        // 记录到通知历史
        Application.Current.Dispatcher.Invoke(() =>
        {
            NotificationRecords.Insert(0, new NotificationRecord
            {
                Title = title,
                Message = message,
                Source = "应用",
                Timestamp = DateTime.Now
            });

            while (NotificationRecords.Count > MaxNotificationRecords)
                NotificationRecords.RemoveAt(NotificationRecords.Count - 1);
        });
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

    private void OnToggleTheme(object? sender, EventArgs e)
    {
        var themeService = _serviceProvider.GetRequiredService<ThemeService>();
        themeService.IsDarkTheme = !themeService.IsDarkTheme;
        var themeName = themeService.IsDarkTheme ? "深色" : "浅色";
        ShowNotification("主题切换", $"已切换为{themeName}主题");
    }

    private void OnExportChat(object? sender, EventArgs e)
    {
        var exportService = _serviceProvider.GetRequiredService<ChatExportService>();
        exportService.ExportToMarkdown();
    }

    private void OnShowNotificationHistory(object? sender, EventArgs e)
    {
        var window = new Features.Notifications.NotificationHistoryWindow(NotificationRecords);
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
