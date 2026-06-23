using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using PersonalAssistant.Features.Chat.ViewModels;

namespace PersonalAssistant.Features.Chat.Views;

/// <summary>
/// 聊天界面视图，包含消息气泡列表、输入框和发送按钮
/// </summary>
public partial class ChatView : UserControl
{
    /// <summary>绑定的 ViewModel</summary>
    public ChatViewModel ViewModel { get; }

    /// <summary>无参构造函数：从 DI 容器解析 ViewModel（供 XAML 嵌入使用）</summary>
    public ChatView() : this(App.Services.GetRequiredService<ChatViewModel>()) { }

    /// <summary>DI 构造函数</summary>
    /// <param name="viewModel">聊天 ViewModel</param>
    public ChatView(ChatViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
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
}
