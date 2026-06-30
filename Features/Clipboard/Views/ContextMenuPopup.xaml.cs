using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using PersonalAssistant.Features.Clipboard.Models;
using PersonalAssistant.Features.Clipboard.Services;
using PersonalAssistant.Features.Widgets;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PersonalAssistant.Features.Clipboard.Views;

/// <summary>
/// 智能剪贴板上下文菜单弹窗：根据剪贴板内容类型显示快捷操作按钮。
/// 零 token 消耗 — 本地执行的操作用 Process.Start，AI 操作用预填文本。
/// 资源成本：仅在显示时消耗（窗口渲染），隐藏时零开销。
/// </summary>
public partial class ContextMenuPopup : Window
{
    private readonly IServiceProvider _serviceProvider;
    private ClipboardMonitor? _clipboardMonitor;

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
        _clipboardMonitor = _serviceProvider.GetRequiredService<ClipboardMonitor>();

        var (icon, label) = GetTypeDisplay(type, content);
        TypeIcon.Text = icon;
        TypeLabel.Text = label;

        // 截断预览文本
        var preview = content.Length > 80 ? content[..80] + "..." : content;
        ContentPreview.Text = preview;

        // 颜色预览：检测并显示色块
        ShowColorPreview(content);

        // 隐藏上次的结果面板
        ResultPanel.Visibility = Visibility.Collapsed;

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

    private void ShowColorPreview(string content)
    {
        try
        {
            if (ClipboardToolHelper.IsHexColor(content) || ClipboardToolHelper.IsRgbColor(content))
            {
                var (r, g, b) = ClipboardToolHelper.ParseColor(content);
                ColorPreview.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
                ColorPreview.Visibility = Visibility.Visible;
                return;
            }
        }
        catch { }
        ColorPreview.Visibility = Visibility.Collapsed;
    }

    private static (string icon, string label) GetTypeDisplay(ClipboardContentType type, string content)
    {
        // 子类型检测
        if (ClipboardToolHelper.IsHexColor(content) || ClipboardToolHelper.IsRgbColor(content))
            return ("🎨", "检测到颜色值");
        if (ClipboardToolHelper.IsJson(content))
            return ("📋", "检测到 JSON");
        if (ClipboardToolHelper.IsTimestamp(content))
            return ("🕐", "检测到时间戳");
        if (ClipboardToolHelper.IsMathExpression(content))
            return ("🔢", "检测到算式");
        if (ClipboardToolHelper.IsBase64(content))
            return ("🔐", "检测到 Base64");

        return type switch
        {
            ClipboardContentType.Url => ("🔗", "检测到链接"),
            ClipboardContentType.Path => ("📁", "检测到路径"),
            ClipboardContentType.Code => ("💻", "检测到代码"),
            ClipboardContentType.Number => ("🔢", "检测到数字"),
            ClipboardContentType.Text => ("📄", "检测到文本"),
            _ => ("📋", "剪贴板内容")
        };
    }

    private List<ClipboardSuggestion> GenerateSuggestions(ClipboardContentType type, string content)
    {
        var suggestions = new List<ClipboardSuggestion>();
        var trimmed = content.Trim();

        // ── 跨类型检测：子类型优先 ──

        // Base64
        if (ClipboardToolHelper.IsBase64(trimmed))
        {
            suggestions.Add(new ClipboardSuggestion
            {
                Label = "Base64 解码",
                InlineResult = () => ClipboardToolHelper.Base64Decode(trimmed)
            });
            suggestions.Add(new ClipboardSuggestion
            {
                Label = "复制解码结果",
                ExecuteDirectly = true,
                DirectAction = () =>
                {
                    var result = ClipboardToolHelper.Base64Decode(trimmed);
                    _clipboardMonitor!.SuppressNextUpdate();
                    System.Windows.Clipboard.SetText(result);
                }
            });
        }

        // JSON
        if (ClipboardToolHelper.IsJson(trimmed))
        {
            suggestions.Add(new ClipboardSuggestion
            {
                Label = "格式化 JSON",
                InlineResult = () =>
                {
                    try { return ClipboardToolHelper.FormatJson(trimmed); }
                    catch (Exception ex) { return $"格式化失败: {ex.Message}"; }
                }
            });
            suggestions.Add(new ClipboardSuggestion
            {
                Label = "压缩 JSON",
                InlineResult = () =>
                {
                    try { return ClipboardToolHelper.MinifyJson(trimmed); }
                    catch (Exception ex) { return $"压缩失败: {ex.Message}"; }
                }
            });
            suggestions.Add(new ClipboardSuggestion
            {
                Label = "复制格式化结果",
                ExecuteDirectly = true,
                DirectAction = () =>
                {
                    var result = ClipboardToolHelper.FormatJson(trimmed);
                    _clipboardMonitor!.SuppressNextUpdate();
                    System.Windows.Clipboard.SetText(result);
                }
            });
        }

        // 时间戳
        if (ClipboardToolHelper.IsTimestamp(trimmed))
        {
            suggestions.Add(new ClipboardSuggestion
            {
                Label = "转换时间戳",
                InlineResult = () => ClipboardToolHelper.ConvertTimestamp(trimmed)
            });
        }

        // 颜色值
        if (ClipboardToolHelper.IsHexColor(trimmed) || ClipboardToolHelper.IsRgbColor(trimmed))
        {
            suggestions.Add(new ClipboardSuggestion
            {
                Label = "复制 HEX",
                ExecuteDirectly = true,
                DirectAction = () =>
                {
                    var (r, g, b) = ClipboardToolHelper.ParseColor(trimmed);
                    _clipboardMonitor!.SuppressNextUpdate();
                    System.Windows.Clipboard.SetText($"#{r:X2}{g:X2}{b:X2}");
                }
            });
            suggestions.Add(new ClipboardSuggestion
            {
                Label = "复制 RGB",
                ExecuteDirectly = true,
                DirectAction = () =>
                {
                    var (r, g, b) = ClipboardToolHelper.ParseColor(trimmed);
                    _clipboardMonitor!.SuppressNextUpdate();
                    System.Windows.Clipboard.SetText($"rgb({r}, {g}, {b})");
                }
            });
        }

        // 数学表达式
        if (ClipboardToolHelper.IsMathExpression(trimmed))
        {
            suggestions.Add(new ClipboardSuggestion
            {
                Label = "计算结果",
                InlineResult = () => ClipboardToolHelper.EvaluateMath(trimmed)
            });
            suggestions.Add(new ClipboardSuggestion
            {
                Label = "复制计算结果",
                ExecuteDirectly = true,
                DirectAction = () =>
                {
                    var result = ClipboardToolHelper.EvaluateMath(trimmed);
                    _clipboardMonitor!.SuppressNextUpdate();
                    System.Windows.Clipboard.SetText(result);
                }
            });
        }

        // ── 主类型 ──

        switch (type)
        {
            case ClipboardContentType.Url:
                if (!suggestions.Any(s => s.Label == "在浏览器打开"))
                {
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
                }
                suggestions.Add(new ClipboardSuggestion
                {
                    Label = "复制链接地址",
                    ExecuteDirectly = true,
                    DirectAction = () =>
                    {
                        _clipboardMonitor!.SuppressNextUpdate();
                        System.Windows.Clipboard.SetText(trimmed);
                    }
                });
                suggestions.Add(new ClipboardSuggestion
                {
                    Label = "生成二维码",
                    InlineResult = () => GenerateQrCode(trimmed)
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
                // 文本统计（代码也适用）
                suggestions.Add(new ClipboardSuggestion
                {
                    Label = "文本统计",
                    InlineResult = () => ClipboardToolHelper.GetTextStatistics(trimmed)
                });
                break;

            case ClipboardContentType.Path:
                if (File.Exists(trimmed) || Directory.Exists(trimmed))
                {
                    suggestions.Add(new ClipboardSuggestion
                    {
                        Label = "打开",
                        ExecuteDirectly = true,
                        DirectAction = () =>
                        {
                            try { Process.Start(new ProcessStartInfo(trimmed) { UseShellExecute = true }); }
                            catch (Exception ex) { Serilog.Log.Warning(ex, "[ContextMenuPopup] 打开文件失败"); }
                        }
                    });
                    suggestions.Add(new ClipboardSuggestion
                    {
                        Label = "复制完整路径",
                        ExecuteDirectly = true,
                        DirectAction = () => ClipboardToolHelper.CopyFullPath(trimmed, _clipboardMonitor!)
                    });
                    suggestions.Add(new ClipboardSuggestion
                    {
                        Label = "复制文件名(无扩展名)",
                        ExecuteDirectly = true,
                        DirectAction = () => ClipboardToolHelper.CopyFileNameWithoutExtension(trimmed, _clipboardMonitor!)
                    });
                    suggestions.Add(new ClipboardSuggestion
                    {
                        Label = "在终端打开",
                        ExecuteDirectly = true,
                        DirectAction = () => ClipboardToolHelper.OpenInTerminal(trimmed)
                    });
                    suggestions.Add(new ClipboardSuggestion
                    {
                        Label = "打开所在目录",
                        ExecuteDirectly = true,
                        DirectAction = () => ClipboardToolHelper.OpenInExplorer(trimmed)
                    });
                }
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
                    Label = "文本统计",
                    InlineResult = () => ClipboardToolHelper.GetTextStatistics(trimmed)
                });
                suggestions.Add(new ClipboardSuggestion
                {
                    Label = "Base64 编码",
                    InlineResult = () => ClipboardToolHelper.Base64Encode(trimmed)
                });
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

        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension("Brush.BackgroundSecondary")));
        style.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("Brush.ForegroundPrimary")));

        var hoverTrigger = new Trigger { Property = IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension("Brush.BorderSecondary")));
        style.Triggers.Add(hoverTrigger);

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
            if (suggestion.InlineResult is not null)
            {
                // 在弹窗内显示结果
                try
                {
                    var result = suggestion.InlineResult();
                    ResultLabel.Text = suggestion.Label;
                    ResultText.Text = result;
                    ResultPanel.Visibility = Visibility.Visible;
                }
                catch (Exception ex)
                {
                    ResultLabel.Text = "错误";
                    ResultText.Text = ex.Message;
                    ResultPanel.Visibility = Visibility.Visible;
                }
                return; // 不关闭弹窗
            }

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

    /// <summary>生成二维码（使用 Google Charts API，在线使用）</summary>
    private static string GenerateQrCode(string url)
    {
        try
        {
            // 使用 HTTP 客户端获取 QR 码图片
            var qrUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=200x200&data={Uri.EscapeDataString(url)}";
            // 返回提示 + 在浏览器中打开
            Process.Start(new ProcessStartInfo(qrUrl) { UseShellExecute = true });
            return $"二维码已生成并在浏览器中打开\n\nURL: {url}";
        }
        catch (Exception ex)
        {
            return $"二维码生成失败: {ex.Message}\n\nURL: {url}";
        }
    }

    /// <summary>定位到目标窗口左侧</summary>
    public void PositionNear(Window target)
    {
        if (target.WindowState == WindowState.Minimized) return;

        var targetLeft = target.Left;
        var targetTop = target.Top;

        Left = targetLeft - Width - 10;
        Top = targetTop;

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

    /// <summary>快速便签按钮：打开便签输入弹窗</summary>
    private void StickyNoteButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
        try
        {
            var stickyNote = new StickyNoteWindow();
            stickyNote.Owner = _serviceProvider.GetRequiredService<MainWindow>();
            stickyNote.Show();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "[ContextMenuPopup] 便签打开失败");
        }
    }

    /// <summary>OCR 按钮：对剪贴板图片进行本地文字识别（Windows 内置 OCR，零 token）</summary>
    private async void OcrButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!System.Windows.Clipboard.ContainsImage())
            {
                ResultLabel.Text = "OCR 识别";
                ResultText.Text = "剪贴板中没有图片";
                ResultPanel.Visibility = Visibility.Visible;
                return;
            }

            var bitmap = System.Windows.Clipboard.GetImage();
            if (bitmap is null) return;

            // 使用 Windows 内置 OCR 引擎
            var text = await RunClipboardOcrAsync(bitmap);
            if (string.IsNullOrWhiteSpace(text))
            {
                ResultLabel.Text = "OCR 识别";
                ResultText.Text = "未识别到文字";
            }
            else
            {
                ResultLabel.Text = "OCR 识别结果";
                ResultText.Text = text;
                // 也复制到剪贴板
                _clipboardMonitor!.SuppressNextUpdate();
                System.Windows.Clipboard.SetText(text);
            }
            ResultPanel.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            ResultLabel.Text = "OCR 错误";
            ResultText.Text = ex.Message;
            ResultPanel.Visibility = Visibility.Visible;
            Serilog.Log.Warning(ex, "[ContextMenuPopup] OCR 识别失败");
        }
    }

    /// <summary>使用 Windows 内置 OCR 引擎识别剪贴板图片文字（零 token）</summary>
    private static async Task<string> RunClipboardOcrAsync(System.Windows.Media.Imaging.BitmapSource bitmap)
    {
        // 先将剪贴板图片编码为 PNG 并保存到临时文件（复用截图 OCR 管道）
        var tmpPath = Path.Combine(Path.GetTempPath(), $"pa_ocr_{Guid.NewGuid():N}.png");
        try
        {
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var fs = new FileStream(tmpPath, FileMode.Create))
                encoder.Save(fs);

            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine is null) return "未安装 OCR 语言包";

            var file = await StorageFile.GetFileFromPathAsync(tmpPath);
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var softwareBitmap = await decoder.GetSoftwareBitmapAsync();
            var result = await engine.RecognizeAsync(softwareBitmap);
            return result.Text;
        }
        catch (Exception ex)
        {
            return $"OCR 失败: {ex.Message}";
        }
        finally
        {
            try { File.Delete(tmpPath); } catch { }
        }
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
