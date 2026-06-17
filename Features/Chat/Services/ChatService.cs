using System.ClientModel;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using Serilog;
using AppModels = PersonalAssistant.Features.Chat.Models;

namespace PersonalAssistant.Features.Chat.Services;

public class ChatService : IChatService
{
    private readonly IToolService _toolService;
    private readonly ChatClient _chatClient;
    private readonly ChatCompletionOptions _chatOptions;
    private readonly List<ChatMessage> _messages;

    public ChatService(IToolService toolService, IOptions<AppModels.ChatSettings> options)
    {
        _toolService = toolService;

        var settings = options.Value;
        var apiKey = settings.ApiKey
            ?? Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "sk-your-key-here")
            throw new InvalidOperationException(
                "DeepSeek API Key not configured. Set it in appsettings.json (ChatSettings:ApiKey) " +
                "or via the DEEPSEEK_API_KEY environment variable.");

        var openAiClient = new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = new Uri(settings.Endpoint) }
        );
        _chatClient = openAiClient.GetChatClient(settings.Model);

        _chatOptions = new ChatCompletionOptions();
        foreach (var tool in CreateToolDefs())
            _chatOptions.Tools.Add(tool);

        _messages = new List<ChatMessage>
        {
            new SystemChatMessage(
                "You are a helpful personal AI assistant running on the user's machine. " +
                "You are powered by DeepSeek. " +
                "You can read/write files, list directories, fetch web content, and run shell commands. " +
                "Use these tools when helpful. Be concise and practical. " +
                $"The current working directory is: {Environment.CurrentDirectory}")
        };
    }

    public async Task<AppModels.ChatResponse> SendMessageAsync(string userMessage)
    {
        try
        {
            _messages.Add(new UserChatMessage(userMessage));

            var toolCallNames = new List<string>();

            while (true)
            {
                var result = await _chatClient.CompleteChatAsync(_messages, _chatOptions);
                var completion = result.Value;

                if (completion.ToolCalls.Count == 0)
                {
                    var text = completion.Content.Count > 0
                        ? string.Join("", completion.Content.Select(c => c.Text))
                        : "";
                    _messages.Add(new AssistantChatMessage(completion));
                    return new AppModels.ChatResponse
                    {
                        Content = text,
                        ToolCalls = toolCallNames
                    };
                }

                _messages.Add(new AssistantChatMessage(completion));

                foreach (var toolCall in completion.ToolCalls)
                {
                    Log.Information("[Tool] {ToolName}", toolCall.FunctionName);
                    toolCallNames.Add(toolCall.FunctionName);

                    var resultText = await _toolService.ExecuteToolAsync(
                        toolCall.FunctionName,
                        toolCall.FunctionArguments.ToString());
                    _messages.Add(new ToolChatMessage(toolCall.Id, resultText));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in SendMessageAsync");
            return new AppModels.ChatResponse
            {
                IsError = true,
                ErrorMessage = ex.Message
            };
        }
    }

    public void ClearHistory()
    {
        _messages.RemoveRange(1, _messages.Count - 1);
    }

    private static List<ChatTool> CreateToolDefs()
    {
        return new List<ChatTool>
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
    }

    private static BinaryData CreateToolParams(
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
}
