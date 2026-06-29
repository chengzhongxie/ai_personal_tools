using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalAssistant.Core.Interfaces;
using PersonalAssistant.Core.Services;
using PersonalAssistant.Features.Chat.Models;
using PersonalAssistant.Features.Chat.Models.Enums;
using PersonalAssistant.Features.Chat.Services;
using PersonalAssistant.Infrastructure.Common.Services;
using Serilog;
using Wpf.Ui.Controls;

namespace PersonalAssistant.Features.Chat.ViewModels;

/// <summary>
/// 聊天界面的 ViewModel，管理消息列表、流式 AI 响应、/clear 命令和对话持久化
/// </summary>
public partial class ChatViewModel : ObservableObject
{
    private const int MaxDisplayMessages = 200;
    private const int MaxInputLength = 50000; // 最大输入长度限制（~12K tokens），防止超大粘贴导致 UI 冻结

    private readonly ChatAgentService _chatAgent;
    private readonly IChatHistoryService _historyService;
    private readonly ModelRoutingService _routing;
    private readonly TokenUsageService _tokenUsage;
    private readonly ConversationSummarizer _summarizer;
    private readonly PluginSharedState _sharedState;

    // 输入历史（环形缓冲区）
    private const int MaxInputHistory = 50;
    private readonly List<string> _inputHistory = new(MaxInputHistory);
    private int _historyIndex = -1;

    // 取消令牌源（用于中止流式响应）
    private CancellationTokenSource? _currentCts;

    /// <summary>聊天消息列表</summary>
    [ObservableProperty]
    private ObservableCollection<ChatMessage> _messages = new();

    /// <summary>用户输入框文本</summary>
    [ObservableProperty]
    private string _inputText = string.Empty;

    /// <summary>是否正在等待 AI 响应</summary>
    [ObservableProperty]
    private bool _isWorking;

    /// <summary>是否显示 InfoBar 错误提示条</summary>
    [ObservableProperty]
    private bool _showInfoBar;

    /// <summary>InfoBar 错误消息文本</summary>
    [ObservableProperty]
    private string _infoBarMessage = string.Empty;

    /// <summary>InfoBar 严重级别</summary>
    [ObservableProperty]
    private InfoBarSeverity _infoBarSeverity = InfoBarSeverity.Error;

    /// <summary>Token 用量显示文本（底部状态栏）</summary>
    [ObservableProperty]
    private string _tokenDisplay = string.Empty;

    /// <summary>是否为离线模式</summary>
    [ObservableProperty]
    private bool _isOffline;

    public ChatViewModel(ChatAgentService chatAgent, IChatHistoryService historyService,
        IDangerousToolPolicy dangerPolicy, ModelRoutingService routing,
        TokenUsageService tokenUsage, ConversationSummarizer summarizer,
        PluginSharedState sharedState)
    {
        Log.Information("[ChatViewModel] 构造开始");
        _chatAgent = chatAgent;
        _historyService = historyService;
        _routing = routing;
        _tokenUsage = tokenUsage;
        _summarizer = summarizer;
        _sharedState = sharedState;

        // 异步网络探测
        _ = UpdateOfflineStatusAsync();

        // 设置高危工具确认回调（MAF 工具循环在后台线程，需封送到 UI 线程弹窗）
        dangerPolicy.DangerConfirmation = (toolName, argsSummary) =>
        {
            var title = toolName switch
            {
                "run_shell" => "执行命令",
                "write_file" => "写入文件",
                "delete_workflow" => "删除工作流",
                "delete_schedule" => "删除定时任务",
                _ => toolName
            };
            var message = $"AI 要执行以下操作：\n\n{title}\n{argsSummary}\n\n是否允许？";
            return Application.Current.Dispatcher.Invoke(() =>
                System.Windows.MessageBox.Show(message, "操作确认",
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning,
                    System.Windows.MessageBoxResult.No)
                == System.Windows.MessageBoxResult.Yes);
        };

        // 从磁盘恢复对话历史
        var saved = _historyService.Load();
        if (saved.Count > 0)
        {
            foreach (var msg in saved)
                Messages.Add(msg);
        }

        // 检查是否有未发送的草稿（崩溃恢复）
        var draftPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PersonalAssistant", "draft.txt");
        try
        {
            if (System.IO.File.Exists(draftPath))
            {
                var draft = System.IO.File.ReadAllText(draftPath);
                if (!string.IsNullOrWhiteSpace(draft))
                {
                    InputText = draft;
                    Messages.Add(new ChatMessage
                    {
                        Role = MessageRole.System,
                        Content = "[系统] 检测到上次未发送的消息，已恢复到输入框",
                        Timestamp = DateTime.Now
                    });
                }
                System.IO.File.Delete(draftPath);
            }
        }
        catch (Exception ex) { Log.Warning(ex, "[ChatViewModel] 草稿恢复失败"); }

        Log.Information("[ChatViewModel] 构造完成");
    }

    /// <summary>更新离线状态（异步网络探测）</summary>
    private async Task UpdateOfflineStatusAsync()
    {
        await _chatAgent.ProbeNetworkAsync();
        IsOffline = _chatAgent.IsOffline;
    }

    /// <summary>
    /// 发送用户消息到 AI 并流式更新回复。
    /// /clear 命令本地拦截处理（零 token）。
    /// 首次发送时携带磁盘历史以恢复 AI 上下文。
    /// </summary>
    [RelayCommand]
    private async Task SendAsync()
    {
        var text = InputText?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        // 输入长度限制：超长截断并提示
        if (text.Length > MaxInputLength)
        {
            text = text[..MaxInputLength];
            Messages.Add(new ChatMessage
            {
                Role = MessageRole.System,
                Content = $"[系统] 输入过长，已自动截断至 {MaxInputLength} 字符",
                Timestamp = DateTime.Now
            });
        }

        // 记录输入历史
        if (_inputHistory.Count == 0 || _inputHistory[^1] != text)
        {
            if (_inputHistory.Count >= MaxInputHistory)
                _inputHistory.RemoveAt(0);
            _inputHistory.Add(text);
        }
        _historyIndex = _inputHistory.Count;

        // 崩溃恢复：先写入草稿文件，成功后再删除
        var draftFilePath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PersonalAssistant", "draft.txt");
        try { System.IO.File.WriteAllText(draftFilePath, text); } catch { }

        // 创建取消令牌
        _currentCts?.Cancel();
        _currentCts?.Dispose();
        _currentCts = new CancellationTokenSource();
        var ct = _currentCts.Token;

        InputText = string.Empty;

        // /clear — 本地处理，零 token
        if (text.Equals("/clear", StringComparison.OrdinalIgnoreCase))
        {
            _currentCts.Dispose();
            _currentCts = null;
            await _chatAgent.ClearHistoryAsync();
            Messages.Clear();
            ShowInfoBar = false;
            _historyService.Save(Messages);
            return;
        }

        // Add user message
        Messages.Add(new ChatMessage
        {
            Role = MessageRole.User,
            Content = text,
            Timestamp = DateTime.Now
        });

        IsWorking = true;
        ShowInfoBar = false;

        // 每次发送前重新探测网络状态（而非依赖缓存值）
        await _chatAgent.ProbeNetworkAsync();
        IsOffline = _chatAgent.IsOffline;

        var assistantMsg = new ChatMessage
        {
            Role = MessageRole.Assistant,
            Content = "",
            Timestamp = DateTime.Now
        };
        Messages.Add(assistantMsg);

        // ═══ 离线模式：强制走本地模型 ═══
        if (IsOffline)
        {
            try
            {
                var (localResponse, _) = await _routing.TryLocalAsync(text);
                assistantMsg.Content = localResponse;
                IsWorking = false;
                _tokenUsage.RecordUsage(text, localResponse, false);
                TokenDisplay = _tokenUsage.GetDisplayText();
                TrimDisplay();
                _historyService.Save(Messages);
                _currentCts.Dispose();
                _currentCts = null;
                return;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[ChatViewModel] 离线本地推理失败");
                assistantMsg.Content = "[离线模式] 本地模型暂不可用，请稍后重试。";
                IsWorking = false;
                _currentCts.Dispose();
                _currentCts = null;
                return;
            }
        }

        // ═══ 自动模型路由：语义意图分类 → 简单对话走本地（零 token） ═══
        if (_routing.ShouldTryLocal(text))
        {
            var intent = await _routing.ClassifyIntentAsync(text);
            Log.Debug("[ChatViewModel] 意图分类: {Intent} | {Msg}",
                intent, text.Length > 80 ? text[..80] + "..." : text);

            if (ModelRoutingService.IsLocalIntent(intent))
            {
                var (localResponse, isAdequate) = await _routing.TryLocalAsync(text);
                if (isAdequate)
                {
                    assistantMsg.Content = localResponse;
                    IsWorking = false;
                    _tokenUsage.RecordUsage(text, localResponse, false);
                    TokenDisplay = _tokenUsage.GetDisplayText();
                    TrimDisplay();
                    _historyService.Save(Messages);
                    _currentCts.Dispose();
                    _currentCts = null;
                    return;
                }
            }
        }

        // ═══ 需工具 / 本地不合格 → 远程模型 ═══
        _sharedState.CurrentRoundToolCalls.Clear();
        try
        {
            var fullContent = "";
            await foreach (var token in _chatAgent.SendMessageStreaming(text, ct))
            {
                fullContent += token;
                assistantMsg.Content = fullContent;
            }

            // 如果回复为空（纯工具调用场景），简要说明
            if (string.IsNullOrWhiteSpace(fullContent))
            {
                assistantMsg.Content = "[工具调用完成]";
            }

            // 记录本轮工具调用到消息上（供 UI 展示）
            foreach (var (toolName, result) in _sharedState.CurrentRoundToolCalls)
                assistantMsg.ToolCalls.Add($"{toolName}: {result}");

            // 记录远程 API 用量
            _tokenUsage.RecordUsage(text, fullContent, true);
        }
        catch (OperationCanceledException)
        {
            assistantMsg.Content = string.IsNullOrWhiteSpace(assistantMsg.Content)
                ? "[已取消]" : assistantMsg.Content;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SendAsync 失败");
            var friendlyMsg = MapExceptionToMessage(ex);
            assistantMsg.Content = friendlyMsg;
            assistantMsg.IsError = true;

            InfoBarMessage = friendlyMsg;
            InfoBarSeverity = InfoBarSeverity.Error;
            ShowInfoBar = true;
        }
        finally
        {
            var cts = Interlocked.Exchange(ref _currentCts, null);
            cts?.Dispose();
            IsWorking = false;
            TokenDisplay = _tokenUsage.GetDisplayText();
            TrimDisplay();

            // 持久化到磁盘
            _historyService.Save(Messages);

            // 成功完成：删除草稿文件
            try
            {
                if (System.IO.File.Exists(draftFilePath))
                    System.IO.File.Delete(draftFilePath);
            }
            catch (Exception ex) { Log.Debug(ex, "[ChatViewModel] 草稿清理失败"); }

            // 递增摘要计数器并检查是否需要触发摘要
            _chatAgent.IncrementSummarizerRound();

            // 检测重复工具调用模式（由 ChatAgentService 内部的 PatternDetector 处理）
            var suggestion = _chatAgent.CollectPatternSuggestion();
            if (suggestion is not null)
            {
                Messages.Add(new ChatMessage
                {
                    Role = MessageRole.System,
                    Content = suggestion,
                    Timestamp = DateTime.Now
                });
            }

            // 触发对话摘要（本地模型，异步不阻塞）
            if (_chatAgent.ShouldSummarize)
            {
                _ = SummarizeAndPruneAsync();
            }
        }

        // 异步生成摘要并修剪旧消息（fire-and-forget）
        async Task SummarizeAndPruneAsync()
        {
            try
            {
                // 如果新一轮对话已开始，跳过本次摘要（避免与 SendAsync 竞态修改 Messages）
                if (IsWorking)
                    return;

                var summary = await _summarizer.SummarizeAsync(Messages);
                // 摘要生成期间可能已开始新一轮对话，再次检查
                if (summary is not null && !IsWorking)
                {
                    // 从显示列表中移除摘要过的旧消息（保留系统消息和最近 10 轮）
                    var keepCount = 20; // 10 rounds * 2 (user+assistant)
                    var toRemove = Messages
                        .Where(m => m.Role is MessageRole.User or MessageRole.Assistant)
                        .SkipLast(keepCount)
                        .ToList();

                    foreach (var msg in toRemove)
                        Messages.Remove(msg);

                    // 注入摘要提示
                    Messages.Insert(0, new ChatMessage
                    {
                        Role = MessageRole.System,
                        Content = $"[对话摘要] {summary}",
                        Timestamp = DateTime.Now
                    });

                    _historyService.Save(Messages);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[ChatViewModel] 摘要生成失败");
            }
        }
    }

    /// <summary>
    /// 清空消息列表和对话历史（绑定到 UI 按钮）
    /// </summary>
    [RelayCommand]
    private async Task Clear()
    {
        await _chatAgent.ClearHistoryAsync();
        Messages.Clear();
        ShowInfoBar = false;
        _historyService.Save(Messages);
    }

    /// <summary>
    /// 取消当前正在进行的 AI 流式响应
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        // 仅发送取消信号，不 Dispose（由 SendAsync finally 统一释放，避免 ObjectDisposedException）
        _currentCts?.Cancel();
    }

    /// <summary>
    /// 向上/向下箭头导航输入历史。
    /// 返回应填入输入框的历史文本，null 表示无历史。
    /// </summary>
    /// <param name="direction">-1=上一条, 1=下一条</param>
    public string? NavigateInputHistory(int direction)
    {
        if (_inputHistory.Count == 0)
            return null;

        _historyIndex += direction;

        // 到顶 → 回到第一条
        if (_historyIndex < 0)
        {
            _historyIndex = -1;
            return "";  // 返回空字符串表示清空输入框
        }

        // 超出最新 → 清空
        if (_historyIndex >= _inputHistory.Count)
        {
            _historyIndex = _inputHistory.Count;
            return "";
        }

        return _inputHistory[_historyIndex];
    }

    /// <summary>将异常映射为用户友好的中文提示</summary>
    private static string MapExceptionToMessage(Exception ex)
    {
        var msg = ex.Message;
        return ex switch
        {
            HttpRequestException => "网络连接失败，请检查网络后重试",
            TimeoutException => "请求超时，服务器响应过慢，请稍后重试",
            TaskCanceledException => "请求超时，请稍后重试",
            OperationCanceledException => "操作已取消",
            _ when msg.Contains("401") || msg.Contains("Unauthorized") || msg.Contains("unauthorized")
                => "API 密钥无效，请在设置中检查 API Key 是否正确",
            _ when msg.Contains("429") || msg.Contains("rate")
                => "请求过于频繁，请稍等片刻再试",
            _ when msg.Contains("503") || msg.Contains("502")
                => "AI 服务暂时不可用，请稍后重试",
            _ when msg.Contains("402") || msg.Contains("quota") || msg.Contains("insufficient")
                => "API 额度不足，请检查账户余额",
            _ when msg.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                => "请求超时，请稍后重试",
            _ => $"出错了: {msg}"
        };
    }

    private void TrimDisplay()
    {
        while (Messages.Count > MaxDisplayMessages)
            Messages.RemoveAt(0);
    }
}
