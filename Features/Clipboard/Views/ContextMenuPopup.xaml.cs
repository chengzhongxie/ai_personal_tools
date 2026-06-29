using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using PersonalAssistant.Features.Clipboard.Models;
using PersonalAssistant.Features.Clipboard.Services;
using PersonalAssistant.Features.Widgets;
using PersonalAssistant.Features.Widgets.Services;

namespace PersonalAssistant.Features.Clipboard.Views;

/// <summary>
/// 智能剪贴板上下文菜单弹窗：根据剪贴板内容类型显示快捷操作按钮。
/// 零 token 消耗 — 本地执行的操作用 Process.Start，AI 操作用预填文本。
/// 资源成本：仅在显示时消耗（窗口渲染），隐藏时零开销。
/// </summary>
public partial class ContextMenuPopup : Window
{
    private readonly IServiceProvider _serviceProvider;

    public ContextMenuPopup(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        InitializeComponent();
    }

    /// <summary>
    /// 展示上下文菜单：根据剪贴板内容类型生成操作按钮，定位到目标窗口左侧。
    /// </summary>
    public void ShowFor(Window target, ClipboardContentType type, string content)
    {
        var (icon, label) = GetTypeDisplay(type);
        TypeIcon.Text = icon;
        TypeLabel.Text = label;

        // 截断预览文本
        var preview = content.Length > 80 ? content[..80] + "..." : content;
        ContentPreview.Text = preview;

        // 生成操作按钮
        ActionButtons.Children.Clear();
        foreach (var suggestion in GenerateSuggestions(type, content))
        {
            var btn = CreateActionButton(suggestion);
            ActionButtons.Children.Add(btn);
        }

        PositionNear(target);
        Show();
        Activate();
    }

    private static (string icon, string label) GetTypeDisplay(ClipboardContentType type) => type switch
    {
        ClipboardContentType.Url => ("\U0001f517", "检测到链接"),
        ClipboardContentType.Path => ("\U0001f4c1", "检测到路径"),
        ClipboardContentType.Code => ("\U0001f4bb", "检测到代码"),
        ClipboardContentType.Number => ("\U0001f522", "检测到数字"),
        ClipboardContentType.Text => ("\U0001f4c4", "检测到文本"),
        _ => ("\U0001f4cb", "剪贴板内容")
    };

    private List<ClipboardSuggestion> GenerateSuggestions(ClipboardContentType type, string content)
    {
        var suggestions = new List<ClipboardSuggestion>();
        var trimmed = content.Trim();

        switch (type)
        {
            case ClipboardContentType.Url:
                suggestions.Add(new ClipboardSuggestion
                {
                    Label = "在浏览器打开",
                    ExecuteDirectly = true,
                    DirectAction = () =>
                    {
                        try { Process.Start(new ProcessStartInfo(trimmed) { UseShellExecute = true }); }
                        catch (Exception ex) { Serilog.Log.Warning(ex, "[ContextMenuPopup] 打开URL失败"); }
                    }
                });
                suggestions.Add(new ClipboardSuggestion
                {
                    Label = "总结网页",
                    ActionText = $"请总结以下网页内容：\n{trimmed}"
                });
                suggestions.Add(new ClipboardSuggestion
                {
                    Label = "翻译网页",
                    ActionText = $"请将以下网页内容翻译为中文：\n{trimmed}"
                });
                break;

            case ClipboardContentType.Code:
                suggestions.Add(new ClipboardSuggestion
                {
                    Label = "解释代码",
                    ActionText = $"请解释以下代码：\n```\n{trimmed}\n```"
                });
                suggestions.Add(new ClipboardSuggestion
                {
                    Label = "优化代码",
                    ActionText = $"请优化以下代码：\n```\n{trimmed}\n```"
                });
                suggestions.Add(new ClipboardSuggestion
                {
                    Label = "查找错误",
                    ActionText = $"请检查以下代码是否有错误：\n```\n{trimmed}\n```"
                });
                break;

            case ClipboardContentType.Path:
                suggestions.Add(new ClipboardSuggestion
                {
                    Label = "打开文件",
                    ExecuteDirectly = true,
                    DirectAction = () =>
                    {
                        try { Process.Start(new ProcessStartInfo(trimmed) { UseShellExecute = true }); }
                        catch (Exception ex) { Serilog.Log.Warning(ex, "[ContextMenuPopup] 打开文件失败"); }
                    }
                });
                suggestions.Add(new ClipboardSuggestion
                {
                    Label = "打开所在文件夹",
                    ExecuteDirectly = true,
                    DirectAction = () =>
                    {
                        try { Process.Start("explorer.exe", $"/select,\"{trimmed}\""); }
                        catch (Exception ex) { Serilog.Log.Warning(ex, "[ContextMenuPopup] 打开文件夹失败"); }
                    }
                });
                suggestions.Add(new ClipboardSuggestion
                {
                    Label = "读取文件内容",
                    ActionText = $"请读取并总结此文件的内容：{trimmed}"
                });
                break;

            case ClipboardContentType.Number:
                suggestions.Add(new ClipboardSuggestion
                {
                    Label = "大写转换",
                    ActionText = $"请将数字 {trimmed} 转换为中文大写金额"
                });
                suggestions.Add(new ClipboardSuggestion
                {
                    Label = "汇率换算",
                    ActionText = $"请将 {trimmed} 换算为人民币（如需要请说明币种）"
                });
                break;

            case ClipboardContentType.Text:
                var textPreview = trimmed.Length > 2000 ? trimmed[..2000] : trimmed;
                suggestions.Add(new ClipboardSuggestion
                {
                    Label = "总结内容",
                    ActionText = $"请总结以下内容：\n{textPreview}"
                });
                suggestions.Add(new ClipboardSuggestion
                {
                    Label = "翻译为英文",
                    ActionText = $"请将以下内容翻译为英文：\n{textPreview}"
                });
                suggestions.Add(new ClipboardSuggestion
                {
                    Label = "搜索相关内容",
                    ActionText = $"请搜索以下内容的相关信息：\n{textPreview}"
                });
                break;

            default:
                var unknownPreview = trimmed.Length > 2000 ? trimmed[..2000] : trimmed;
                suggestions.Add(new ClipboardSuggestion
                {
                    Label = "分析内容",
                    ActionText = $"请分析以下内容：\n{unknownPreview}"
                });
                break;
        }

        return suggestions;
    }

    private Button CreateActionButton(ClipboardSuggestion suggestion)
    {
        var btn = new Button
        {
            Content = suggestion.Label,
            FontSize = 12,
            Margin = new Thickness(0, 2, 0, 2),
            Padding = new Thickness(10, 6, 10, 6),
            Cursor = Cursors.Hand,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };

        // 按钮样式：DynamicResource 深色/浅色适配
        var style = new Style(typeof(Button));
        var bgSetter = new Setter(BackgroundProperty, new DynamicResourceExtension("Brush.BackgroundSecondary"));
        var fgSetter = new Setter(ForegroundProperty, new DynamicResourceExtension("Brush.ForegroundPrimary"));
        style.Setters.Add(bgSetter);
        style.Setters.Add(fgSetter);

        // Hover 触发器
        var hoverTrigger = new Trigger
        {
            Property = IsMouseOverProperty,
            Value = true
        };
        hoverTrigger.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension("Brush.BorderSecondary")));
        style.Triggers.Add(hoverTrigger);

        // 模板：圆角边框
        var template = new ControlTemplate(typeof(Button));
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.Name = "Border";
        borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
        contentPresenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Left);
        contentPresenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        borderFactory.AppendChild(contentPresenter);
        template.VisualTree = borderFactory;
        style.Setters.Add(new Setter(TemplateProperty, template));

        btn.Style = style;

        btn.Click += (_, _) =>
        {
            if (suggestion.ExecuteDirectly)
            {
                suggestion.DirectAction?.Invoke();
            }
            else
            {
                FillMainWindowInput(suggestion.ActionText ?? suggestion.Label);
            }
            Close();
        };

        return btn;
    }

    private void FillMainWindowInput(string text)
    {
        try
        {
            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            Application.Current.Dispatcher.Invoke(() =>
            {
                mainWindow.ShowWindow();
                mainWindow.ChatViewControl.ViewModel.InputText = text;
                mainWindow.ChatViewControl.FocusInput();
            });
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "[ContextMenuPopup] 填充输入框失败");
        }
    }

    /// <summary>
    /// 定位到目标窗口左侧（复用 WidgetPanel 的模式）
    /// </summary>
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

    /// <summary>失去焦点时自动关闭</summary>
    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        Close();
    }

    /// <summary>Escape 键关闭</summary>
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }

    /// <summary>桌面小组件按钮：回退到 WidgetPanel</summary>
    private void WidgetsButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
        try
        {
            var widgetPanel = _serviceProvider.GetRequiredService<WidgetPanel>();
            widgetPanel.PositionNear(_serviceProvider.GetRequiredService<Features.Mascot.MascotWindow>());
            widgetPanel.Show();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "[ContextMenuPopup] WidgetPanel 打开失败");
        }
    }
}
