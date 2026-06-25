using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalAssistant.Core.Interfaces;
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

    private readonly ChatAgentService _chatAgent;
    private readonly IChatHistoryService _historyService;
    private readonly ModelRoutingService _routing;
    private readonly TokenUsageService _tokenUsage;
    private readonly ConversationSummarizer _summarizer;

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
        TokenUsageService tokenUsage, ConversationSummarizer summarizer)
    {
        Log.Information("[ChatViewModel] 构造开始");
        _chatAgent = chatAgent;
        _historyService = historyService;
        _routing = routing;
        _tokenUsage = tokenUsage;
        _summarizer = summarizer;

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

        // 记录输入历史
        if (_inputHistory.Count == 0 || _inputHistory[^1] != text)
        {
            if (_inputHistory.Count >= MaxInputHistory)
                _inputHistory.RemoveAt(0);
            _inputHistory.Add(text);
        }
        _historyIndex = _inputHistory.Count;

        // 创建取消令牌
        _currentCts?.Cancel();
        _currentCts?.Dispose();
        _currentCts = new CancellationTokenSource();
        var ct = _currentCts.Token;

        InputText = string.Empty;

        // /clear — 本地处理，零 token
        if (text.Equals("/clear", StringComparison.OrdinalIgnoreCase))
        {
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

        // 刷新离线状态
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
                return;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[ChatViewModel] 离线本地推理失败");
                assistantMsg.Content = "[离线模式] 本地模型暂不可用，请稍后重试。";
                IsWorking = false;
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
                    return;
                }
            }
        }

        // ═══ 需工具 / 本地不合格 → 远程模型 ═══
        try
        {
            var fullContent = "";
            await foreach (var token in _chatAgent.SendMessageStreaming(text, ct))
            {
                fullContent += token;
                assistantMsg.Content = fullContent;
            }

            // 如果回复为空（纯工具调用场景），填充占位
            if (string.IsNullOrWhiteSpace(fullContent))
            {
                assistantMsg.Content = "[工具调用完成]";
            }

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
            assistantMsg.Content = $"未知错误: {ex.Message}";
            assistantMsg.IsError = true;

            InfoBarMessage = ex.Message;
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
                var summary = await _summarizer.SummarizeAsync(Messages);
                if (summary is not null)
                {
                    // 从显示列表中移除摘要过的旧消息（保留系统消息和最近 10 轮）
                    var keepCount = 20; // 10 rounds * 2 (user+assistant)
                    var toRemove = Messages
                        .Where(m => m.Role is MessageRole.User or MessageRole.Assistant)
                        .TakeLast(Messages.Count - keepCount)
                        .TakeWhile(_ => true)
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
        // 捕获当前引用避免与 SendAsync finally 竞争
        var cts = Interlocked.Exchange(ref _currentCts, null);
        cts?.Cancel();
        cts?.Dispose();
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

    private void TrimDisplay()
    {
        while (Messages.Count > MaxDisplayMessages)
            Messages.RemoveAt(0);
    }
}
