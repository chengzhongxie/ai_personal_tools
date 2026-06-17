namespace PersonalAssistant.Features.Chat.Models;

public class ChatSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "deepseek-chat";
    public string Endpoint { get; set; } = "https://api.deepseek.com/v1";
}
