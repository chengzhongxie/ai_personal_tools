namespace PersonalAssistant.Features.Chat.Services;

public interface IToolService
{
    Task<string> ExecuteToolAsync(string name, string argumentsJson);
}
