using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalAssistant.Features.Chat.Models;
using PersonalAssistant.Features.Chat.Models.Enums;
using PersonalAssistant.Features.Chat.Services;
using Serilog;
using Wpf.Ui.Controls;

namespace PersonalAssistant.Features.Chat.ViewModels;

/// <summary>
/// 聊天界面的 ViewModel，管理消息列表、用户输入、发送/清空命令和错误提示
/// </summary>
public partial class ChatViewModel : ObservableObject
{
    private const int MaxDisplayMessages = 200;

    private readonly IChatService _chatService;

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

    /// <summary>
    /// 初始化 ViewModel，注入聊天服务
    /// </summary>
    public ChatViewModel(IChatService chatService)
    {
        _chatService = chatService;
    }

    /// <summary>
    /// 发送用户消息到 AI 并处理回复（含工具调用）
    /// </summary>
    [RelayCommand]
    private async Task SendAsync()
    {
        var text = InputText?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        InputText = string.Empty;

        // Handle /clear command
        if (text.Equals("/clear", StringComparison.OrdinalIgnoreCase))
        {
            Clear();
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

        try
        {
            var response = await _chatService.SendMessageAsync(text);

            if (response.IsError)
            {
                Messages.Add(new ChatMessage
                {
                    Role = MessageRole.Assistant,
                    Content = response.ErrorMessage,
                    Timestamp = DateTime.Now,
                    IsError = true
                });

                InfoBarMessage = response.ErrorMessage;
                InfoBarSeverity = InfoBarSeverity.Error;
                ShowInfoBar = true;
            }
            else
            {
                var toolCallText = response.ToolCalls.Count > 0
                    ? "\n\n[使用工具: " + string.Join(", ", response.ToolCalls) + "]"
                    : "";

                Messages.Add(new ChatMessage
                {
                    Role = MessageRole.Assistant,
                    Content = response.Content + toolCallText,
                    Timestamp = DateTime.Now,
                    ToolCalls = response.ToolCalls
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SendAsync 失败");
            Messages.Add(new ChatMessage
            {
                Role = MessageRole.Assistant,
                Content = $"未知错误: {ex.Message}",
                Timestamp = DateTime.Now,
                IsError = true
            });

            InfoBarMessage = ex.Message;
            InfoBarSeverity = InfoBarSeverity.Error;
            ShowInfoBar = true;
        }
        finally
        {
            IsWorking = false;
            TrimDisplay();
        }
    }

    private void TrimDisplay()
    {
        while (Messages.Count > MaxDisplayMessages)
            Messages.RemoveAt(0);
    }

    /// <summary>
    /// 清空消息列表和对话历史
    /// </summary>
    [RelayCommand]
    private void Clear()
    {
        _chatService.ClearHistory();
        Messages.Clear();
        ShowInfoBar = false;
    }
}
