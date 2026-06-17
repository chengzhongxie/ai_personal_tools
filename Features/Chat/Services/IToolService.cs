namespace PersonalAssistant.Features.Chat.Services;

/// <summary>
/// 工具执行服务接口，负责执行 AI 请求的工具调用
/// </summary>
public interface IToolService
{
    /// <summary>
    /// 根据工具名称和 JSON 参数执行对应的工具，返回执行结果文本
    /// </summary>
    /// <param name="name">工具名称</param>
    /// <param name="argumentsJson">工具参数的 JSON 字符串</param>
    /// <returns>工具执行结果</returns>
    Task<string> ExecuteToolAsync(string name, string argumentsJson);
}
