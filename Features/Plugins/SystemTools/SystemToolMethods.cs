using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using PersonalAssistant.Core.Interfaces;

namespace PersonalAssistant.Features.Plugins.SystemTools;

/// <summary>
/// 13 个系统工具方法的静态实现（从 ChatAgentService 提取）。
/// 录制由 PluginAggregator 透明处理，危险确认通过 IDangerousToolPolicy 注入。
/// </summary>
internal static class SystemToolMethods
{
    // ═══════════════════════════════════════════════════════════════
    // read_file
    // ═══════════════════════════════════════════════════════════════

    [Description("Read the contents of a file at the given path")]
    public static string ReadFile(
        [Description("Absolute or relative path to the file")] string path)
    {
        try
        {
            if (!File.Exists(path)) return $"文件未找到: {path}";
            var content = File.ReadAllText(path);
            if (content.Length > 10000) content = content[..10000] + "\n... (已截断)";
            return content;
        }
        catch (Exception ex) { return $"读取文件出错: {ex.Message}"; }
    }

    // ═══════════════════════════════════════════════════════════════
    // write_file
    // ═══════════════════════════════════════════════════════════════

    [Description("Write text content to a file. Creates parent directories if needed.")]
    public static string WriteFile(
        [Description("Path where the file should be written")] string path,
        [Description("Text content to write to the file")] string content,
        IDangerousToolPolicy policy)
    {
        if (!policy.ConfirmDangerous("write_file", $"{path} ({content.Length} chars)"))
            return "用户取消了文件写入操作。";
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, content);
            return $"成功写入 {content.Length} 个字符到 {path}";
        }
        catch (Exception ex) { return $"写入文件出错: {ex.Message}"; }
    }

    // ═══════════════════════════════════════════════════════════════
    // list_files
    // ═══════════════════════════════════════════════════════════════

    [Description("List files and subdirectories in a directory. Defaults to current directory if omitted.")]
    public static string ListFiles(
        [Description("Directory path to list")] string? path = null)
    {
        try
        {
            var dir = path ?? Directory.GetCurrentDirectory();
            if (!Directory.Exists(dir)) return $"目录未找到: {dir}";
            var entries = Directory.GetFileSystemEntries(dir)
                .Select(e => $"{(Directory.Exists(e) ? "[DIR] " : "[FILE]")} {Path.GetFileName(e)}");
            return string.Join("\n", entries);
        }
        catch (Exception ex) { return $"列出文件出错: {ex.Message}"; }
    }

    // ═══════════════════════════════════════════════════════════════
    // run_shell
    // ═══════════════════════════════════════════════════════════════

    [Description(
        "Execute a PowerShell command and return its captured output.\n" +
        "Covers: file management, process/service control, registry, recycle bin, etc.\n" +
        "Common cmd aliases work: dir, type, del, copy, cd, echo, mkdir.\n" +
        "System operations: Clear-RecycleBin -Force, Get-Service, Stop-Process, etc.\n" +
        "Do NOT use for: launching GUI programs (use run_command instead).\n" +
        "Timeout: 15 seconds. Max output: ~100KB.")]
    public static string RunShell(
        [Description("PowerShell command to execute")] string command,
        IDangerousToolPolicy policy)
    {
        if (!policy.ConfirmDangerous("run_shell", command))
            return "用户取消了该命令的执行。";
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
            return $"执行命令出错: {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // run_command
    // ═══════════════════════════════════════════════════════════════

    [Description(
        "Launch a program or open a file/URL using Windows Shell (ShellExecute).\n" +
        "Shell handles: PATH lookup, UAC elevation, file associations.\n" +
        "Returns immediately after launching — does NOT wait for exit.\n" +
        "Use for: starting GUI apps, browsers, documents, URLs, installers.\n" +
        "For commands that need output/exit-code, use run_shell instead.\n" +
        "Examples: run_command(\"PixPin\"), run_command(\"notepad\", \"file.txt\"),\n" +
        "  run_command(\"https://google.com\"), run_command(\"explorer\", \".\")")]
    public static string RunCommand(
        [Description("Program name / full path / URL")] string exe,
        [Description("Optional command-line arguments")] string? args = null)
    {
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

            int pid = process.Id;
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
            return $"启动失败: {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // find_app
    // ═══════════════════════════════════════════════════════════════

    [Description(
        "Search the Windows Start Menu for installed applications matching a keyword.\n" +
        "Returns app names and executable paths. Searches both per-user and all-users shortcuts.\n" +
        "Use this BEFORE launching any app the user asks for — prefer user-installed tools over system defaults.")]
    public static string FindApp(
        [Description("Keyword to search for (e.g., 'screenshot', 'editor', 'postman')")] string keyword)
    {
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
            return $"搜索出错: {ex.Message}";
        }
    }

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
    // window_info
    // ═══════════════════════════════════════════════════════════════

    [Description(
        "Get info about the currently focused window and list all visible top-level windows.\n" +
        "Returns: focused window title/class/process, plus list of other windows.\n" +
        "Use BEFORE sending keystrokes to verify the correct window has focus.")]
    public static string WindowInfo()
    {
        try
        {
            var sb = new StringBuilder();
            var focused = Win32Native.GetForegroundWindow();
            if (focused != IntPtr.Zero)
            {
                var title = Win32Native.GetWindowTitle(focused);
                var cls = Win32Native.GetWindowClass(focused);
                Win32Native.GetWindowThreadProcessId(focused, out uint pid);
                string procName = "?";
                try { procName = Process.GetProcessById((int)pid).ProcessName; }
                catch { }

                sb.AppendLine($"FOCUSED: [{cls}] \"{title}\" ({procName}.exe, PID {pid})");
            }
            else
            {
                sb.AppendLine("FOCUSED: (none)");
            }

            sb.AppendLine();
            sb.AppendLine("Visible windows:");
            var windows = new List<(IntPtr hWnd, string title, string cls)>();
            Win32Native.EnumWindows((hWnd, _) =>
            {
                if (!Win32Native.IsWindowVisible(hWnd)) return true;
                if (hWnd == focused) return true;
                var title = Win32Native.GetWindowTitle(hWnd);
                if (string.IsNullOrWhiteSpace(title)) return true;
                var cls = Win32Native.GetWindowClass(hWnd);
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
            return $"获取窗口信息出错: {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // focus_window
    // ═══════════════════════════════════════════════════════════════

    [Description(
        "Find a visible window whose title contains the given text, and bring it to foreground.\n" +
        "Use when keystrokes are going to the wrong window.\n" +
        "Example: focus_window('IntelliJ IDEA'), focus_window('Visual Studio')")]
    public static string FocusWindow(
        [Description("Partial window title to search for")] string titlePart)
    {
        try
        {
            IntPtr found = IntPtr.Zero;
            string foundTitle = "";

            Win32Native.EnumWindows((hWnd, _) =>
            {
                if (!Win32Native.IsWindowVisible(hWnd)) return true;
                var title = Win32Native.GetWindowTitle(hWnd);
                if (string.IsNullOrWhiteSpace(title)) return true;
                if (title.Contains(titlePart, StringComparison.OrdinalIgnoreCase))
                {
                    found = hWnd;
                    foundTitle = title;
                    return false;
                }
                return true;
            }, IntPtr.Zero);

            if (found == IntPtr.Zero)
                return $"未找到标题包含 '{titlePart}' 的窗口";

            Win32Native.SetForegroundWindow(found);
            return $"已聚焦: \"{foundTitle}\"";
        }
        catch (Exception ex)
        {
            return $"聚焦窗口出错: {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // send_keys
    // ═══════════════════════════════════════════════════════════════

    [Description(
        "Send keystrokes at the hardware input level via Win32 SendInput.\n" +
        "Two modes:\n" +
        "  Key combo (contains '+'): 'Ctrl+Alt+A', 'Win+Shift+S', 'Alt+F4'\n" +
        "  Typing text (no '+'): 'claude', 'npm install', 'hello world'\n" +
        "Special keys: Enter, Tab, F1-F12, Esc, Space, Home, End, PrtSc.")]
    public static string SendKeys(
        [Description("Key combo like 'Ctrl+Alt+A', or plain text to type")] string input)
    {
        try
        {
            if (input.Contains('+'))
                return Win32Native.SendCombo(input);
            else
                return Win32Native.TypeTextImpl(input);
        }
        catch (Exception ex)
        {
            return $"发送按键出错: {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // type_text
    // ═══════════════════════════════════════════════════════════════

    [Description(
        "Type text directly into the foreground application at the cursor position.\n" +
        "Use this to output results back to the user's active app (editor, text box, terminal, etc.).\n" +
        "Works with any text including Chinese characters. Supports long text blocks.\n" +
        "Before typing, verify target window with window_info() first.")]
    public static string TypeText(
        [Description("The text to type into the foreground application")] string text)
    {
        return Win32Native.TypeTextImpl(text);
    }

    // ═══════════════════════════════════════════════════════════════
    // read_clipboard
    // ═══════════════════════════════════════════════════════════════

    [Description("Read text content from the Windows clipboard")]
    public static string ReadClipboard()
    {
        try
        {
            string? result = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (Clipboard.ContainsText())
                    result = Clipboard.GetText();
            });
            return result ?? "(剪贴板为空或非文本内容)";
        }
        catch (Exception ex)
        {
            return $"读取剪贴板出错: {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // write_clipboard
    // ═══════════════════════════════════════════════════════════════

    [Description("Write text to the Windows clipboard")]
    public static string WriteClipboard(
        [Description("Text to write to the clipboard")] string text)
    {
        try
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Clipboard.SetText(text);
            });
            return $"已复制到剪贴板 ({text.Length} 个字符)";
        }
        catch (Exception ex)
        {
            return $"写入剪贴板出错: {ex.Message}";
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // search_files
    // ═══════════════════════════════════════════════════════════════

    [Description(
        "Recursively search for files matching a name pattern.\n" +
        "MUCH faster than list_files for targeted searches — uses lazy enumeration.\n" +
        "Pattern: '*.pdf', 'report*', '*2024*.xlsx', or 'photo' (auto appends *).\n" +
        "Directory: defaults to user profile. Cap: 100 results, 10s timeout.\n" +
        "Example: search_files(\"*.docx\", \"C:\\\\Users\"), search_files(\"Claude.md\")")]
    public static string SearchFiles(
        [Description("File name pattern (e.g. '*.pdf', 'budget*.xlsx')")] string pattern,
        [Description("Directory to start searching from, defaults to user profile")] string? directory = null)
    {
        try
        {
            var dir = directory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!Directory.Exists(dir))
                return $"目录未找到: {dir}";

            if (!pattern.Contains('*') && !pattern.Contains('?'))
                pattern = $"*{pattern}*";

            var results = new List<string>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var opts = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MaxRecursionDepth = 12,
            };

            try
            {
                var files = Directory.EnumerateFiles(dir, pattern, opts);
                foreach (var file in files)
                {
                    if (cts.IsCancellationRequested)
                        break;
                    if (results.Count >= 100)
                        break;

                    results.Add(file);
                }
            }
            catch (OperationCanceledException) { }
            catch (UnauthorizedAccessException) { }

            if (results.Count == 0)
                return $"在 {dir} 中未找到匹配 \"{pattern}\" 的文件。";

            var grouped = results
                .Select(f => new { Path = f, Dir = Path.GetDirectoryName(f) ?? "" })
                .GroupBy(f => f.Dir)
                .Take(20);

            var sb = new StringBuilder();
            sb.AppendLine($"搜索: {pattern} → {results.Count} 个文件");
            if (results.Count >= 100)
                sb.AppendLine("(已达 100 条结果上限)");
            sb.AppendLine();

            foreach (var g in grouped)
            {
                sb.AppendLine($"[{g.Key}]");
                foreach (var f in g.Take(10))
                {
                    long size = 0;
                    try { size = new FileInfo(f.Path).Length; }
                    catch { }
                    sb.AppendLine($"  {Path.GetFileName(f.Path)} ({Win32Native.FormatBytes((ulong)size)})");
                }
                if (g.Count() > 10)
                    sb.AppendLine($"  ... ({g.Count() - 10} more)");
            }

            if (grouped.Count() > 20)
                sb.AppendLine($"... ({(grouped.Count() - 20)} more directories)");

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return $"搜索出错: {ex.Message}";
        }
    }
}
