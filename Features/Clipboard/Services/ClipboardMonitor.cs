using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using PersonalAssistant.Features.Clipboard.Models;

namespace PersonalAssistant.Features.Clipboard.Services;

/// <summary>
/// 剪贴板变化监听器：Win32 AddClipboardFormatListener（OS 消息驱动，零轮询）+ 本地启发式分类。
/// 资源成本：OS 消息驱动，空闲时零 CPU，~200 bytes 常驻内存。
/// </summary>
public sealed class ClipboardMonitor : IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private const int MaxClipboardTextLength = 5120; // 5K 截断

    private IntPtr _hwnd;
    private HwndSource? _hwndSource;
    private long _lastChangeTick;
    private string? _latestText;
    private ClipboardContentType _latestType;
    private bool _suppressNext;
    private readonly object _lock = new();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    /// <summary>最近一次剪贴板文本内容（截断 5K），可能为 null</summary>
    public string? LatestClipboardText
    {
        get { lock (_lock) return _latestText; }
    }

    /// <summary>最近一次剪贴板内容类型</summary>
    public ClipboardContentType LatestClipboardType
    {
        get { lock (_lock) return _latestType; }
    }

    /// <summary>剪贴板内容变化时触发（后台线程，订阅者需自行封送 UI 线程）</summary>
    public event EventHandler? ClipboardChanged;

    /// <summary>
    /// 两阶段初始化第二步：挂载 HWND 并注册剪贴板监听。
    /// 必须在窗口 OnSourceInitialized 之后调用。
    /// </summary>
    public void Initialize(IntPtr hwnd)
    {
        _hwnd = hwnd;
        if (!AddClipboardFormatListener(hwnd))
        {
            Serilog.Log.Warning("[ClipboardMonitor] AddClipboardFormatListener 失败");
        }

        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);

        Serilog.Log.Information("[ClipboardMonitor] 初始化完成");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            OnClipboardUpdate();
        }
        return IntPtr.Zero;
    }

    private async void OnClipboardUpdate()
    {
        // 200ms 防抖
        var tick = Stopwatch.GetTimestamp();
        var last = Interlocked.Read(ref _lastChangeTick);
        if (tick - last < Stopwatch.Frequency / 5)
            return;
        Interlocked.Exchange(ref _lastChangeTick, tick);

        // 检查是否抑制本次更新（避免自己写剪贴板触发反馈循环）
        if (_suppressNext)
        {
            _suppressNext = false;
            return;
        }

        string? text = null;
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (System.Windows.Clipboard.ContainsText())
                    text = System.Windows.Clipboard.GetText();
            }
            catch (COMException)
            {
                // 剪贴板被其他进程锁定，静默跳过
            }
        });

        if (string.IsNullOrEmpty(text))
            return;

        // 截断
        if (text.Length > MaxClipboardTextLength)
            text = text[..MaxClipboardTextLength];

        var type = Classify(text);

        lock (_lock)
        {
            _latestText = text;
            _latestType = type;
        }

        ClipboardChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 纯本地启发式分类（正则/字符串匹配），零 token 消耗。
    /// 顺序敏感：先匹配高优先级类型。
    /// </summary>
    public static ClipboardContentType Classify(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return ClipboardContentType.Unknown;

        var trimmed = text.Trim();

        // 1. URL: http://, https://, ftp://
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
        {
            return ClipboardContentType.Url;
        }

        // 2. Path: [A-Za-z]:\ 且 Path.IsPathFullyQualified
        if (trimmed.Length >= 3 && char.IsLetter(trimmed[0]) && trimmed[1] == ':' && trimmed[2] == '\\')
        {
            try
            {
                if (Path.IsPathFullyQualified(trimmed)
                    && trimmed.IndexOfAny(Path.GetInvalidPathChars()) < 0)
                {
                    return ClipboardContentType.Path;
                }
            }
            catch
            {
                // Path 验证失败，继续尝试其他类型
            }
        }

        // 3. Number: 仅包含数字、逗号、点号、空格（允许千分位和小数）
        if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[\d,.\s]+$"))
        {
            // 必须至少包含一个数字
            if (trimmed.Any(char.IsDigit))
                return ClipboardContentType.Number;
        }

        // 4. Code: 符号密度 >12% 且 ≥3 种符号族
        if (trimmed.Length >= 10)
        {
            var symbolFamilies = new HashSet<char>();
            var symbolCount = 0;
            foreach (var c in trimmed)
            {
                if (IsCodeSymbol(c, symbolFamilies))
                    symbolCount++;
            }
            var density = (double)symbolCount / trimmed.Length;
            if (density > 0.12 && symbolFamilies.Count >= 3)
                return ClipboardContentType.Code;
        }

        // 5. Text: 包含字母的内容
        if (trimmed.Any(char.IsLetter))
            return ClipboardContentType.Text;

        return ClipboardContentType.Unknown;
    }

    private static bool IsCodeSymbol(char c, HashSet<char> families)
    {
        switch (c)
        {
            case '{': case '}': families.Add('{'); return true;
            case '(': case ')': families.Add('('); return true;
            case '[': case ']': families.Add('['); return true;
            case ';': families.Add(';'); return true;
            case '=': families.Add('='); return true;
            case '&': case '|': families.Add('&'); return true;
            case '!': families.Add('!'); return true;
            case '@': families.Add('@'); return true;
            case '#': families.Add('#'); return true;
            case '$': families.Add('$'); return true;
            case '%': families.Add('%'); return true;
            case '^': families.Add('^'); return true;
            case '*': families.Add('*'); return true;
            case '+': case '-': case '/': families.Add('+'); return true;
            case '<': case '>': families.Add('<'); return true;
            default: return false;
        }
    }

    /// <summary>
    /// 抑制下一次剪贴板更新事件，用于避免自身写入剪贴板时触发反馈循环
    /// </summary>
    public void SuppressNextUpdate()
    {
        _suppressNext = true;
    }

    public void Dispose()
    {
        if (_hwnd != IntPtr.Zero)
            RemoveClipboardFormatListener(_hwnd);
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource?.Dispose();
    }
}
