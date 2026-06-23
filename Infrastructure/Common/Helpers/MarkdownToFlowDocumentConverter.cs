using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using Markdig;
using Serilog;

namespace PersonalAssistant.Infrastructure.Common.Helpers;

/// <summary>
/// 将 Markdown 字符串转换为 WPF FlowDocument。
/// 用于在聊天气泡中渲染 AI 回复（代码高亮、列表、加粗等）。
/// </summary>
public class MarkdownToFlowDocumentConverter : System.Windows.Data.IValueConverter
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string markdown || string.IsNullOrWhiteSpace(markdown))
            return null;

        try
        {
            return Markdig.Wpf.Markdown.ToFlowDocument(markdown, Pipeline);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[MarkdownConverter] Markdown 解析失败，回退为纯文本");
            // Fallback: plain text paragraph
            var doc = new FlowDocument();
            var para = new Paragraph(new Run(markdown));
            doc.Blocks.Add(para);
            return doc;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
