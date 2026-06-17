using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Controls;

namespace PersonalAssistant;

/// <summary>
/// 应用程序主窗口，基于 WPF-UI FluentWindow，内嵌 ChatView
/// </summary>
public partial class MainWindow : FluentWindow
{
    /// <summary>无参构造函数</summary>
    public MainWindow()
    {
        DataContext = this;
        InitializeComponent();
    }

    /// <summary>DI 构造函数</summary>
    public MainWindow(IServiceProvider serviceProvider) : this()
    {
    }
}
