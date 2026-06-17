using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace PersonalAssistant.Features.Chat.Services;

public class ToolService : IToolService
{
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
