using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PersonalAssistant.Features.Chat.Models;
using Serilog;

namespace PersonalAssistant.Features.Chat.Services;

/// <summary>
/// 多对话文件 I/O 服务。
/// 存储布局：
///   %APPDATA%\PersonalAssistant\conversations\
///     index.json  →  List&lt;ConversationInfo&gt;
///     {guid}.json →  每个对话独立文件
/// 首次启动自动迁移旧 chat_history.json 到默认对话。
/// 资源成本：仅读写时触发磁盘 I/O（按需消耗）。
/// </summary>
public class ConversationStorageService
{
    private static readonly string BaseDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PersonalAssistant", "conversations");

    private static readonly string IndexPath = Path.Combine(BaseDir, "index.json");

    private static readonly string LegacyHistoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PersonalAssistant", "chat_history.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private List<ConversationInfo>? _cachedIndex;
    private readonly Lock _indexLock = new();

    /// <summary>当前活跃的对话 ID</summary>
    public string? ActiveConversationId { get; set; }

    // ──── Init / Migration ────

    /// <summary>
    /// 初始化存储目录并在首次启动时迁移旧对话数据。
    /// </summary>
    public void Initialize()
    {
        if (!Directory.Exists(BaseDir))
            Directory.CreateDirectory(BaseDir);

        if (!File.Exists(IndexPath))
        {
            // 迁移旧的 chat_history.json
            if (File.Exists(LegacyHistoryPath))
            {
                try
                {
                    var json = File.ReadAllText(LegacyHistoryPath);
                    var messages = JsonSerializer.Deserialize<List<ChatMessage>>(json, JsonOptions);
                    if (messages is { Count: > 0 })
                    {
                        var convId = Guid.NewGuid().ToString("N");
                        var conv = new ConversationInfo
                        {
                            Id = convId,
                            Title = "默认对话",
                            CreatedAt = messages.Min(m => m.Timestamp),
                            UpdatedAt = messages.Max(m => m.Timestamp),
                            MessageCount = messages.Count,
                            IsDefault = true
                        };

                        SaveConversationMessages(convId, messages);
                        SaveIndex(new List<ConversationInfo> { conv });
                        ActiveConversationId = convId;

                        // 迁移成功后删除旧文件
                        File.Delete(LegacyHistoryPath);
                        Log.Information("[ConversationStorage] 已迁移旧 chat_history.json → {ConvId} ({Count} 条消息)",
                            convId, messages.Count);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[ConversationStorage] 迁移旧对话数据失败");
                }
            }

            // 创建默认对话
            CreateConversation("默认对话");
        }
    }

    // ──── Index CRUD ────

    public List<ConversationInfo> LoadIndex()
    {
        lock (_indexLock)
        {
            if (_cachedIndex is not null)
                return _cachedIndex;

            if (!File.Exists(IndexPath))
            {
                _cachedIndex = new List<ConversationInfo>();
                return _cachedIndex;
            }

            try
            {
                var json = File.ReadAllText(IndexPath);
                _cachedIndex = JsonSerializer.Deserialize<List<ConversationInfo>>(json, JsonOptions)
                    ?? new List<ConversationInfo>();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[ConversationStorage] 加载索引失败");
                _cachedIndex = new List<ConversationInfo>();
            }
            return _cachedIndex;
        }
    }

    private void SaveIndex(List<ConversationInfo> index)
    {
        lock (_indexLock)
        {
            _cachedIndex = index;
            var json = JsonSerializer.Serialize(index, JsonOptions);
            var tmp = IndexPath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, IndexPath, overwrite: true);
        }
    }

    // ──── Conversation CRUD ────

    public ConversationInfo CreateConversation(string title)
    {
        var conv = new ConversationInfo
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = title,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        var index = LoadIndex();
        index.Insert(0, conv);
        SaveIndex(index);
        SaveConversationMessages(conv.Id, new List<ChatMessage>());

        ActiveConversationId = conv.Id;
        Log.Information("[ConversationStorage] 创建对话: {Id} ({Title})", conv.Id, conv.Title);
        return conv;
    }

    public void RenameConversation(string conversationId, string newTitle)
    {
        var index = LoadIndex();
        var conv = index.Find(c => c.Id == conversationId);
        if (conv is null) return;

        conv.Title = newTitle;
        conv.UpdatedAt = DateTime.Now;
        SaveIndex(index);
    }

    public void DeleteConversation(string conversationId)
    {
        var index = LoadIndex();
        index.RemoveAll(c => c.Id == conversationId);
        SaveIndex(index);

        // 删除对话文件
        var convPath = GetConversationFilePath(conversationId);
        try { if (File.Exists(convPath)) File.Delete(convPath); } catch { }

        // 删除关联图片目录
        var imgDir = GetConversationImageDir(conversationId);
        try { if (Directory.Exists(imgDir)) Directory.Delete(imgDir, recursive: true); } catch { }

        Log.Information("[ConversationStorage] 删除对话: {Id}", conversationId);
    }

    public void UpdateConversationMeta(string conversationId, int messageCount)
    {
        var index = LoadIndex();
        var conv = index.Find(c => c.Id == conversationId);
        if (conv is null) return;

        conv.MessageCount = messageCount;
        conv.UpdatedAt = DateTime.Now;
        SaveIndex(index);
    }

    // ──── Message I/O ────

    public List<ChatMessage> LoadMessages(string conversationId)
    {
        var path = GetConversationFilePath(conversationId);
        if (!File.Exists(path))
            return new List<ChatMessage>();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<ChatMessage>>(json, JsonOptions)
                ?? new List<ChatMessage>();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[ConversationStorage] 加载对话消息失败: {Id}", conversationId);
            return new List<ChatMessage>();
        }
    }

    public void SaveMessages(string conversationId, IEnumerable<ChatMessage> messages)
    {
        var convMessages = messages
            .Where(m => m.Role != Features.Chat.Models.Enums.MessageRole.System)
            .TakeLast(200)
            .ToList();

        SaveConversationMessages(conversationId, convMessages);
        UpdateConversationMeta(conversationId, convMessages.Count);
    }

    private void SaveConversationMessages(string conversationId, List<ChatMessage> messages)
    {
        var path = GetConversationFilePath(conversationId);
        var json = JsonSerializer.Serialize(messages, JsonOptions);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    // ──── Search ────

    /// <summary>
    /// 在所有对话中搜索关键词（string.Contains，不区分大小写）。
    /// 返回包含匹配消息片段的结果列表。
    /// </summary>
    public List<ConversationSearchResult> SearchAllConversations(string query)
    {
        var results = new List<ConversationSearchResult>();
        if (string.IsNullOrWhiteSpace(query))
            return results;

        var index = LoadIndex();
        foreach (var conv in index)
        {
            var messages = LoadMessages(conv.Id);
            for (var i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];
                if (string.IsNullOrEmpty(msg.Content))
                    continue;

                var idx = msg.Content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;

                // 提取摘录（匹配位置前后各 40 字符）
                var start = Math.Max(0, idx - 40);
                var length = Math.Min(msg.Content.Length - start, 80 + query.Length);
                var excerpt = msg.Content.Substring(start, length);
                if (start > 0) excerpt = "..." + excerpt;
                if (start + length < msg.Content.Length) excerpt += "...";

                results.Add(new ConversationSearchResult
                {
                    ConversationId = conv.Id,
                    Title = conv.Title,
                    Excerpt = excerpt,
                    MessageIndex = i
                });
            }
        }

        return results;
    }

    // ──── Helpers ────

    private static string GetConversationFilePath(string conversationId)
        => Path.Combine(BaseDir, $"{conversationId}.json");

    /// <summary>获取对话对应的图片存储目录</summary>
    public static string GetConversationImageDir(string conversationId)
        => Path.Combine(BaseDir, $"{conversationId}_images");
}
