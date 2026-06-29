namespace PersonalAssistant.Features.Clipboard.Models;

/// <summary>
/// 剪贴板内容类型，由 ClipboardMonitor 通过纯本地启发式算法分类（零 token 消耗）
/// </summary>
public enum ClipboardContentType
{
    Unknown = 0,
    Url,
    Code,
    Path,
    Number,
    Text
}
