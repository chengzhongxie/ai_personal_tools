using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PersonalAssistant.Features.Chat.Models;

namespace PersonalAssistant.Infrastructure.Common.Services;

/// <summary>
/// 聊天历史持久化服务接口
/// </summary>
public interface IChatHistoryService
{
    void Save(IEnumerable<ChatMessage> messages);
    List<ChatMessage> Load();
    bool HasHistory { get; }
}

/// <summary>
/// 聊天历史持久化服务。
/// 将对话消息列表保存到 %APPDATA%\PersonalAssistant\chat_history.json。
/// 资源成本：仅读写时触发磁盘 I/O（按需消耗）。
/// </summary>
public class ChatHistoryService : IChatHistoryService
{
    private static readonly string HistoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PersonalAssistant", "chat_history.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 保存聊天消息列表到磁盘。
    /// 跳过 System 角色消息（模式建议等临时消息，不需要持久化）。
    /// 最多保存 200 条消息。
    /// </summary>
    public void Save(IEnumerable<ChatMessage> messages)
    {
        var toSave = messages
            .Where(m => m.Role != Features.Chat.Models.Enums.MessageRole.System)
            .TakeLast(200)
            .ToList();

        var dir = Path.GetDirectoryName(HistoryPath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(toSave, JsonOptions);
        File.WriteAllText(HistoryPath, json);
    }

    /// <summary>
    /// 从磁盘加载聊天消息列表。
    /// 文件不存在或损坏时返回空列表。
    /// </summary>
    public List<ChatMessage> Load()
    {
        if (!File.Exists(HistoryPath))
            return new List<ChatMessage>();

        try
        {
            var json = File.ReadAllText(HistoryPath);
            return JsonSerializer.Deserialize<List<ChatMessage>>(json, JsonOptions)
                   ?? new List<ChatMessage>();
        }
        catch
        {
            return new List<ChatMessage>();
        }
    }

    /// <summary>
    /// 是否有已保存的历史记录
    /// </summary>
    public bool HasHistory => File.Exists(HistoryPath);
}
