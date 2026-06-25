using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using PersonalAssistant.Core.Interfaces;

namespace PersonalAssistant.Features.Plugins.SystemTools;

/// <summary>
/// 系统工具插件：提供 13 个系统级 AI 工具。
/// 资源成本：1个单例，工具调用时方法调度，空闲零开销。
/// </summary>
public class SystemToolsPlugin : IToolPlugin
{
    private readonly IServiceProvider _services;
    private IDangerousToolPolicy? _policy;
    private IDangerousToolPolicy Policy => _policy ??= _services.GetRequiredService<IDangerousToolPolicy>();

    public string Name => "SystemTools";

    public SystemToolsPlugin(IServiceProvider services)
    {
        _services = services;
    }

    public AIFunction[] GetTools()
    {
        return new[]
        {
            AIFunctionFactory.Create(new Func<string, string>(ReadFile), name: "read_file"),
            AIFunctionFactory.Create(new Func<string, string, string>(WriteFileWrapper), name: "write_file"),
            AIFunctionFactory.Create(new Func<string?, string>(ListFiles), name: "list_files"),
            AIFunctionFactory.Create(new Func<string, Task<string>>(RunShellWrapper), name: "run_shell"),
            AIFunctionFactory.Create(new Func<string, string?, string>(RunCommand), name: "run_command"),
            AIFunctionFactory.Create(new Func<string, string>(FindApp), name: "find_app"),
            AIFunctionFactory.Create(new Func<string, string>(SendKeys), name: "send_keys"),
            AIFunctionFactory.Create(new Func<string, string>(TypeText), name: "type_text"),
            AIFunctionFactory.Create(new Func<string>(WindowInfo), name: "window_info"),
            AIFunctionFactory.Create(new Func<string, string>(FocusWindow), name: "focus_window"),
            AIFunctionFactory.Create(new Func<string>(ReadClipboard), name: "read_clipboard"),
            AIFunctionFactory.Create(new Func<string, string>(WriteClipboard), name: "write_clipboard"),
            AIFunctionFactory.Create(new Func<string, string?, string>(SearchFiles), name: "search_files"),
        };
    }

    public async Task<string?> TryExecuteToolAsync(string toolName, string args)
    {
        string? result = toolName switch
        {
            "read_file" => SystemToolMethods.ReadFile(args),
            "write_file" => SystemToolMethods.WriteFile(args, "", Policy),
            "list_files" => SystemToolMethods.ListFiles(string.IsNullOrEmpty(args) ? null : args),
            "run_shell" => await SystemToolMethods.RunShellAsync(args, Policy),
            "run_command" => SystemToolMethods.RunCommand(args, null),
            "find_app" => SystemToolMethods.FindApp(args),
            "send_keys" => SystemToolMethods.SendKeys(args),
            "type_text" => SystemToolMethods.TypeText(args),
            "window_info" => SystemToolMethods.WindowInfo(),
            "focus_window" => SystemToolMethods.FocusWindow(args),
            "read_clipboard" => SystemToolMethods.ReadClipboard(),
            "write_clipboard" => SystemToolMethods.WriteClipboard(args),
            "search_files" => SystemToolMethods.SearchFiles(args, null),
            _ => null
        };

        return result;
    }

    public string? GetPromptFragment() => null; // System prompt is built centrally in ChatSystemPrompt

    [Description("Write text content to a file. Creates parent directories if needed.")]
    private string WriteFileWrapper(
        [Description("Path where the file should be written")] string path,
        [Description("Text content to write to the file")] string content) =>
        SystemToolMethods.WriteFile(path, content, Policy);

    [Description(
        "Execute a PowerShell command and return its captured output.\n" +
        "Covers: file management, process/service control, registry, recycle bin, etc.\n" +
        "Common cmd aliases work: dir, type, del, copy, cd, echo, mkdir.\n" +
        "System operations: Clear-RecycleBin -Force, Get-Service, Stop-Process, etc.\n" +
        "Do NOT use for: launching GUI programs (use run_command instead).\n" +
        "Timeout: 15 seconds. Max output: ~100KB.")]
    private Task<string> RunShellWrapper(
        [Description("PowerShell command to execute")] string command) =>
        SystemToolMethods.RunShellAsync(command, Policy);

    // Direct delegate wrappers matching the [Description] attributes on SystemToolMethods
    [Description("Read the contents of a file at the given path")]
    private static string ReadFile(
        [Description("Absolute or relative path to the file")] string path) =>
        SystemToolMethods.ReadFile(path);


    [Description("List files and subdirectories in a directory. Defaults to current directory if omitted.")]
    private static string ListFiles(
        [Description("Directory path to list")] string? path = null) =>
        SystemToolMethods.ListFiles(path);

    [Description(
        "Launch a program or open a file/URL using Windows Shell (ShellExecute).\n" +
        "Shell handles: PATH lookup, UAC elevation, file associations.\n" +
        "Returns immediately after launching — does NOT wait for exit.\n" +
        "Use for: starting GUI apps, browsers, documents, URLs, installers.\n" +
        "For commands that need output/exit-code, use run_shell instead.\n" +
        "Examples: run_command(\"PixPin\"), run_command(\"notepad\", \"file.txt\"),\n" +
        "  run_command(\"https://google.com\"), run_command(\"explorer\", \".\")")]
    private static string RunCommand(
        [Description("Program name / full path / URL")] string exe,
        [Description("Optional command-line arguments")] string? args = null) =>
        SystemToolMethods.RunCommand(exe, args);

    [Description(
        "Search the Windows Start Menu for installed applications matching a keyword.\n" +
        "Returns app names and executable paths. Searches both per-user and all-users shortcuts.\n" +
        "Use this BEFORE launching any app the user asks for — prefer user-installed tools over system defaults.")]
    private static string FindApp(
        [Description("Keyword to search for (e.g., 'screenshot', 'editor', 'postman')")] string keyword) =>
        SystemToolMethods.FindApp(keyword);

    [Description(
        "Send keystrokes at the hardware input level via Win32 SendInput.\n" +
        "Two modes:\n" +
        "  Key combo (contains '+'): 'Ctrl+Alt+A', 'Win+Shift+S', 'Alt+F4'\n" +
        "  Typing text (no '+'): 'claude', 'npm install', 'hello world'\n" +
        "Special keys: Enter, Tab, F1-F12, Esc, Space, Home, End, PrtSc.")]
    private static string SendKeys(
        [Description("Key combo like 'Ctrl+Alt+A', or plain text to type")] string input) =>
        SystemToolMethods.SendKeys(input);

    [Description(
        "Type text directly into the foreground application at the cursor position.\n" +
        "Use this to output results back to the user's active app (editor, text box, terminal, etc.).\n" +
        "Works with any text including Chinese characters. Supports long text blocks.\n" +
        "Before typing, verify target window with window_info() first.")]
    private static string TypeText(
        [Description("The text to type into the foreground application")] string text) =>
        SystemToolMethods.TypeText(text);

    [Description(
        "Get info about the currently focused window and list all visible top-level windows.\n" +
        "Returns: focused window title/class/process, plus list of other windows.\n" +
        "Use BEFORE sending keystrokes to verify the correct window has focus.")]
    private static string WindowInfo() => SystemToolMethods.WindowInfo();

    [Description(
        "Find a visible window whose title contains the given text, and bring it to foreground.\n" +
        "Use when keystrokes are going to the wrong window.\n" +
        "Example: focus_window('IntelliJ IDEA'), focus_window('Visual Studio')")]
    private static string FocusWindow(
        [Description("Partial window title to search for")] string titlePart) =>
        SystemToolMethods.FocusWindow(titlePart);

    [Description("Read text content from the Windows clipboard")]
    private static string ReadClipboard() => SystemToolMethods.ReadClipboard();

    [Description("Write text to the Windows clipboard")]
    private static string WriteClipboard(
        [Description("Text to write to the clipboard")] string text) =>
        SystemToolMethods.WriteClipboard(text);

    [Description(
        "Recursively search for files matching a name pattern.\n" +
        "MUCH faster than list_files for targeted searches — uses lazy enumeration.\n" +
        "Pattern: '*.pdf', 'report*', '*2024*.xlsx', or 'photo' (auto appends *).\n" +
        "Directory: defaults to user profile. Cap: 100 results, 10s timeout.\n" +
        "Example: search_files(\"*.docx\", \"C:\\\\Users\"), search_files(\"Claude.md\")")]
    private static string SearchFiles(
        [Description("File name pattern (e.g. '*.pdf', 'budget*.xlsx')")] string pattern,
        [Description("Directory to start searching from, defaults to user profile")] string? directory = null) =>
        SystemToolMethods.SearchFiles(pattern, directory);
}
