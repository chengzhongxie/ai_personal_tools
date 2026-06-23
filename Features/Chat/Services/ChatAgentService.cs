using System.ClientModel;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using PersonalAssistant.Core.Interfaces;
using PersonalAssistant.Core.Services;
using PersonalAssistant.Features.Workflow.Models;
using PersonalAssistant.Features.Workflow.Services;
using PersonalAssistant.Infrastructure.Common.Services;

namespace PersonalAssistant.Features.Chat.Services;

/// <summary>
/// 基于 Microsoft Agent Framework 的 AI 聊天服务，
/// 封装 AIAgent 生命周期、流式输出和模式建议收集。
/// 工具方法已全部提取到 Plugins/，通过 IToolPluginHost 聚合。
/// 资源成本：仅消息发送时消耗，空闲时零开销（事件驱动）。
/// </summary>
public class ChatAgentService
{
    private readonly UserSettingsService _settings;
    private readonly IToolPluginHost _pluginHost;
    private readonly PatternDetector _patternDetector;
    private readonly WorkflowRecorder _recorder;
    private readonly PluginSharedState _sharedState;

    private ChatClientAgent? _agent;
    private AgentSession? _session;

    public ChatAgentService(UserSettingsService settings, IToolPluginHost pluginHost,
        PatternDetector patternDetector, WorkflowRecorder recorder,
        PluginSharedState sharedState)
    {
        _settings = settings;
        _pluginHost = pluginHost;
        _patternDetector = patternDetector;
        _recorder = recorder;
        _sharedState = sharedState;

        // 设置清空对话回调（供 ChatToolsPlugin.ClearChat 调用）
        _sharedState.OnClearChat += () =>
        {
            // 使用 fire-and-forget 避免在事件调用链中阻塞
            _ = ClearHistoryAsync();
        };
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
            instructions: ChatSystemPrompt.GetPrompt(_pluginHost.GetAggregatedPrompt()),
            name: "桌面助手",
            tools: _pluginHost.GetAllTools()
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
    /// 清空对话历史（创建新 Session）并重置模式检测器。
    /// </summary>
    public async Task ClearHistoryAsync()
    {
        var oldSession = _session;
        if (_agent is not null)
            _session = await _agent.CreateSessionAsync();
        _patternDetector.Reset();
        _recorder.CollectRound();
        _sharedState.PendingSuggestion = null;

        // 延迟释放旧 Session（避免阻塞当前操作）
        if (oldSession is IDisposable d)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000); // 等待旧 session 不再被 MAF 引用
                d.Dispose();
            });
        }
    }

    /// <summary>
    /// 收集本轮工具序列并检测重复模式。
    /// 供 ChatViewModel 在每轮对话结束后调用。
    /// </summary>
    /// <returns>模式建议消息文本（未检测到则返回 null）</returns>
    public string? CollectPatternSuggestion()
    {
        var sequence = _recorder.CollectRound();
        if (sequence.Count == 0) return null;

        var pattern = _patternDetector.AddRound(sequence);
        if (pattern is null) return null;

        _sharedState.PendingSuggestion = pattern;

        return $"检测到重复操作模式：{string.Join(" → ", pattern.ToolSequence)} " +
               $"(已出现 {pattern.OccurrenceCount} 次)。\n" +
               $"要保存为工作流吗？回复 \"保存为 {pattern.SuggestedName}\" 或告诉我你想要的名称。";
    }

}
