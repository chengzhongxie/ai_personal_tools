using PersonalAssistant.Features.Chat.Models;

namespace PersonalAssistant.Features.Chat.Services;

public interface IChatService
{
    Task<ChatResponse> SendMessageAsync(string userMessage);
    void ClearHistory();
}
