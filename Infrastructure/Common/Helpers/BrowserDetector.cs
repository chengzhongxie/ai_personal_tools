using System.IO;
using Microsoft.Win32;

namespace PersonalAssistant.Infrastructure.Common.Helpers;

/// <summary>
/// 检测本机已安装的浏览器，返回 AI 可用的打开方式建议。
/// 仅在应用启动时调用一次，无后台消耗。
/// </summary>
public static class BrowserDetector
{
    /// <summary>
    /// 已知浏览器的检测规则：注册表 App Paths 键名、已知安装路径、start 命令名。
    /// </summary>
    private sealed record BrowserInfo(string Name, string RegKey, string[] KnownPaths, string StartCommand);

    private static readonly BrowserInfo[] KnownBrowsers =
    [
        new("Google Chrome",   "chrome.exe",  [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Google", "Chrome", "Bin", "chrome.exe"),
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
        ], "start chrome"),
        new("Mozilla Firefox", "firefox.exe", [
            @"C:\Program Files\Mozilla Firefox\firefox.exe",
            @"C:\Program Files (x86)\Mozilla Firefox\firefox.exe",
        ], "start firefox"),
        new("Microsoft Edge",  "msedge.exe",  [
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        ], "start msedge"),
    ];

    /// <summary>
    /// 扫描系统，返回已安装浏览器的列表描述。
    /// 检测顺序：注册表 HKCU App Paths → HKLM App Paths → 已知文件路径。
    /// </summary>
    public static IReadOnlyList<(string Name, string StartCommand)> Detect()
    {
        var found = new List<(string Name, string StartCommand)>();

        foreach (var browser in KnownBrowsers)
        {
            if (IsInstalled(browser))
                found.Add((browser.Name, browser.StartCommand));
        }

        return found;
    }

    private static bool IsInstalled(BrowserInfo browser)
    {
        // 1. 注册表 HKCU App Paths（每用户安装）
        using var hkcuKey = Registry.CurrentUser.OpenSubKey(
            $@"Software\Microsoft\Windows\CurrentVersion\App Paths\{browser.RegKey}");
        if (hkcuKey?.GetValue(null) is string hkcuPath && File.Exists(hkcuPath))
            return true;

        // 2. 注册表 HKLM App Paths（系统级安装）
        using var hklmKey = Registry.LocalMachine.OpenSubKey(
            $@"Software\Microsoft\Windows\CurrentVersion\App Paths\{browser.RegKey}");
        if (hklmKey?.GetValue(null) is string hklmPath && File.Exists(hklmPath))
            return true;

        // 3. 已知安装路径
        foreach (var path in browser.KnownPaths)
        {
            if (File.Exists(path))
                return true;
        }

        return false;
    }
}
