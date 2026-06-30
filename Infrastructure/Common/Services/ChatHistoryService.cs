using PersonalAssistant.Features.Chat.Models;
using PersonalAssistant.Features.Chat.Services;

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
/// 聊天历史持久化服务（委托给 ConversationStorageService）。
/// 资源成本：仅读写时触发磁盘 I/O（按需消耗）。
/// </summary>
public class ChatHistoryService : IChatHistoryService
{
    private readonly ConversationStorageService _storage;

    public ChatHistoryService(ConversationStorageService storage)
    {
        _storage = storage;
    }

    /// <summary>
    /// 保存聊天消息列表到当前活跃对话的文件中。
    /// </summary>
    public void Save(IEnumerable<ChatMessage> messages)
    {
        if (_storage.ActiveConversationId is null) return;
        _storage.SaveMessages(_storage.ActiveConversationId, messages);
    }

    /// <summary>
    /// 从当前活跃对话文件加载消息列表。
    /// </summary>
    public List<ChatMessage> Load()
    {
        if (_storage.ActiveConversationId is null)
        {
            // 未初始化：触发初始化并返回默认对话的消息
            _storage.Initialize();
        }
        return _storage.ActiveConversationId is not null
            ? _storage.LoadMessages(_storage.ActiveConversationId)
            : new List<ChatMessage>();
    }

    /// <summary>是否有已保存的历史记录</summary>
    public bool HasHistory => _storage.ActiveConversationId is not null
        && System.IO.File.Exists(System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PersonalAssistant", "conversations", $"{_storage.ActiveConversationId}.json"));
}
