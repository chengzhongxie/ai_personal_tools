using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Serilog;

namespace PersonalAssistant.Features.Chat.Services;

/// <summary>
/// 本地命令拦截器：将确定性系统指令（打开任务管理器、锁屏等）在发送到 AI 之前本地拦截执行。
/// 零 token 消耗。资源成本：仅拦截匹配时消耗 CPU（~1ms 正则），空闲时零开销。
/// </summary>
public sealed class LocalCommandInterceptor
{
    // 命令 → 本地执行结果文本
    private static readonly Dictionary<string, Func<string>> CommandMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // ──── 系统工具 ────
        ["打开任务管理器"] = Run("taskmgr.exe", "正在打开任务管理器"),
        ["任务管理器"] = Run("taskmgr.exe", "正在打开任务管理器"),
        ["打开计算器"] = Run("calc.exe", "正在打开计算器"),
        ["计算器"] = Run("calc.exe", "正在打开计算器"),
        ["打开记事本"] = Run("notepad.exe", "正在打开记事本"),
        ["记事本"] = Run("notepad.exe", "正在打开记事本"),
        ["打开画图"] = Run("mspaint.exe", "正在打开画图"),
        ["画图"] = Run("mspaint.exe", "正在打开画图"),
        ["打开设备管理器"] = Run("devmgmt.msc", "正在打开设备管理器"),
        ["设备管理器"] = Run("devmgmt.msc", "正在打开设备管理器"),
        ["打开注册表编辑器"] = Run("regedit.exe", "正在打开注册表编辑器"),
        ["打开磁盘管理"] = Run("diskmgmt.msc", "正在打开磁盘管理"),
        ["打开服务"] = Run("services.msc", "正在打开服务"),

        // ──── 系统操作 ────
        ["锁屏"] = () => { LockWorkStation(); return "屏幕已锁定"; },
        ["锁定屏幕"] = () => { LockWorkStation(); return "屏幕已锁定"; },
        ["锁定计算机"] = () => { LockWorkStation(); return "计算机已锁定"; },
        ["关机"] = RunShutdown("/s /t 30", "正在准备关机（30 秒后），可运行 shutdown /a 取消"),
        ["重启"] = RunShutdown("/r /t 30", "正在准备重启（30 秒后），可运行 shutdown /a 取消"),
        ["注销"] = RunShutdown("/l", "正在注销"),
        ["休眠"] = RunShutdown("/h", "正在休眠"),
        ["清空回收站"] = () =>
        {
            try
            {
                var psi = new ProcessStartInfo("powershell.exe", "-Command Clear-RecycleBin -Force")
                { CreateNoWindow = true, UseShellExecute = false };
                using var p = Process.Start(psi)!;
                p.WaitForExit(5000);
                return "回收站已清空";
            }
            catch (Exception ex) { return $"清空回收站失败: {ex.Message}"; }
        },

        // ──── 文件夹快捷方式 ────
        ["打开下载文件夹"] = Run("explorer.exe", "shell:Downloads", "正在打开下载文件夹"),
        ["打开下载"] = Run("explorer.exe", "shell:Downloads", "正在打开下载文件夹"),
        ["打开文档"] = Run("explorer.exe", "shell:Personal", "正在打开文档"),
        ["打开桌面"] = Run("explorer.exe", "shell:Desktop", "正在打开桌面"),
        ["打开图片"] = Run("explorer.exe", @"shell:My Pictures", "正在打开图片文件夹"),
        ["打开音乐"] = Run("explorer.exe", "shell:My Music", "正在打开音乐文件夹"),
        ["打开视频"] = Run("explorer.exe", "shell:My Video", "正在打开视频文件夹"),
        ["打开用户目录"] = Run("explorer.exe",
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "正在打开用户目录"),

        // ──── 命令行 ────
        ["打开cmd"] = Run("cmd.exe", "正在打开命令提示符"),
        ["打开命令行"] = Run("cmd.exe", "正在打开命令提示符"),
        ["打开命令提示符"] = Run("cmd.exe", "正在打开命令提示符"),
        ["打开powershell"] = Run("powershell.exe", "正在打开 PowerShell"),
        ["打开终端"] = Run("wt.exe", "正在打开 Windows 终端"),

        // ──── 系统设置 ────
        ["打开设置"] = Run("ms-settings:", "正在打开系统设置"),
        ["打开系统设置"] = Run("ms-settings:", "正在打开系统设置"),
        ["打开控制面板"] = Run("control", "正在打开控制面板"),
        ["控制面板"] = Run("control", "正在打开控制面板"),
        ["打开网络设置"] = Run("ms-settings:network", "正在打开网络设置"),
        ["打开蓝牙设置"] = Run("ms-settings:bluetooth", "正在打开蓝牙设置"),
        ["打开显示器设置"] = Run("ms-settings:display", "正在打开显示器设置"),
        ["打开声音设置"] = Run("ms-settings:sound", "正在打开声音设置"),

        // ──── 常用应用 ────
        ["打开资源管理器"] = Run("explorer.exe", "正在打开资源管理器"),
        ["文件资源管理器"] = Run("explorer.exe", "正在打开资源管理器"),
        ["打开浏览器"] = RunBrowser("正在打开浏览器"),

        // ──── 系统信息 ────
        ["系统信息"] = SystemInfo,
        ["系统状态"] = SystemInfo,
        ["cpu使用率"] = SystemInfo,
        ["内存使用"] = SystemInfo,
    };

    [DllImport("user32.dll")]
    private static extern bool LockWorkStation();

    /// <summary>
    /// 尝试拦截命令。返回 null 表示不是已知命令，需继续走 AI 流程。
    /// 返回非 null 字符串表示已本地处理，直接展示给用户。
    /// </summary>
    public string? TryIntercept(string userInput)
    {
        // 标准化输入：去除尾部标点，匹配命令表
        var normalized = userInput.Trim().TrimEnd('。', '！', '!', '，', ',', '~', '～', '.');

        if (CommandMap.TryGetValue(normalized, out var action))
        {
            Log.Information("[LocalCmd] 拦截命令: {Cmd}", normalized);
            try
            {
                return action();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[LocalCmd] 命令执行失败: {Cmd}", normalized);
                return $"命令执行失败: {ex.Message}";
            }
        }

        return null;
    }

    // ──── Helper factories ────

    private static Func<string> Run(string exe, string message)
        => () => { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true }); return message; };

    private static Func<string> Run(string exe, string args, string message)
        => () =>
        {
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
            return message;
        };

    private static Func<string> RunShutdown(string args, string message)
        => () =>
        {
            Process.Start(new ProcessStartInfo("shutdown.exe", args) { UseShellExecute = true });
            return message;
        };

    private static Func<string> RunBrowser(string message)
        => () =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://www.bing.com") { UseShellExecute = true });
            }
            catch
            {
                // 如果默认浏览器打开失败，尝试 edge
                try { Process.Start(new ProcessStartInfo("msedge.exe") { UseShellExecute = true }); }
                catch { return "无法打开浏览器，请检查默认浏览器设置"; }
            }
            return message;
        };

    private static string SystemInfo()
    {
        try
        {
            using var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            cpuCounter.NextValue();
            Thread.Sleep(100);
            var cpu = cpuCounter.NextValue();
            var mem = GC.GetGCMemoryInfo();
            var totalMemMB = Environment.WorkingSet / 1024 / 1024;
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => $"{d.Name} 可用 {d.AvailableFreeSpace / 1024 / 1024 / 1024}GB / 共 {d.TotalSize / 1024 / 1024 / 1024}GB");
            return $"CPU 使用率: {cpu:F0}%\n内存使用: {totalMemMB}MB\n磁盘:\n{string.Join("\n", drives)}";
        }
        catch (Exception ex) { return $"获取系统信息失败: {ex.Message}"; }
    }
}
