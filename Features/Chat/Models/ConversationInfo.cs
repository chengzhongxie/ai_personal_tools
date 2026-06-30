using System.Text.Json.Serialization;

namespace PersonalAssistant.Features.Chat.Models;

/// <summary>
/// 对话元数据 POCO。每个对话对应一个独立 JSON 文件。
/// </summary>
public sealed class ConversationInfo
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public int MessageCount { get; set; }

    /// <summary>是否为迁移后的默认对话</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsDefault { get; set; }

    /// <summary>是否为当前活跃对话（仅运行时使用，不持久化）</summary>
    [JsonIgnore]
    public bool IsActive { get; set; }
}
