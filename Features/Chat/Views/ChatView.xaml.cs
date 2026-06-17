using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using PersonalAssistant.Features.Chat.ViewModels;

namespace PersonalAssistant.Features.Chat.Views;

public partial class ChatView : UserControl
{
    public ChatViewModel ViewModel { get; }

    public ChatView() : this(App.Services.GetRequiredService<ChatViewModel>()) { }

    public ChatView(ChatViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    private void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !ViewModel.IsWorking)
        {
            e.Handled = true;
            ViewModel.SendCommand.Execute(null);
        }
    }
}
