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

    public ChatViewModel(ChatAgentService chatAgent, IChatHistoryService historyService,
        IDangerousToolPolicy dangerPolicy, ModelRoutingService routing)
    {
        _chatAgent = chatAgent;
        _historyService = historyService;
        _routing = routing;

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

        var assistantMsg = new ChatMessage
        {
            Role = MessageRole.Assistant,
            Content = "",
            Timestamp = DateTime.Now
        };
        Messages.Add(assistantMsg);

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
            await foreach (var token in _chatAgent.SendMessageStreaming(text))
            {
                fullContent += token;
                assistantMsg.Content = fullContent;
            }

            // 如果回复为空（纯工具调用场景），填充占位
            if (string.IsNullOrWhiteSpace(fullContent))
            {
                assistantMsg.Content = "[工具调用完成]";
            }
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
            IsWorking = false;
            TrimDisplay();

            // 持久化到磁盘
            _historyService.Save(Messages);

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

    private void TrimDisplay()
    {
        while (Messages.Count > MaxDisplayMessages)
            Messages.RemoveAt(0);
    }
}
