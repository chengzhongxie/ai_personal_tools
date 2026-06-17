namespace PersonalAssistant.Features.Chat.Models;

public class ChatResponse
{
    public string Content { get; set; } = string.Empty;
    public List<string> ToolCalls { get; set; } = new();
    public bool IsError { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}
