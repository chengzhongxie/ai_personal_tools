using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PersonalAssistant.Core.Interfaces;

namespace PersonalAssistant.Features.Plugins.SystemInfoPlugin;

/// <summary>
/// 系统信息插件：提供 system_info、screenshot 两个 AI 工具。
/// 资源成本：1个单例，工具调用时方法调度 + GDI/OCR 按需消耗。
/// </summary>
public class SystemInfoPlugin : IToolPlugin
{
    public string Name => "SystemInfo";
    public string Description => "提供 2 个系统信息工具：系统状态查询（内存/磁盘/进程/电池）和 截图 + Windows 本地 OCR";

    public AIFunction[] GetTools()
    {
        return new[]
        {
            AIFunctionFactory.Create(new Func<string?, string>(SystemInfo), name: "system_info"),
            AIFunctionFactory.Create(new Func<Task<string>>(Screenshot), name: "screenshot"),
        };
    }

    public async Task<string?> TryExecuteToolAsync(string toolName, string args)
    {
        return toolName switch
        {
            "system_info" => SystemInfoMethods.SystemInfo(string.IsNullOrEmpty(args) ? null : args),
            "screenshot" => await SystemInfoMethods.Screenshot(),
            _ => null
        };
    }

    public string? GetPromptFragment() => null;

    [Description(
        "Get system status information: memory usage, disk space, top processes, battery, uptime.\n" +
        "Call with no args or \"all\" for full summary.\n" +
        "  \"memory\" — RAM usage: total, available, used, percentage\n" +
        "  \"disk\"   — all fixed drives: total, free, used space\n" +
        "  \"processes\" — top 10 processes by memory usage\n" +
        "  \"battery\" — battery percentage, charging status, remaining time\n" +
        "  \"all\"    — everything above in one report")]
    private static string SystemInfo(
        [Description("Category: memory, disk, processes, battery, or empty/all for full summary")] string? category = null) =>
        SystemInfoMethods.SystemInfo(category);

    [Description(
        "Capture a screenshot of the current screen, save as PNG, and run Windows built-in OCR.\n" +
        "Returns the file path, image dimensions, and any text found on screen.\n" +
        "100% local — no cloud AI needed. Use to read error dialogs, browser text, or UI content.")]
    private static Task<string> Screenshot() => SystemInfoMethods.Screenshot();
}
