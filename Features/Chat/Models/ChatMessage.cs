using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using PersonalAssistant.Features.Chat.Models.Enums;

namespace PersonalAssistant.Features.Chat.Models;

/// <summary>
/// 聊天界面中显示的一条消息。
/// 支持 Content 属性变更通知，供流式输出时 UI 实时更新。
/// </summary>
public partial class ChatMessage : ObservableObject
{
    /// <summary>消息角色（用户/助手/系统/工具）</summary>
    public MessageRole Role { get; set; }

    /// <summary>消息文本内容（Observable，流式输出时逐 token 更新）</summary>
    [ObservableProperty]
    private string _content = string.Empty;

    /// <summary>消息时间戳</summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>本次回复中调用的工具名称列表</summary>
    public List<string> ToolCalls { get; set; } = new();

    /// <summary>是否为错误消息</summary>
    public bool IsError { get; set; }

    // ──── 多对话支持 ────

    /// <summary>所属对话 ID</summary>
    public string? ConversationId { get; set; }

    // ──── 图片附件支持 ────

    /// <summary>图片文件路径（JSON 序列化持久化）</summary>
    public string? ImagePath { get; set; }

    /// <summary>图片字节数据（UI 绑定，不序列化）</summary>
    [JsonIgnore]
    public byte[]? ImageBytes { get; set; }

    /// <summary>是否有图片附件（计算属性，JsonIgnore）</summary>
    [JsonIgnore]
    public bool HasImage => ImageBytes is not null && ImageBytes.Length > 0;

    // ──── 消息编辑支持 ────

    /// <summary>是否处于编辑模式（JsonIgnore）</summary>
    [JsonIgnore]
    public bool IsEditing { get; set; }

    /// <summary>编辑时的文本（JsonIgnore）</summary>
    [JsonIgnore]
    public string? EditText { get; set; }

    // ──── 重新生成支持 ────

    /// <summary>是否允许重新生成（最后一条 Assistant 消息，JsonIgnore）</summary>
    [JsonIgnore]
    public bool CanRegenerate { get; set; }
}
