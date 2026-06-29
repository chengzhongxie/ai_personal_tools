using System.ClientModel;
using System.Net.Http;
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
using Serilog;

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
    private readonly ConversationSummarizer _summarizer;

    private ChatClientAgent? _agent;
    private AgentSession? _session;
    private bool _isOffline;
    private bool _networkChecked;

    // 防止 SendMessageStreaming 和 ClearHistoryAsync 并发执行
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    // 网络探测用的 HttpClient（复用，避免频繁创建）
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(3) };

    public ChatAgentService(UserSettingsService settings, IToolPluginHost pluginHost,
        PatternDetector patternDetector, WorkflowRecorder recorder,
        PluginSharedState sharedState, ConversationSummarizer summarizer)
    {
        Log.Information("[ChatAgentService] 构造开始");
        _settings = settings;
        _pluginHost = pluginHost;
        _patternDetector = patternDetector;
        _recorder = recorder;
        _sharedState = sharedState;
        _summarizer = summarizer;

        // 设置清空对话回调（供 ChatToolsPlugin.ClearChat 调用）
        // 使用 async void 等待锁释放后再执行清空，避免与 SendMessageStreaming 并发
        _sharedState.OnClearChat += async () =>
        {
            try
            {
                await _sendLock.WaitAsync();
                await ClearHistoryInternalAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[ChatAgentService] OnClearChat 执行失败");
            }
            finally
            {
                _sendLock.Release();
            }
        };

        // 订阅插件变更事件（热重载后重建 MAF Session）
        if (pluginHost is PluginAggregator aggregator)
        {
            aggregator.PluginsChanged += () =>
            {
                Log.Information("[ChatAgentService] 插件已更新，重建 Agent Session");
                _agent = null; // 强制 EnsureInitializedAsync 重新创建 Agent + Session
                _session = null;
            };
        }
        Log.Information("[ChatAgentService] 构造完成");
    }

    /// <summary>当前是否为离线模式（网络不可达）</summary>
    public bool IsOffline
    {
        get
        {
            if (!_networkChecked)
            {
                _ = ProbeNetworkAsync(); // fire-and-forget 探测
                return false; // 首次默认在线，探测完成后更新
            }
            return _isOffline;
        }
    }

    /// <summary>
    /// 探测网络连通性（异步，3s 超时）。
    /// 在后台静默完成，更新 IsOffline 状态。
    /// </summary>
    public async Task ProbeNetworkAsync()
    {
        try
        {
            var endpoint = _settings.GetChatSettings().Endpoint;
            var response = await SharedHttpClient.SendAsync(
                new HttpRequestMessage(HttpMethod.Head, endpoint),
                HttpCompletionOption.ResponseHeadersRead);
            _isOffline = !response.IsSuccessStatusCode;
        }
        catch
        {
            _isOffline = true;
        }
        _networkChecked = true;
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
    /// 通过 SemaphoreSlim 保证同一时间只有一个请求在执行。
    /// </summary>
    public async IAsyncEnumerable<string> SendMessageStreaming(string message,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await _sendLock.WaitAsync(ct);
        try
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
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// 清空对话历史（创建新 Session）并重置模式检测器和摘要器。
    /// </summary>
    public async Task ClearHistoryAsync()
    {
        await _sendLock.WaitAsync();
        try
        {
            await ClearHistoryInternalAsync();
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// 清空对话历史的内部实现（调用方必须持有 _sendLock）。
    /// </summary>
    private async Task ClearHistoryInternalAsync()
    {
        var oldSession = _session;
        if (_agent is not null)
            _session = await _agent.CreateSessionAsync();
        _patternDetector.Reset();
        _recorder.CollectRound();
        _sharedState.PendingSuggestion = null;
        _summarizer.Reset();

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

    /// <summary>每轮对话后递增摘要计数器</summary>
    public void IncrementSummarizerRound() => _summarizer.IncrementRound();

    /// <summary>是否需要触发摘要</summary>
    public bool ShouldSummarize => _summarizer.ShouldSummarize;

    /// <summary>获取摘要提示词片段（用于注入系统提示词）</summary>
    public string? GetSummaryPromptFragment()
    {
        var summary = _summarizer.LatestSummary;
        if (summary is null) return null;
        return $"Previous conversation summary (use this for context): {summary}";
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
