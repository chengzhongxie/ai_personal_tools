using System.ComponentModel;
using System.Windows;
using PersonalAssistant.Features.Mascot;
using PersonalAssistant.Infrastructure.Common.Helpers;
using PersonalAssistant.Infrastructure.Common.Services;
using Wpf.Ui.Controls;

namespace PersonalAssistant;

/// <summary>
/// 应用程序主窗口，基于 WPF-UI FluentWindow，内嵌 ChatView
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly TrayService _trayService;
    private readonly MascotWindow _mascotWindow;
    private bool _isShuttingDown;

    /// <summary>DI 构造函数，注入 TrayService 和 MascotWindow</summary>
    public MainWindow(TrayService trayService, MascotWindow mascotWindow)
    {
        _trayService = trayService;
        _mascotWindow = mascotWindow;
        DataContext = this;
        InitializeComponent();

        // 设置窗口图标（与托盘图标一致：蓝紫渐变 AI）
        Icon = AppIconGenerator.WpfIcon;
    }

    /// <summary>
    /// 最小化时隐藏到托盘，不在任务栏显示
    /// </summary>
    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            ShowInTaskbar = false;
            _mascotWindow.Show();
        }
    }

    /// <summary>
    /// 拦截窗口关闭，隐藏主窗口并显示卡通人偶。
    /// 程序退出时不拦截，直接关闭。
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isShuttingDown) return;

        e.Cancel = true;
        Hide();
        ShowInTaskbar = false;
        _mascotWindow.Show();
    }

    /// <summary>标记程序正在退出，下次 OnClosing 不再拦截</summary>
    public void FlagShutdown() => _isShuttingDown = true;

    /// <summary>
    /// 恢复窗口显示（从托盘右键"显示主窗口"或人偶点击调用）
    /// </summary>
    public void ShowWindow()
    {
        _mascotWindow.Hide();
        Show();
        ShowInTaskbar = true;
        Activate();
        WindowState = WindowState.Normal;
        Dispatcher.BeginInvoke(() => ChatViewControl.FocusInput());
    }
}
