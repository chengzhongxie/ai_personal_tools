using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using PersonalAssistant.Features.Clipboard.Services;
using PersonalAssistant.Features.Mascot;
using PersonalAssistant.Infrastructure.Common.Helpers;
using PersonalAssistant.Infrastructure.Common.Services;
using Wpf.Ui.Controls;

namespace PersonalAssistant;

/// <summary>
/// 应用程序主窗口，基于 WPF-UI FluentWindow，内嵌 ChatView。
/// 热键配置从 UserSettingsService 读取，支持用户自定义。
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly TrayService _trayService;
    private readonly MascotWindow _mascotWindow;
    private readonly UserSettingsService _settings;
    private readonly ClipboardMonitor _clipboardMonitor;
    private bool _isShuttingDown;

    // Win32 global hotkey
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 9001;
    private const int HOTKEY_SELTEXT = 9002;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const byte VK_CONTROL = 0x11;
    private const byte VK_C = 0x43;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    /// <summary>DI 构造函数，注入 TrayService、MascotWindow、ClipboardMonitor 和 UserSettingsService</summary>
    public MainWindow(TrayService trayService, MascotWindow mascotWindow,
        UserSettingsService settings, ClipboardMonitor clipboardMonitor)
    {
        Serilog.Log.Information("[MainWindow] 构造开始");
        _trayService = trayService;
        _mascotWindow = mascotWindow;
        _settings = settings;
        _clipboardMonitor = clipboardMonitor;
        DataContext = this;
        InitializeComponent();
        Serilog.Log.Information("[MainWindow] InitializeComponent完成");

        // 设置窗口图标（与托盘图标一致：蓝紫渐变 AI）
        Icon = AppIconGenerator.WpfIcon;
        Serilog.Log.Information("[MainWindow] 构造完成");
    }

    /// <summary>注册全局热键（从用户设置读取配置）</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;

        // 注册主窗口热键（默认 Alt+Space）
        if (!RegisterHotKey(hwnd, HOTKEY_ID, _settings.HotkeyModifiers, _settings.HotkeyKey))
        {
            _trayService.ShowNotification("热键注册失败",
                "全局热键被其他程序占用（如 PowerToys），请在设置中修改快捷键");
        }

        // 注册选中文本热键（默认 Ctrl+Alt+Space）
        if (!RegisterHotKey(hwnd, HOTKEY_SELTEXT, _settings.SelectTextModifiers, _settings.SelectTextKey))
        {
            _trayService.ShowNotification("热键注册失败",
                "选中文本快捷键被占用，请在设置中修改快捷键");
        }

        HwndSource.FromHwnd(hwnd)!.AddHook(WndProc);

        // 初始化剪贴板监听（OS 消息驱动，零轮询）
        _clipboardMonitor.Initialize(hwnd);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var hotkeyId = wParam.ToInt32();
            if (hotkeyId == HOTKEY_ID)
            {
                ToggleVisibility();
                handled = true;
            }
            else if (hotkeyId == HOTKEY_SELTEXT)
            {
                HandleSelectedText();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// 处理选中文本快捷操作（Ctrl+Alt+Space）：
    /// 模拟 Ctrl+C 复制选中文本 → 读取剪贴板 → 显示主窗口并填入输入框
    /// </summary>
    private void HandleSelectedText()
    {
        _ = HandleSelectedTextAsync();
    }

    private async Task HandleSelectedTextAsync()
    {
        try
        {
            // 1. 保存原始剪贴板内容，发送 Ctrl+C 获取选中文本
            string? originalClipboard = null;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (System.Windows.Clipboard.ContainsText())
                        originalClipboard = System.Windows.Clipboard.GetText();
                }
                catch (System.Runtime.InteropServices.COMException) { /* 剪贴板被其他进程占用 */ }
            });

            // 2. 模拟 Ctrl+C
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_C, 0, 0, UIntPtr.Zero);
            keybd_event(VK_C, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

            // 3. 等待剪贴板更新
            await Task.Delay(80);

            // 4. 读取剪贴板
            string? selectedText = null;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (System.Windows.Clipboard.ContainsText())
                        selectedText = System.Windows.Clipboard.GetText();
                }
                catch (System.Runtime.InteropServices.COMException) { /* 剪贴板被其他进程占用 */ }
            });

            // 5. 恢复原始剪贴板
            if (originalClipboard is not null)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try { System.Windows.Clipboard.SetText(originalClipboard); }
                    catch (System.Runtime.InteropServices.COMException) { }
                });
            }

            // 6. 如果没有选中文本，静默返回
            if (string.IsNullOrWhiteSpace(selectedText))
                return;

            // 7. 显示主窗口并填入文本
            Dispatcher.Invoke(() =>
            {
                ShowWindow();
                ChatViewControl.ViewModel.InputText = selectedText;
                ChatViewControl.FocusInput();
            });
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "[MainWindow] HandleSelectedText 异常");
        }
    }

    /// <summary>切换窗口显示/隐藏（全局热键回调）</summary>
    private void ToggleVisibility()
    {
        if (Visibility == Visibility.Visible)
        {
            Hide();
            ShowInTaskbar = false;
            _mascotWindow.Show();
        }
        else
        {
            ShowWindow();
        }
    }
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
