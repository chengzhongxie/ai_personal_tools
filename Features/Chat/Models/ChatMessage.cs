using PersonalAssistant.Features.Chat.Models.Enums;

namespace PersonalAssistant.Features.Chat.Models;

public class ChatMessage
{
    public MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public List<string> ToolCalls { get; set; } = new();
    public bool IsError { get; set; }
}
