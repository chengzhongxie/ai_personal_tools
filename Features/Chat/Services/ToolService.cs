using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace PersonalAssistant.Features.Chat.Services;

/// <summary>
/// 工具执行服务实现，提供文件读写、目录列表、网页抓取和 Shell 命令执行五种工具
/// </summary>
public class ToolService : IToolService
{
    /// <inheritdoc />
    public Task<string> ExecuteToolAsync(string name, string argumentsJson)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var args = doc.RootElement;

        string? GetArg(string key) =>
            args.TryGetProperty(key, out var v) ? v.GetString() : null;

        var result = name switch
        {
            "read_file" => ToolReadFile(GetArg("path")!),
            "write_file" => ToolWriteFile(GetArg("path")!, GetArg("content")!),
            "list_files" => ToolListFiles(GetArg("path")),
            "web_fetch" => ToolWebFetch(GetArg("url")!).GetAwaiter().GetResult(),
            "run_shell" => ToolRunShell(GetArg("command")!),
            _ => $"Unknown tool: {name}"
        };

        return Task.FromResult(result);
    }

    /// <summary>读取指定路径的文件内容，超过 10000 字符时截断</summary>
    private static string ToolReadFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return $"Error: File not found: {path}";
            var content = File.ReadAllText(path);
            if (content.Length > 10000)
                content = content[..10000] + "\n... (truncated)";
            return content;
        }
        catch (Exception ex) { return $"Error reading file: {ex.Message}"; }
    }

    /// <summary>将文本内容写入指定路径的文件，自动创建父目录</summary>
    private static string ToolWriteFile(string path, string content)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, content);
            return $"Successfully wrote {content.Length} characters to {path}";
        }
        catch (Exception ex) { return $"Error writing file: {ex.Message}"; }
    }

    /// <summary>列出目录下的文件和子目录，前缀标记 [DIR] / [FILE]</summary>
    private static string ToolListFiles(string? path = null)
    {
        try
        {
            var dir = path ?? Directory.GetCurrentDirectory();
            if (!Directory.Exists(dir))
                return $"Error: Directory not found: {dir}";
            var entries = Directory.GetFileSystemEntries(dir)
                .Select(e => $"{(Directory.Exists(e) ? "[DIR] " : "[FILE]")} {Path.GetFileName(e)}");
            return string.Join("\n", entries);
        }
        catch (Exception ex) { return $"Error listing files: {ex.Message}"; }
    }

    /// <summary>抓取指定 URL 的文本内容，15 秒超时，超过 8000 字符时截断</summary>
    private static async Task<string> ToolWebFetch(string url)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        try
        {
            var response = await http.GetStringAsync(url);
            if (response.Length > 8000)
                response = response[..8000] + "\n... (truncated)";
            return response;
        }
        catch (Exception ex) { return $"Error fetching URL: {ex.Message}"; }
    }

    /// <summary>执行 Shell 命令并返回输出，5 秒超时，自动适配 Windows/Linux</summary>
    private static string ToolRunShell(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash",
                Arguments = OperatingSystem.IsWindows() ? $"/c {command}" : $"-c \"{command}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(milliseconds: 5000);

            var result = output;
            if (!string.IsNullOrWhiteSpace(error))
                result += "\n[stderr]\n" + error;
            return string.IsNullOrWhiteSpace(result) ? "(no output)" : result;
        }
        catch (Exception ex) { return $"Error running command: {ex.Message}"; }
    }
}
