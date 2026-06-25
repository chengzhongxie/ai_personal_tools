using System.Windows;
using System.Windows.Controls;

namespace PersonalAssistant.Features.Widgets;

public partial class WidgetCard : UserControl
{
    public WidgetCard()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => TitleText.Text;
        set => TitleText.Text = value;
    }

    public object? CardContent
    {
        get => ContentHost.Content;
        set => ContentHost.Content = value;
    }
}
