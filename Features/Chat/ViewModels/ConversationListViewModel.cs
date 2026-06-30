using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalAssistant.Features.Chat.Models;
using PersonalAssistant.Features.Chat.Services;
using Serilog;

namespace PersonalAssistant.Features.Chat.ViewModels;

/// <summary>
/// 对话列表 ViewModel：管理多对话的创建、重命名、删除、切换和搜索。
/// </summary>
public partial class ConversationListViewModel : ObservableObject
{
    private readonly ConversationStorageService _storage;

    public ConversationListViewModel(ConversationStorageService storage)
    {
        _storage = storage;
        try
        {
            _storage.Initialize();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[ConversationList] 对话存储初始化失败，将使用空列表");
        }

        var index = _storage.LoadIndex();
        foreach (var conv in index.OrderByDescending(c => c.UpdatedAt))
            Conversations.Add(conv);

        if (_storage.ActiveConversationId is not null)
            ActiveConversation = Conversations.FirstOrDefault(c => c.Id == _storage.ActiveConversationId);

        ActiveConversation ??= Conversations.FirstOrDefault();
        if (ActiveConversation is not null)
        {
            _storage.ActiveConversationId = ActiveConversation.Id;
            ActiveConversation.IsActive = true;
        }
    }

    /// <summary>对话列表</summary>
    public ObservableCollection<ConversationInfo> Conversations { get; } = new();

    /// <summary>搜索结果列表（有搜索词时替代对话列表展示）</summary>
    [ObservableProperty]
    private ObservableCollection<ConversationSearchResult> _searchResults = new();

    /// <summary>搜索关键词</summary>
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    /// <summary>是否正在搜索中</summary>
    [ObservableProperty]
    private bool _isSearching;

    /// <summary>当前活跃对话</summary>
    [ObservableProperty]
    private ConversationInfo? _activeConversation;

    /// <summary>对话切换事件（供 ChatViewModel 订阅）</summary>
    public event Func<ConversationInfo, Task>? ConversationSwitched;

    // 搜索防抖定时器（不自动启动，按需创建）
    private System.Windows.Threading.DispatcherTimer? _searchTimer;

    partial void OnSearchQueryChanged(string value)
    {
        if (_searchTimer is null)
        {
            _searchTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _searchTimer.Tick += (_, _) =>
            {
                _searchTimer.Stop();
                _ = ExecuteSearchAsync(SearchQuery);
            };
        }
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private async Task ExecuteSearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            IsSearching = false;
            SearchResults.Clear();
            return;
        }

        try
        {
            var results = await Task.Run(() => _storage.SearchAllConversations(query));
            SearchResults.Clear();
            foreach (var r in results)
                SearchResults.Add(r);
            IsSearching = results.Count > 0;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[ConversationList] 搜索失败");
        }
    }

    [RelayCommand]
    private async Task SwitchConversation(ConversationInfo? conversation)
    {
        if (conversation is null || conversation.Id == ActiveConversation?.Id)
            return;

        var previous = ActiveConversation;
        ActiveConversation = conversation;
        if (previous is not null) previous.IsActive = false;
        conversation.IsActive = true;
        _storage.ActiveConversationId = conversation.Id;

        if (ConversationSwitched is not null)
        {
            try { await ConversationSwitched.Invoke(conversation); }
            catch (Exception ex)
            {
                Log.Warning(ex, "[ConversationList] 切换对话回调失败");
                ActiveConversation = previous;
                if (previous is not null) previous.IsActive = true;
                conversation.IsActive = false;
                _storage.ActiveConversationId = previous?.Id;
            }
        }
    }

    /// <summary>从搜索结果跳转到对应对话</summary>
    [RelayCommand]
    private async Task NavigateToSearchResult(ConversationSearchResult? result)
    {
        if (result is null) return;

        var conv = Conversations.FirstOrDefault(c => c.Id == result.ConversationId);
        if (conv is not null)
        {
            SearchQuery = string.Empty;
            await SwitchConversation(conv);
        }
    }

    [RelayCommand]
    private void NewConversation()
    {
        SearchQuery = string.Empty;
        var conv = _storage.CreateConversation("新对话");
        if (ActiveConversation is not null) ActiveConversation.IsActive = false;
        conv.IsActive = true;
        Conversations.Insert(0, conv);
        _ = SwitchConversation(conv);
    }

    [RelayCommand]
    private void RenameConversation(ConversationInfo? conversation)
    {
        if (conversation is null) return;

        var newTitle = ShowRenameDialog(conversation.Title);
        if (!string.IsNullOrWhiteSpace(newTitle) && newTitle != conversation.Title)
        {
            _storage.RenameConversation(conversation.Id, newTitle);
            conversation.Title = newTitle;
            conversation.UpdatedAt = DateTime.Now;

            // 触发 UI 刷新
            var idx = Conversations.IndexOf(conversation);
            if (idx >= 0)
            {
                Conversations.RemoveAt(idx);
                Conversations.Insert(idx, conversation);
            }
        }
    }

    /// <summary>简单的重命名对话框（纯 WPF，不依赖 VisualBasic）</summary>
    private static string? ShowRenameDialog(string currentTitle)
    {
        var dialog = new Window
        {
            Title = "重命名对话",
            Width = 320,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = false
        };

        var grid = new System.Windows.Controls.Grid { Margin = new Thickness(12) };
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

        var label = new System.Windows.Controls.TextBlock
        {
            Text = "请输入新名称:",
            Margin = new Thickness(0, 0, 0, 8),
            FontSize = 12
        };
        System.Windows.Controls.Grid.SetRow(label, 0);
        grid.Children.Add(label);

        var textBox = new System.Windows.Controls.TextBox
        {
            Text = currentTitle,
            Margin = new Thickness(0, 0, 0, 12),
            Padding = new Thickness(6, 4, 6, 4),
            FontSize = 12
        };
        textBox.GotFocus += (_, _) => textBox.SelectAll();
        System.Windows.Controls.Grid.SetRow(textBox, 1);
        grid.Children.Add(textBox);

        var btnPanel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        var cancelBtn = new System.Windows.Controls.Button
        {
            Content = "取消", Width = 70, Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(8, 4, 8, 4)
        };
        cancelBtn.Click += (_, _) => { dialog.DialogResult = false; dialog.Close(); };
        var okBtn = new System.Windows.Controls.Button
        {
            Content = "确定", Width = 70, Padding = new Thickness(8, 4, 8, 4),
            IsDefault = true
        };
        okBtn.Click += (_, _) => { dialog.DialogResult = true; dialog.Close(); };
        btnPanel.Children.Add(cancelBtn);
        btnPanel.Children.Add(okBtn);
        System.Windows.Controls.Grid.SetRow(btnPanel, 2);
        grid.Children.Add(btnPanel);

        dialog.Content = grid;
        textBox.Focus();

        return dialog.ShowDialog() == true ? textBox.Text.Trim() : null;
    }

    [RelayCommand]
    private async Task DeleteConversation(ConversationInfo? conversation)
    {
        if (conversation is null) return;

        var result = MessageBox.Show(
            $"确定删除对话 \"{conversation.Title}\" 吗？此操作不可撤销。",
            "删除对话", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        _storage.DeleteConversation(conversation.Id);
        Conversations.Remove(conversation);

        // 如果删除的是当前活跃对话，切换到第一个
        if (ActiveConversation?.Id == conversation.Id)
        {
            var next = Conversations.FirstOrDefault();
            if (next is not null)
                await SwitchConversation(next);
            else
                NewConversation();
        }
    }

    /// <summary>刷新活跃对话的消息计数</summary>
    public void RefreshActiveMeta(int messageCount)
    {
        if (ActiveConversation is null) return;
        _storage.UpdateConversationMeta(ActiveConversation.Id, messageCount);
        ActiveConversation.MessageCount = messageCount;
        ActiveConversation.UpdatedAt = DateTime.Now;

        // 自动更新标题（取第一条用户消息的前 20 字）
        if (ActiveConversation.Title == "新对话" || ActiveConversation.Title == "默认对话")
        {
            var messages = _storage.LoadMessages(ActiveConversation.Id);
            var firstUser = messages.FirstOrDefault(m => m.Role == Models.Enums.MessageRole.User);
            if (firstUser is not null)
            {
                var title = firstUser.Content.Length > 20
                    ? firstUser.Content[..20] + "..."
                    : firstUser.Content;
                // 移除换行
                title = title.Replace('\n', ' ').Replace('\r', ' ').Trim();
                if (!string.IsNullOrWhiteSpace(title))
                {
                    _storage.RenameConversation(ActiveConversation.Id, title);
                }
            }
        }
    }
}
