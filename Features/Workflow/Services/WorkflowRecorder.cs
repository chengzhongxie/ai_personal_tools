using PersonalAssistant.Features.Workflow.Models;

namespace PersonalAssistant.Features.Workflow.Services;

/// <summary>
/// 工具调用录制器。
/// ChatAgentService 的每个工具方法在执行时调用 RecordStep，
/// 累积当前轮次的工具名序列供 PatternDetector 分析。
/// </summary>
public class WorkflowRecorder
{
    private readonly List<string> _currentRound = new();
    private readonly List<ToolCallRecord> _currentRoundFull = new();

    /// <summary>录制单条工具调用</summary>
    public void RecordStep(string toolName, string args)
    {
        _currentRound.Add(toolName);
        _currentRoundFull.Add(new ToolCallRecord
        {
            ToolName = toolName,
            Args = args,
            Timestamp = DateTime.Now
        });
    }

    /// <summary>获取并清空当前轮次录制的工具名序列（供 PatternDetector）</summary>
    public List<string> CollectRound()
    {
        var round = new List<string>(_currentRound);
        _currentRound.Clear();
        _currentRoundFull.Clear();
        return round;
    }

    /// <summary>获取并清空当前轮次录制的完整 ToolCallRecord（供录屏保存）</summary>
    public List<ToolCallRecord> CollectRoundRecords()
    {
        var records = new List<ToolCallRecord>(_currentRoundFull);
        _currentRound.Clear();
        _currentRoundFull.Clear();
        return records;
    }

    /// <summary>是否有未收集的录制数据</summary>
    public bool HasPendingRecords => _currentRound.Count > 0;

    public static List<ToolCallRecord> SequenceToRecords(List<string> toolNames)
    {
        return toolNames.Select(name => new ToolCallRecord
        {
            ToolName = name,
            Args = "",
            Timestamp = DateTime.Now
        }).ToList();
    }
}
