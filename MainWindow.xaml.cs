using Wpf.Ui.Controls;

namespace PersonalAssistant;

/// <summary>
/// 应用程序主窗口，基于 WPF-UI FluentWindow，内嵌 ChatView
/// </summary>
public partial class MainWindow : FluentWindow
{
    /// <summary>无参构造函数（DI 容器通过此构造函数创建实例）</summary>
    public MainWindow()
    {
        DataContext = this;
        InitializeComponent();
    }
}
