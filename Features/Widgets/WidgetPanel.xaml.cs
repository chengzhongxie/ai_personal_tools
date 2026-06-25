using System.Windows;
using PersonalAssistant.Features.Widgets.Services;

namespace PersonalAssistant.Features.Widgets;

public partial class WidgetPanel : Window
{
    private readonly WidgetConfigService _configService;
    private readonly IServiceProvider _serviceProvider;

    public WidgetPanel(WidgetConfigService configService, IServiceProvider serviceProvider)
    {
        _configService = configService;
        _serviceProvider = serviceProvider;
        InitializeComponent();
        BuildCards();
    }

    public void RefreshCards()
    {
        CardStack.Children.Clear();
        BuildCards();
    }

    private void BuildCards()
    {
        var config = _configService.Config;

        if (config.WeatherEnabled)
        {
            var weatherWidget = new WeatherWidget();
            CardStack.Children.Add(weatherWidget);
        }

        if (config.TodoEnabled)
        {
            var todoWidget = new TodoWidget();
            CardStack.Children.Add(todoWidget);
        }

        if (config.SystemStatusEnabled)
        {
            var statusWidget = new SystemStatusWidget();
            CardStack.Children.Add(statusWidget);
        }
    }

    public void PositionNear(Window target)
    {
        if (target.WindowState == WindowState.Minimized) return;

        var targetLeft = target.Left;
        var targetTop = target.Top;

        Left = targetLeft - Width - 10;
        Top = targetTop;

        // Ensure on-screen
        if (Left < 0) Left = 0;
        if (Top + Height > SystemParameters.PrimaryScreenHeight)
            Top = SystemParameters.PrimaryScreenHeight - Height;
    }
}
