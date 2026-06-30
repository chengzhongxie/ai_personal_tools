using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using PersonalAssistant.Features.Chat.Models;
using PersonalAssistant.Features.Chat.ViewModels;
using Serilog;

namespace PersonalAssistant.Features.Chat.Views;

/// <summary>
/// 聊天界面视图，包含消息气泡列表、输入框、侧边栏和发送按钮
/// </summary>
public partial class ChatView : UserControl
{
    /// <summary>绑定的 ViewModel</summary>
    public ChatViewModel ViewModel { get; }

    /// <summary>无参构造函数：从 DI 容器解析 ViewModel（供 XAML 嵌入使用）</summary>
    public ChatView() : this(App.Services.GetRequiredService<ChatViewModel>())
    {
        Log.Information("[ChatView] 无参构造完成");
    }

    /// <summary>DI 构造函数</summary>
    /// <param name="viewModel">聊天 ViewModel</param>
    public ChatView(ChatViewModel viewModel)
    {
        Log.Information("[ChatView] DI构造开始");
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();

        // 自动滚动：新消息添加时滚到底部
        ViewModel.Messages.CollectionChanged += OnMessagesChanged;

        Log.Information("[ChatView] DI构造完成");
    }

    /// <summary>消息列表变化时自动滚到底部，并在消息移除时解绑事件防止内存泄漏</summary>
    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
        {
            // 对新添加的 Assistant 消息订阅 Content 变化（流式输出时持续滚动）
            foreach (var item in e.NewItems)
            {
                if (item is ChatMessage msg && msg.Role == Models.Enums.MessageRole.Assistant)
                    msg.PropertyChanged += OnAssistantContentChanged;
            }
        }
        // 消息移除时解绑 PropertyChanged 事件，防止内存泄漏
        if (e.Action is NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Replace
            or NotifyCollectionChangedAction.Reset
            && e.OldItems is not null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is ChatMessage msg)
                    msg.PropertyChanged -= OnAssistantContentChanged;
            }
        }
        ScrollToBottom();
    }

    /// <summary>AI 回复流式更新时保持滚动到底部</summary>
    private void OnAssistantContentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatMessage.Content))
            ScrollToBottom();
    }

    private void ScrollToBottom()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            MessageScroll.ScrollToEnd();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>聚焦输入框并将光标移至末尾</summary>
    public void FocusInput()
    {
        InputTextBox.Focus();
        InputTextBox.Select(InputTextBox.Text.Length, 0);
    }

    /// <summary>处理输入框按键：Enter 发送，Up/Down 导航输入历史</summary>
    private void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !ViewModel.IsWorking)
        {
            e.Handled = true;
            ViewModel.SendCommand.Execute(null);
        }
        else if (e.Key == Key.Up)
        {
            var text = ViewModel.NavigateInputHistory(-1);
            if (text is not null)
            {
                ViewModel.InputText = text;
                InputTextBox.CaretIndex = InputTextBox.Text.Length;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            var text = ViewModel.NavigateInputHistory(1);
            if (text is not null)
            {
                ViewModel.InputText = text;
                InputTextBox.CaretIndex = InputTextBox.Text.Length;
            }
            e.Handled = true;
        }
    }

    /// <summary>处理 Ctrl+V 图片粘贴</summary>
    private void InputTextBox_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.KeyboardDevice.Modifiers == ModifierKeys.Control && e.Key == Key.V)
        {
            // Check if clipboard contains image
            if (System.Windows.Clipboard.ContainsImage())
            {
                ViewModel.PasteImageCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    // ──── Sidebar interactions ────

    private void ToggleSidebar_Click(object sender, MouseButtonEventArgs e)
    {
        ViewModel.ToggleSidebarCommand.Execute(null);
    }

    private void ConversationItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is ConversationInfo conv)
        {
            ViewModel.ConversationList.SwitchConversationCommand.Execute(conv);
        }
    }

    private void SearchResultItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is ConversationSearchResult result)
        {
            ViewModel.ConversationList.NavigateToSearchResultCommand.Execute(result);
        }
    }

    // ──── Drag & Drop ────

    private bool _isDragOver;

    private void ChatView_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            _isDragOver = true;
            e.Effects = DragDropEffects.Copy;

            // Set a highlight border effect on the main content area
            if (Parent is UIElement parent)
            {
                // Visual feedback via cursor only
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void ChatView_DragOver(object sender, DragEventArgs e)
    {
        if (_isDragOver)
        {
            e.Effects = DragDropEffects.Copy;
        }
        e.Handled = true;
    }

    private void ChatView_DragLeave(object sender, DragEventArgs e)
    {
        _isDragOver = false;
        e.Handled = true;
    }

    private void ChatView_Drop(object sender, DragEventArgs e)
    {
        _isDragOver = false;
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files is { Length: > 0 })
            {
                ViewModel.HandleDroppedFiles(files);
            }
        }
        e.Handled = true;
    }
}
