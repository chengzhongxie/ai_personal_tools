using System.ComponentModel;
using System.Windows;
using PersonalAssistant.Features.Mascot;
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

    /// <summary>DI 构造函数，注入 TrayService 和 MascotWindow</summary>
    public MainWindow(TrayService trayService, MascotWindow mascotWindow)
    {
        _trayService = trayService;
        _mascotWindow = mascotWindow;
        DataContext = this;
        InitializeComponent();
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
    /// 拦截窗口关闭，隐藏主窗口并显示卡通人偶
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        ShowInTaskbar = false;
        _mascotWindow.Show();
    }

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
