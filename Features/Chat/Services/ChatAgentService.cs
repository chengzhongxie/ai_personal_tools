using System.ClientModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using Serilog;
using PersonalAssistant.Features.Workflow.Services;
using PersonalAssistant.Infrastructure.Common.Helpers;
using PersonalAssistant.Infrastructure.Common.Services;

namespace PersonalAssistant.Features.Chat.Services;

/// <summary>
/// 基于 Microsoft Agent Framework 的 AI 聊天服务，
/// 封装 AIAgent、工具调用循环和流式输出。
/// 资源成本：仅消息发送时消耗 CPU/GPU，空闲时零开销（事件驱动，无后台轮询）。
/// </summary>
public class ChatAgentService
{
    private readonly UserSettingsService _settings;
    private readonly WorkflowRecorder _recorder;

    private ChatClientAgent? _agent;
    private AgentSession? _session;

    // Win32 SendInput — modern hardware-level input injection
    private const int INPUT_KEYBOARD = 1;
    private const int KEYEVENTF_KEYDOWN = 0x0000;
    private const int KEYEVENTF_KEYUP = 0x0002;
    private const int KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const int KEYEVENTF_UNICODE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public int dwFlags;
        public int time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT
    {
        [FieldOffset(0)] public int type;
        [FieldOffset(8)] public KEYBDINPUT ki;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern ushort MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern short VkKeyScan(char ch);

    // Window management
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int count);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const uint GW_OWNER = 4;

    private static readonly string SystemPrompt = BuildSystemPrompt();

    private static string BuildSystemPrompt()
    {
        var cwd = Environment.CurrentDirectory;
        var browsers = BrowserDetector.Detect();
        var installedApps = StartMenuScanner.Scan();

        var sb = new StringBuilder();
        sb.AppendLine("You are a personal AI assistant on the user's Windows machine. Be concise and practical.");
        sb.AppendLine($"Working directory: {cwd}");
        sb.AppendLine();

        if (browsers.Count > 0)
        {
            sb.AppendLine("Detected browsers:");
            foreach (var (name, _) in browsers)
                sb.AppendLine($"  - {name}");
        }

        if (installedApps.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("User-installed applications (prefer these over system defaults):");
            foreach (var app in installedApps)
                sb.AppendLine($"  - {app}");
        }
        else
        {
            sb.AppendLine("No common browsers detected in registry.");
        }

        sb.AppendLine();
        sb.AppendLine("Available tools:");
        sb.AppendLine("  read_file(path)      - read text file contents");
        sb.AppendLine("  write_file(path,content) - write text to a file");
        sb.AppendLine("  list_files(path?)    - list directory contents (defaults to cwd)");
        sb.AppendLine("  web_fetch(url)       - fetch text content from a URL");
        sb.AppendLine("  run_shell(command)   - execute PowerShell command, capture output");
        sb.AppendLine("                          covers: system ops, file management, service control,");
        sb.AppendLine("                          registry, process management, recycle bin, etc.");
        sb.AppendLine("                          common aliases work: dir, type, del, copy, cd");
        sb.AppendLine("  run_command(exe,args) - launch a program (returns instantly, does NOT wait for exit)");
        sb.AppendLine("                           use for GUI apps, URLs, documents — then follow up with send_keys");
        sb.AppendLine("                           for commands that need exit code (winget, uninstallers): use run_shell");
        sb.AppendLine("  find_app(keyword)    - search Start Menu for installed apps matching keyword");
        sb.AppendLine("  send_keys(input)     - send key combos or type text (SendInput, hardware level)");
        sb.AppendLine("                          combo: 'Ctrl+Alt+A', 'Ctrl+Shift+A', 'Alt+F4'");
        sb.AppendLine("                          text:  'claude', 'npm run dev', 'git status'");
        sb.AppendLine("  window_info()        - show focused window + list all visible windows");
        sb.AppendLine("  focus_window(title)  - bring window matching title to foreground");
        sb.AppendLine();
        sb.AppendLine("Built-in app commands (tell the user to type these directly, do NOT simulate them):");
        sb.AppendLine("  /schedule add \"HH:mm\" <tool> \"args\" — create a daily scheduled task inside this app");
        sb.AppendLine("  /schedules — list all scheduled tasks");
        sb.AppendLine("  /schedule delete \"name\" — delete a scheduled task");
        sb.AppendLine("  /workflows — list saved workflow sequences");
        sb.AppendLine("  /run <name> — replay a saved workflow locally");
        sb.AppendLine();
        sb.AppendLine("IMPORTANT - Scheduling:");
        sb.AppendLine("- When the user asks to schedule something daily (定时/计划/每天/定时任务),");
        sb.AppendLine("  tell them to use: /schedule add \"HH:mm\" <tool> \"args\"");
        sb.AppendLine("- Example: /schedule add \"09:00\" run_shell \"echo good morning\"");
        sb.AppendLine("- NEVER use run_shell to create Windows Task Scheduler tasks (schtasks, Register-ScheduledTask).");
        sb.AppendLine("  This app has its own built-in scheduler — use /schedule add instead.");
        sb.AppendLine();
        sb.AppendLine("Strategy - before sending keystrokes, ALWAYS verify the target:");
        sb.AppendLine("  1. window_info() to check which window has focus");
        sb.AppendLine("  2. If wrong window: focus_window(\"<correct app name>\") to switch");
        sb.AppendLine("  3. Then send_keys to interact");
        sb.AppendLine("- Never send keystrokes blindly — verify focus first.");
        sb.AppendLine("- To open IDE plugins: launch IDE → focus_window ↔ send_keys('Ctrl+Shift+A') →");
        sb.AppendLine("  send_keys('plugin name') → send_keys('Enter')");
        sb.AppendLine("- Prefer user-installed apps over system defaults.");
        sb.AppendLine("- After uninstalling, check for leftover files in:");
        sb.AppendLine("  %LOCALAPPDATA%/<app>, %APPDATA%/<app>, %PROGRAMDATA%/<app>");

        return sb.ToString();
    }

    public ChatAgentService(UserSettingsService settings, WorkflowRecorder recorder)
    {
        _settings = settings;
        _recorder = recorder;
    }

    /// <summary>懒初始化 MAF Agent + Session，若 Key 未配置则返回错误</summary>
    private async Task<string?> EnsureInitializedAsync()
    {
        if (_agent is not null)
            return null;

        var chatSettings = _settings.GetChatSettings();
        var apiKey = chatSettings.ApiKey
            ?? Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "sk-your-key-here")
            return "DeepSeek API 密钥未配置。请右键托盘图标 → 设置，配置 API Key，" +
                   "或通过 DEEPSEEK_API_KEY 环境变量设置。";

        var client = new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = new Uri(chatSettings.Endpoint) }
        );

        var chatClient = client.GetChatClient(chatSettings.Model);

        _agent = chatClient.AsAIAgent(
            instructions: SystemPrompt,
            name: "PersonalAssistant",
            tools: [
                AIFunctionFactory.Create(new Func<string, string>(ReadFile), name: "read_file"),
                AIFunctionFactory.Create(new Func<string, string, string>(WriteFile), name: "write_file"),
                AIFunctionFactory.Create(new Func<string?, string>(ListFiles), name: "list_files"),
                AIFunctionFactory.Create(new Func<string, Task<string>>(WebFetch), name: "web_fetch"),
                AIFunctionFactory.Create(new Func<string, string>(RunShell), name: "run_shell"),
                AIFunctionFactory.Create(new Func<string, string?, string>(RunCommand), name: "run_command"),
                AIFunctionFactory.Create(new Func<string, string>(FindApp), name: "find_app"),
                AIFunctionFactory.Create(new Func<string, string>(SendKeys), name: "send_keys"),
                AIFunctionFactory.Create(new Func<string>(WindowInfo), name: "window_info"),
                AIFunctionFactory.Create(new Func<string, string>(FocusWindow), name: "focus_window"),
            ]
        );

        _session = await _agent.CreateSessionAsync();
        return null;
    }

    /// <summary>
    /// 流式发送消息并返回 token 序列。
    /// </summary>
    public async IAsyncEnumerable<string> SendMessageStreaming(string message,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var initError = await EnsureInitializedAsync();
        if (initError is not null)
        {
            yield return initError;
            yield break;
        }

        await foreach (var update in _agent!.RunStreamingAsync(message, _session!, cancellationToken: ct))
        {
            if (!string.IsNullOrEmpty(update.Text))
                yield return update.Text;
        }
    }

    /// <summary>
    /// 清空对话历史（创建新 Session）。
    /// </summary>
    public async Task ClearHistoryAsync()
    {
        if (_agent is not null)
            _session = await _agent.CreateSessionAsync();
    }

    /// <summary>
    /// 供 WorkflowExecutor 直接调用工具方法（不经过 AI）。
    /// </summary>
    public Task<string> ExecuteToolStepAsync(string toolName, string args)
    {
        var result = toolName switch
        {
            "read_file" => ReadFile(args),
            "write_file" => WriteFile(args, ""),
            "list_files" => ListFiles(args),
            "web_fetch" => WebFetch(args).GetAwaiter().GetResult(),
            "run_shell" => RunShell(args),
            "run_command" => RunCommand(args, null),
            "find_app" => FindApp(args),
            "send_keys" => SendKeys(args),
            "window_info" => WindowInfo(),
            "focus_window" => FocusWindow(args),
            _ => $"未知工具: {toolName}"
        };
        return Task.FromResult(result);
    }

    // ═══════════════════════════════════════════════════════════════
    // 工具: read_file
    // ═══════════════════════════════════════════════════════════════

    [Description("Read the contents of a file at the given path")]
    private string ReadFile(
        [Description("Absolute or relative path to the file")] string path)
    {
        _recorder.RecordStep("read_file", path);
        try
        {
            if (!File.Exists(path)) return $"文件未找到: {path}";
            var content = File.ReadAllText(path);
            if (content.Length > 10000) content = content[..10000] + "\n... (已截断)";
            return content;
        }
        catch (Exception ex) { Log.Error(ex, "读取文件出错: {Path}", path); return $"读取文件出错: {ex.Message}"; }
    }

    // ═══════════════════════════════════════════════════════════════
    // 工具: write_file
    // ═══════════════════════════════════════════════════════════════

    [Description("Write text content to a file. Creates parent directories if needed.")]
    private string WriteFile(
        [Description("Path where the file should be written")] string path,
        [Description("Text content to write to the file")] string content)
    {
        _recorder.RecordStep("write_file", $"{path} ({content.Length} chars)");
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, content);
            return $"成功写入 {content.Length} 个字符到 {path}";
        }
        catch (Exception ex) { Log.Error(ex, "写入文件出错: {Path}", path); return $"写入文件出错: {ex.Message}"; }
    }

    // ═══════════════════════════════════════════════════════════════
    // 工具: list_files
    // ═══════════════════════════════════════════════════════════════

    [Description("List files and subdirectories in a directory. Defaults to current directory if omitted.")]
    private string ListFiles(
        [Description("Directory path to list")] string? path = null)
    {
        _recorder.RecordStep("list_files", path ?? ".");
        try
        {
            var dir = path ?? Directory.GetCurrentDirectory();
            if (!Directory.Exists(dir)) return $"目录未找到: {dir}";
            var entries = Directory.GetFileSystemEntries(dir)
                .Select(e => $"{(Directory.Exists(e) ? "[DIR] " : "[FILE]")} {Path.GetFileName(e)}");
            return string.Join("\n", entries);
        }
        catch (Exception ex) { Log.Error(ex, "列出文件出错: {Path}", path); return $"列出文件出错: {ex.Message}"; }
    }

    // ═══════════════════════════════════════════════════════════════
    // 工具: web_fetch
    // ═══════════════════════════════════════════════════════════════

    [Description("Fetch and return text content from a URL")]
    private async Task<string> WebFetch(
        [Description("The URL to fetch content from")] string url)
    {
        _recorder.RecordStep("web_fetch", url);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        try
        {
            var response = await http.GetStringAsync(url);
            if (response.Length > 8000) response = response[..8000] + "\n... (已截断)";
            return response;
        }
        catch (Exception ex) { Log.Error(ex, "抓取网页出错: {Url}", url); return $"抓取网页出错: {ex.Message}"; }
    }

    // ═══════════════════════════════════════════════════════════════
    // 工具: run_shell — CLI 命令，捕获输出
    // ═══════════════════════════════════════════════════════════════

    [Description(
        "Execute a PowerShell command and return its captured output.\n" +
        "Covers: file management, process/service control, registry, recycle bin, etc.\n" +
        "Common cmd aliases work: dir, type, del, copy, cd, echo, mkdir.\n" +
        "System operations: Clear-RecycleBin -Force, Get-Service, Stop-Process, etc.\n" +
        "Do NOT use for: launching GUI programs (use run_command instead).\n" +
        "Timeout: 15 seconds. Max output: ~100KB.")]
    private string RunShell(
        [Description("PowerShell command to execute")] string command)
    {
        _recorder.RecordStep("run_shell", command);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(command);

            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(15000))
            {
                process.Kill(entireProcessTree: true);
                return "错误: 命令执行超时 (15秒)";
            }

            var result = output;
            if (!string.IsNullOrWhiteSpace(error))
                result += (result.Length > 0 ? "\n" : "") + error;
            if (result.Length > 100000)
                result = result[..100000] + "\n... (已截断)";
            return string.IsNullOrWhiteSpace(result) ? "(无输出)" : result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "执行命令出错: {Command}", command);
            return $"执行命令出错: {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 工具: run_command — 启动程序，Shell 自动处理 PATH/UAC/关联
    // ═══════════════════════════════════════════════════════════════

    [Description(
        "Launch a program or open a file/URL using Windows Shell (ShellExecute).\n" +
        "Shell handles: PATH lookup, UAC elevation, file associations.\n" +
        "Returns immediately after launching — does NOT wait for exit.\n" +
        "Use for: starting GUI apps, browsers, documents, URLs, installers.\n" +
        "For commands that need output/exit-code, use run_shell instead.\n" +
        "Examples: run_command(\"PixPin\"), run_command(\"notepad\", \"file.txt\"),\n" +
        "  run_command(\"https://google.com\"), run_command(\"explorer\", \".\")")]
    private string RunCommand(
        [Description("Program name / full path / URL")] string exe,
        [Description("Optional command-line arguments")] string? args = null)
    {
        _recorder.RecordStep("run_command", $"{exe} {args ?? ""}");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
            };

            if (!string.IsNullOrEmpty(args))
                psi.Arguments = args;

            var process = Process.Start(psi);
            if (process is null)
                return "已启动 (ShellExecute)";

            // Don't wait — returns immediately so caller can send_keys etc.
            int pid = process.Id;
            // Dispose releases Process handle but doesn't kill the process
            process.Dispose();
            return $"已启动 (PID {pid})";
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            return $"未找到程序: {exe}。请使用 run_shell 执行 'where {exe}' 查找正确位置。";
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return "用户取消了管理员权限确认。";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "启动程序出错: {Exe} {Args}", exe, args);
            return $"启动失败: {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 工具: find_app — 搜索开始菜单中的已安装程序
    // ═══════════════════════════════════════════════════════════════

    [Description(
        "Search the Windows Start Menu for installed applications matching a keyword.\n" +
        "Returns app names and their executable paths. Searches both per-user and all-users shortcuts.\n" +
        "Use this BEFORE launching any app the user asks for — prefer user-installed tools over system defaults.")]
    private string FindApp(
        [Description("Keyword to search for (e.g., 'screenshot', 'editor', 'postman')")] string keyword)
    {
        _recorder.RecordStep("find_app", keyword);
        try
        {
            var results = new List<string>();
            var startMenuPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs)),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)),
            };

            foreach (var startMenu in startMenuPaths)
            {
                if (!Directory.Exists(startMenu)) continue;
                var links = Directory.GetFiles(startMenu, "*.lnk", SearchOption.AllDirectories);

                foreach (var link in links)
                {
                    var name = Path.GetFileNameWithoutExtension(link);
                    if (!name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var target = ResolveShortcut(link) ?? "(unknown)";
                    results.Add($"{name} -> {target}");
                }
            }

            return results.Count > 0
                ? string.Join("\n", results.OrderBy(r => r))
                : $"未找到匹配 '{keyword}' 的开始菜单程序。可尝试用 run_shell 执行 'Get-StartApps' 查找。";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "搜索开始菜单程序出错: {Keyword}", keyword);
            return $"搜索出错: {ex.Message}";
        }
    }

    /// <summary>解析 .lnk 快捷方式的目标路径，失败返回 null</summary>
    private static string? ResolveShortcut(string linkPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return null;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(linkPath);
            string target = shortcut.TargetPath;
            return target;
        }
        catch
        {
            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 工具: send_keys — Win32 keybd_event 硬件级按键注入
    // ═══════════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════
    // 工具: window_info — 当前焦点窗口 + 所有可见窗口列表
    // ═══════════════════════════════════════════════════════════════

    [Description(
        "Get info about the currently focused window and list all visible top-level windows.\n" +
        "Returns: focused window title/class/process, plus list of other windows.\n" +
        "Use BEFORE sending keystrokes to verify the correct window has focus.")]
    private string WindowInfo()
    {
        _recorder.RecordStep("window_info", "");
        try
        {
            var sb = new StringBuilder();
            var focused = GetForegroundWindow();
            if (focused != IntPtr.Zero)
            {
                var title = GetWindowTitle(focused);
                var cls = GetWindowClass(focused);
                GetWindowThreadProcessId(focused, out uint pid);
                string procName = "?";
                try { procName = Process.GetProcessById((int)pid).ProcessName; }
                catch { }

                sb.AppendLine($"FOCUSED: [{cls}] \"{title}\" ({procName}.exe, PID {pid})");
            }
            else
            {
                sb.AppendLine("FOCUSED: (none)");
            }

            // Enumerate visible top-level windows
            sb.AppendLine();
            sb.AppendLine("Visible windows:");
            var windows = new List<(IntPtr hWnd, string title, string cls)>();
            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd)) return true;
                if (hWnd == focused) return true; // skip focused (already shown)
                var title = GetWindowTitle(hWnd);
                if (string.IsNullOrWhiteSpace(title)) return true;
                var cls = GetWindowClass(hWnd);
                windows.Add((hWnd, title, cls));
                return true;
            }, IntPtr.Zero);

            int count = 0;
            foreach (var (_, title, cls) in windows.OrderBy(w => w.title))
            {
                sb.AppendLine($"  [{cls}] \"{title}\"");
                if (++count >= 20) { sb.AppendLine($"  ... ({windows.Count - 20} more)"); break; }
            }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "window_info 出错");
            return $"获取窗口信息出错: {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 工具: focus_window — 根据标题查找窗口并设为焦点
    // ═══════════════════════════════════════════════════════════════

    [Description(
        "Find a visible window whose title contains the given text, and bring it to foreground.\n" +
        "Use when keystrokes are going to the wrong window.\n" +
        "Example: focus_window('IntelliJ IDEA'), focus_window('Visual Studio')")]
    private string FocusWindow(
        [Description("Partial window title to search for")] string titlePart)
    {
        _recorder.RecordStep("focus_window", titlePart);
        try
        {
            IntPtr found = IntPtr.Zero;
            string foundTitle = "";

            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd)) return true;
                var title = GetWindowTitle(hWnd);
                if (string.IsNullOrWhiteSpace(title)) return true;
                if (title.Contains(titlePart, StringComparison.OrdinalIgnoreCase))
                {
                    found = hWnd;
                    foundTitle = title;
                    return false; // stop enumeration
                }
                return true;
            }, IntPtr.Zero);

            if (found == IntPtr.Zero)
                return $"未找到标题包含 '{titlePart}' 的窗口";

            SetForegroundWindow(found);
            return $"已聚焦: \"{foundTitle}\"";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "focus_window 出错: {Title}", titlePart);
            return $"聚焦窗口出错: {ex.Message}";
        }
    }

    private static string GetWindowTitle(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetWindowClass(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    // Virtual key codes for common keys
    private static readonly Dictionary<string, ushort> VkMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Alt"] = 0x12,        ["Ctrl"] = 0x11,       ["Shift"] = 0x10,
        ["Win"] = 0x5B,        ["Enter"] = 0x0D,       ["Tab"] = 0x09,
        ["Escape"] = 0x1B,     ["Esc"] = 0x1B,         ["Space"] = 0x20,
        ["Backspace"] = 0x08,  ["Delete"] = 0x2E,      ["Del"] = 0x2E,
        ["Insert"] = 0x2D,     ["Ins"] = 0x2D,         ["Home"] = 0x24,
        ["End"] = 0x23,        ["PgUp"] = 0x21,        ["PgDn"] = 0x22,
        ["Up"] = 0x26,         ["Down"] = 0x28,        ["Left"] = 0x25,
        ["Right"] = 0x27,      ["PrtSc"] = 0x2C,       ["PrintScreen"] = 0x2C,
        ["Scroll"] = 0x91,     ["Pause"] = 0x13,
        ["F1"] = 0x70,  ["F2"] = 0x71,  ["F3"] = 0x72,  ["F4"] = 0x73,
        ["F5"] = 0x74,  ["F6"] = 0x75,  ["F7"] = 0x76,  ["F8"] = 0x77,
        ["F9"] = 0x78,  ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B,
    };

    [Description(
        "Send keystrokes at the hardware input level via Win32 SendInput.\n" +
        "Two modes:\n" +
        "  Key combo (contains '+'): 'Ctrl+Alt+A', 'Win+Shift+S', 'Alt+F4'\n" +
        "  Typing text (no '+'): 'claude', 'npm install', 'hello world'\n" +
        "Special keys: Enter, Tab, F1-F12, Esc, Space, Home, End, PrtSc.")]
    private string SendKeys(
        [Description("Key combo like 'Ctrl+Alt+A', or plain text to type")] string input)
    {
        _recorder.RecordStep("send_keys", input);
        try
        {
            if (input.Contains('+'))
                return SendCombo(input);
            else
                return TypeText(input);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "send_keys 出错: {Input}", input);
            return $"发送按键出错: {ex.Message}";
        }
    }

    private string SendCombo(string hotkey)
    {
        var parts = hotkey.Split('+', StringSplitOptions.TrimEntries);
        var inputs = new List<INPUT>();

        foreach (var part in parts)
        {
            if (!TryGetVk(part, out ushort vk))
                return $"无法识别的按键: '{part}'";

            bool isExtended = vk is 0x12 or 0x5B;
            var scan = (ushort)MapVirtualKey(vk, 0);

            inputs.Add(new INPUT
            {
                type = INPUT_KEYBOARD,
                ki = new KEYBDINPUT
                {
                    wVk = vk, wScan = scan,
                    dwFlags = KEYEVENTF_KEYDOWN | (isExtended ? KEYEVENTF_EXTENDEDKEY : 0),
                }
            });
        }

        for (int i = inputs.Count - 1; i >= 0; i--)
        {
            var ki = inputs[i].ki;
            ki.dwFlags = KEYEVENTF_KEYUP | (ki.dwFlags & KEYEVENTF_EXTENDEDKEY);
            inputs.Add(new INPUT { type = INPUT_KEYBOARD, ki = ki });
        }

        var array = inputs.ToArray();
        var sent = SendInput((uint)array.Length, array, Marshal.SizeOf<INPUT>());
        return sent == array.Length
            ? $"已发送按键: {hotkey}"
            : $"按键发送不全 (sent {sent}/{array.Length})";
    }

    private string TypeText(string text)
    {
        // Safety: release all modifiers first to prevent ghost shortcuts
        ReleaseModifiers();

        var inputs = new List<INPUT>();

        foreach (var ch in text)
        {
            // Skip control characters
            if (ch < ' ' && ch != '\t') continue;

            // Use virtual key for ASCII (more reliable), Unicode for non-ASCII
            short scanResult = VkKeyScan(ch);
            if (scanResult != -1 && (scanResult & 0xFF) != 0xFF)
            {
                ushort vk = (ushort)(scanResult & 0xFF);
                bool needsShift = (scanResult & 0x100) != 0;
                if (needsShift)
                {
                    inputs.Add(new INPUT
                    {
                        type = INPUT_KEYBOARD,
                        ki = new KEYBDINPUT { wVk = 0x10, dwFlags = KEYEVENTF_KEYDOWN }
                    });
                }

                var scan = (ushort)MapVirtualKey(vk, 0);
                inputs.Add(new INPUT
                {
                    type = INPUT_KEYBOARD,
                    ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = KEYEVENTF_KEYDOWN }
                });
                inputs.Add(new INPUT
                {
                    type = INPUT_KEYBOARD,
                    ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = KEYEVENTF_KEYUP }
                });

                if (needsShift)
                {
                    inputs.Add(new INPUT
                    {
                        type = INPUT_KEYBOARD,
                        ki = new KEYBDINPUT { wVk = 0x10, dwFlags = KEYEVENTF_KEYUP }
                    });
                }
            }
            else
            {
                // Fallback: Unicode input for non-mappable characters
                inputs.Add(new INPUT
                {
                    type = INPUT_KEYBOARD,
                    ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE }
                });
                inputs.Add(new INPUT
                {
                    type = INPUT_KEYBOARD,
                    ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP }
                });
            }
        }

        if (inputs.Count == 0) return "无有效字符可输入";

        var array = inputs.ToArray();
        var sent = SendInput((uint)array.Length, array, Marshal.SizeOf<INPUT>());
        return sent == array.Length
            ? $"已输入: {text}"
            : $"输入部分失败 (sent {sent}/{array.Length})";
    }

    /// <summary>释放所有可能卡住的修饰键</summary>
    private static void ReleaseModifiers()
    {
        var mods = new[] { (ushort)0x11, (ushort)0x12, (ushort)0x10, (ushort)0x5B }; // Ctrl, Alt, Shift, Win
        foreach (var vk in mods)
        {
            var inputs = new INPUT[1]
            {
                new INPUT
                {
                    type = INPUT_KEYBOARD,
                    ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP }
                }
            };
            SendInput(1, inputs, Marshal.SizeOf<INPUT>());
        }
    }

    private static bool TryGetVk(string key, out ushort vk)
    {
        if (VkMap.TryGetValue(key, out vk))
            return true;

        if (key.Length == 1)
        {
            var result = VkKeyScan(key[0]);
            if (result != -1)
            {
                vk = (ushort)(result & 0xFF);
                return true;
            }
        }

        return false;
    }

}
