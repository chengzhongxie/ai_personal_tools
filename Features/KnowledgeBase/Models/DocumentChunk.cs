using System.Text.Json.Serialization;

namespace PersonalAssistant.Features.KnowledgeBase.Models;

/// <summary>
/// 文档分块：将文档拆分为固定大小的重叠块用于检索
/// </summary>
public class DocumentChunk
{
    /// <summary>块 ID（文件名 + 序号）</summary>
    public string Id { get; set; } = "";

    /// <summary>来源文件路径</summary>
    public string SourceFile { get; set; } = "";

    /// <summary>块文本内容</summary>
    public string Content { get; set; } = "";

    /// <summary>块起始位置</summary>
    public int CharOffset { get; set; }

    /// <summary>索引时间</summary>
    public DateTime IndexedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 知识库索引元数据
/// </summary>
public class KnowledgeBaseIndex
{
    /// <summary>索引的目录路径</summary>
    public string SourceDirectory { get; set; } = "";

    /// <summary>所有文档块</summary>
    public List<DocumentChunk> Chunks { get; set; } = new();

    /// <summary>上次索引时间</summary>
    public DateTime LastIndexed { get; set; } = DateTime.Now;

    /// <summary>索引的文件数量</summary>
    public int FileCount { get; set; }
}
