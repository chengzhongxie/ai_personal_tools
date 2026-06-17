using System.ClientModel;
using System.Text.Json.Nodes;
using OpenAI;
using OpenAI.Chat;
using Serilog;
using PersonalAssistant.Infrastructure.Common.Services;
using AppModels = PersonalAssistant.Features.Chat.Models;

namespace PersonalAssistant.Features.Chat.Services;

/// <summary>
/// AI 聊天服务实现，通过 DeepSeek API 提供对话能力，支持自动工具调用循环
/// </summary>
public class ChatService : IChatService
{
    private const int MaxHistory = 200; // 含 system 消息，超出自动修剪

    private readonly IToolService _toolService;
    private readonly UserSettingsService _settings;
    private readonly List<ChatMessage> _messages;

    private ChatClient? _chatClient;
    private ChatCompletionOptions? _chatOptions;

    /// <summary>
    /// 初始化聊天服务，懒加载 API 客户端（首次发送消息时才校验 Key）
    /// </summary>
    public ChatService(IToolService toolService, UserSettingsService settings)
    {
        _toolService = toolService;
        _settings = settings;

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

    /// <summary>懒初始化 ChatClient，若 Key 未配置则返回错误</summary>
    private string? EnsureInitialized()
    {
        if (_chatClient is not null)
            return null;

        var chatSettings = _settings.GetChatSettings();
        var apiKey = chatSettings.ApiKey
            ?? Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "sk-your-key-here")
            return "DeepSeek API 密钥未配置。请右键托盘图标 → 设置，配置 API Key，" +
                   "或通过 DEEPSEEK_API_KEY 环境变量设置。";

        var openAiClient = new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = new Uri(chatSettings.Endpoint) }
        );
        _chatClient = openAiClient.GetChatClient(chatSettings.Model);

        _chatOptions = new ChatCompletionOptions();
        foreach (var tool in CreateToolDefs())
            _chatOptions.Tools.Add(tool);

        return null;
    }

    /// <inheritdoc />
    public async Task<AppModels.ChatResponse> SendMessageAsync(string userMessage)
    {
        try
        {
            var initError = EnsureInitialized();
            if (initError is not null)
                return new AppModels.ChatResponse { IsError = true, ErrorMessage = initError };

            _messages.Add(new UserChatMessage(userMessage));

            var toolCallNames = new List<string>();

            while (true)
            {
                var result = await _chatClient!.CompleteChatAsync(_messages, _chatOptions);
                var completion = result.Value;

                if (completion.ToolCalls.Count == 0)
                {
                    var text = completion.Content.Count > 0
                        ? string.Join("", completion.Content.Select(c => c.Text))
                        : "";
                    _messages.Add(new AssistantChatMessage(completion));
                    TrimHistory();
                    return new AppModels.ChatResponse
                    {
                        Content = text,
                        ToolCalls = toolCallNames
                    };
                }

                _messages.Add(new AssistantChatMessage(completion));

                foreach (var toolCall in completion.ToolCalls)
                {
                    Log.Debug("[Tool] {ToolName}", toolCall.FunctionName);
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
            Log.Error(ex, "SendMessageAsync 出错");
            return new AppModels.ChatResponse
            {
                IsError = true,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <inheritdoc />
    public void ClearHistory()
    {
        _messages.RemoveRange(1, _messages.Count - 1);
    }

    /// <summary>修剪历史到上限，保留系统消息（索引0）</summary>
    private void TrimHistory()
    {
        while (_messages.Count > MaxHistory)
            _messages.RemoveAt(1); // 移除最旧的非系统消息
    }

    /// <summary>创建 DeepSeek function calling 工具定义列表</summary>
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

    /// <summary>将工具参数定义转换为 OpenAI SDK 所需的 BinaryData JSON Schema 格式</summary>
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
