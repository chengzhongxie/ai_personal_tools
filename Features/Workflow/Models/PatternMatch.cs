namespace PersonalAssistant.Features.Workflow.Models;

/// <summary>
/// 检测到的重复工具调用模式，用于向用户建议创建工作流
/// </summary>
public class PatternMatch
{
    /// <summary>重复的工具名序列</summary>
    public List<string> ToolSequence { get; set; } = new();

    /// <summary>出现次数</summary>
    public int OccurrenceCount { get; set; }

    /// <summary>最近一次出现的轮次索引列表</summary>
    public List<int> RecentRoundIndices { get; set; } = new();

    /// <summary>建议的工作流名称</summary>
    public string SuggestedName { get; set; } = string.Empty;
}
