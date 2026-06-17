using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

// ── Tool Schema Helpers ────────────────────────────────────────────

static BinaryData CreateToolParams(
    string[]? required = null,
    params (string Name, string Type, string Desc, string[]? Enum)[] properties)
{
    var props = new JsonObject();
    foreach (var (name, type, desc, enm) in properties)
    {
        var prop = new JsonObject
        {
            ["type"] = type,
            ["description"] = desc
        };
        if (enm is not null)
        {
            var arr = new JsonArray();
            foreach (var v in enm) arr.Add(v);
            prop["enum"] = arr;
        }
        props[name] = prop;
    }

    var schema = new JsonObject
    {
        ["type"] = "object",
        ["properties"] = props,
    };
    if (required is not null)
    {
        var reqArr = new JsonArray();
        foreach (var r in required) reqArr.Add(r);
        schema["required"] = reqArr;
    }
    return BinaryData.FromObjectAsJson(schema);
}

// ── Tool Definitions ───────────────────────────────────────────────

var toolDefs = new List<ChatTool>
{
    ChatTool.CreateFunctionTool("read_file", "Read the contents of a file at the given path",
        CreateToolParams(required: ["path"],
            ("path", "string", "Absolute or relative path to the file", null))),

    ChatTool.CreateFunctionTool("write_file", "Write text content to a file. Creates parent directories if needed.",
        CreateToolParams(required: ["path", "content"],
            ("path", "string", "Path where the file should be written", null),
            ("content", "string", "Text content to write to the file", null))),

    ChatTool.CreateFunctionTool("list_files", "List files and subdirectories in a directory.",
        CreateToolParams(required: null,
            ("path", "string", "Directory path to list. Defaults to current directory if omitted.", null))),

    ChatTool.CreateFunctionTool("web_fetch", "Fetch and return text content from a URL.",
        CreateToolParams(required: ["url"],
            ("url", "string", "The URL to fetch content from", null))),

    ChatTool.CreateFunctionTool("run_shell", "Execute a shell command and return its output.",
        CreateToolParams(required: ["command"],
            ("command", "string", "The shell command to execute", null))),
};

// ── Tool Implementations ───────────────────────────────────────────

string ToolReadFile(string path)
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

string ToolWriteFile(string path, string content)
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

string ToolListFiles(string? path = null)
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

async Task<string> ToolWebFetch(string url)
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

string ToolRunShell(string command)
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

async Task<string> ExecuteToolAsync(string name, string argumentsJson)
{
    using var doc = JsonDocument.Parse(argumentsJson);
    var args = doc.RootElement;

    string? GetArg(string key) =>
        args.TryGetProperty(key, out var v) ? v.GetString() : null;

    return name switch
    {
        "read_file" => ToolReadFile(GetArg("path")!),
        "write_file" => ToolWriteFile(GetArg("path")!, GetArg("content")!),
        "list_files" => ToolListFiles(GetArg("path")),
        "web_fetch" => await ToolWebFetch(GetArg("url")!),
        "run_shell" => ToolRunShell(GetArg("command")!),
        _ => $"Unknown tool: {name}"
    };
}

// ── Main Program ───────────────────────────────────────────────────

var builder = Host.CreateApplicationBuilder(args);

var apiKey = builder.Configuration["DeepSeek:ApiKey"]
    ?? Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");

if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "sk-your-key-here")
{
    Console.WriteLine("Error: DeepSeek API Key not configured.");
    Console.WriteLine("Set it in appsettings.json (DeepSeek:ApiKey)");
    Console.WriteLine("or via the DEEPSEEK_API_KEY environment variable.");
    return 1;
}

var model = builder.Configuration["DeepSeek:Model"] ?? "deepseek-chat";
var endpoint = builder.Configuration["DeepSeek:Endpoint"] ?? "https://api.deepseek.com/v1";

var openAiClient = new OpenAIClient(
    new ApiKeyCredential(apiKey),
    new OpenAIClientOptions { Endpoint = new Uri(endpoint) }
);
var chatClient = openAiClient.GetChatClient(model);

var chatOptions = new ChatCompletionOptions();
foreach (var tool in toolDefs) chatOptions.Tools.Add(tool);

var messages = new List<ChatMessage>
{
    new SystemChatMessage(
        $"You are a helpful personal AI assistant running in a console on the user's machine. " +
        $"You are powered by DeepSeek. " +
        $"You can read/write files, list directories, fetch web content, and run shell commands. " +
        $"Use these tools when helpful. Be concise and practical. " +
        $"The current working directory is: {Environment.CurrentDirectory}")
};

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("\n==============================================");
Console.WriteLine("  Personal AI Assistant");
Console.WriteLine("  Powered by DeepSeek + .NET 10");
Console.WriteLine("==============================================");
Console.WriteLine("  Commands:");
Console.WriteLine("    /exit   - Quit the assistant");
Console.WriteLine("    /clear  - Clear conversation history");
Console.WriteLine("==============================================\n");

while (true)
{
    Console.Write("You> ");
    var input = Console.ReadLine();

    if (input is null) break;
    if (string.IsNullOrWhiteSpace(input)) continue;
    if (input.Equals("/exit", StringComparison.OrdinalIgnoreCase)) break;
    if (input.Equals("/clear", StringComparison.OrdinalIgnoreCase))
    {
        messages.RemoveRange(1, messages.Count - 1);
        Console.WriteLine("[Conversation history cleared.]\n");
        continue;
    }

    messages.Add(new UserChatMessage(input));

    try
    {
        while (true)
        {
            var result = await chatClient.CompleteChatAsync(messages, chatOptions);
            var completion = result.Value;

            // If no tool calls, display the text response
            if (completion.ToolCalls.Count == 0)
            {
                var text = completion.Content.Count > 0
                    ? string.Join("", completion.Content.Select(c => c.Text))
                    : "";
                Console.WriteLine($"\nAssistant> {text}\n");
                messages.Add(new AssistantChatMessage(completion));
                break;
            }

            // Add the assistant message with tool calls
            messages.Add(new AssistantChatMessage(completion));

            // Execute each tool call
            var toolResults = new List<ChatMessage>();
            foreach (var toolCall in completion.ToolCalls)
            {
                Console.WriteLine($"  [{toolCall.FunctionName}]");
                var resultText = await ExecuteToolAsync(
                    toolCall.FunctionName,
                    toolCall.FunctionArguments.ToString());
                toolResults.Add(new ToolChatMessage(toolCall.Id, resultText));
            }
            messages.AddRange(toolResults);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n[Error]: {ex.Message}\n");
    }
}

return 0;
