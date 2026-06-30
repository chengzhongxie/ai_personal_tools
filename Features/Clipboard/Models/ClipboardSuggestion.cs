namespace PersonalAssistant.Features.Clipboard.Models;

/// <summary>
/// 剪贴板内容触发的操作建议
/// </summary>
public sealed class ClipboardSuggestion
{
    /// <summary>按钮上显示的标签</summary>
    public required string Label { get; init; }

    /// <summary>
    /// 预填到聊天输入框的文本（ExecuteDirectly=false 时使用）
    /// </summary>
    public string? ActionText { get; init; }

    /// <summary>
    /// true=本地直接执行（如 Process.Start 打开浏览器），false=发送给 AI
    /// </summary>
    public bool ExecuteDirectly { get; init; }

    /// <summary>
    /// ExecuteDirectly=true 时执行的回调
    /// </summary>
    public Action? DirectAction { get; init; }

    /// <summary>
    /// 执行后在弹窗内显示的结果文本（用于文本统计、Base64 解码、JSON 格式化等）。
    /// 如果非 null，点击按钮后不关闭弹窗，而是替换预览区显示此结果。
    /// </summary>
    public Func<string>? InlineResult { get; init; }
}
