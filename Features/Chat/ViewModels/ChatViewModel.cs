using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalAssistant.Features.Chat.Models;
using PersonalAssistant.Features.Chat.Models.Enums;
using PersonalAssistant.Features.Chat.Services;
using Serilog;
using Wpf.Ui.Controls;

namespace PersonalAssistant.Features.Chat.ViewModels;

public partial class ChatViewModel : ObservableObject
{
    private readonly IChatService _chatService;

    [ObservableProperty]
    private ObservableCollection<ChatMessage> _messages = new();

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private bool _isWorking;

    [ObservableProperty]
    private bool _showInfoBar;

    [ObservableProperty]
    private string _infoBarMessage = string.Empty;

    [ObservableProperty]
    private InfoBarSeverity _infoBarSeverity = InfoBarSeverity.Error;

    public ChatViewModel(IChatService chatService)
    {
        _chatService = chatService;
    }

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
                    ? "\n\n[Tools used: " + string.Join(", ", response.ToolCalls) + "]"
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
            Log.Error(ex, "SendAsync failed");
            Messages.Add(new ChatMessage
            {
                Role = MessageRole.Assistant,
                Content = $"Unexpected error: {ex.Message}",
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
        }
    }

    [RelayCommand]
    private void Clear()
    {
        _chatService.ClearHistory();
        Messages.Clear();
        ShowInfoBar = false;
    }
}
