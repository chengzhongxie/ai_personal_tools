namespace PersonalAssistant.Features.Chat.Models;

/// <summary>
/// DeepSeek API 连接配置
/// </summary>
public class ChatSettings
{
    /// <summary>DeepSeek API 密钥</summary>
    public string ApiKey { get; set; } = string.Empty;
    /// <summary>模型名称，默认 deepseek-v4-flash</summary>
    public string Model { get; set; } = "deepseek-v4-flash";
    /// <summary>API 端点地址（OpenAI 兼容格式）</summary>
    public string Endpoint { get; set; } = "https://api.deepseek.com";
}
