using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Controls;

namespace PersonalAssistant;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        DataContext = this;
        InitializeComponent();
    }

    public MainWindow(IServiceProvider serviceProvider) : this()
    {
    }
}
