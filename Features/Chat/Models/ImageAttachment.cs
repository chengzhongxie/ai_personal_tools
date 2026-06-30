namespace PersonalAssistant.Features.Chat.Models;

/// <summary>
/// 图片附件 POCO，用于 UI 绑定。
/// </summary>
public sealed class ImageAttachment
{
    public byte[] Bytes { get; set; } = [];
    public string MediaType { get; set; } = "image/png";
    public int Width { get; set; }
    public int Height { get; set; }
}
