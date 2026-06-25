using PersonalAssistant.Features.Workflow.Models;

namespace PersonalAssistant.Features.Workflow.Services;

/// <summary>
/// 工具调用录制器。
/// 被多个线程调用（MAF 工具循环、定时器、工作流回放），加锁保护。
/// ChatAgentService 的每个工具方法在执行时调用 RecordStep，
/// 累积当前轮次的工具名序列供 PatternDetector 分析。
/// </summary>
public class WorkflowRecorder
{
    private readonly object _lock = new();
    private readonly List<string> _currentRound = new();
    private readonly List<ToolCallRecord> _currentRoundFull = new();

    /// <summary>录制单条工具调用（线程安全）</summary>
    public void RecordStep(string toolName, string args)
    {
        lock (_lock)
        {
            _currentRound.Add(toolName);
            _currentRoundFull.Add(new ToolCallRecord
            {
                ToolName = toolName,
                Args = args,
                Timestamp = DateTime.Now
            });
        }
    }

    /// <summary>获取并清空当前轮次录制的工具名序列（供 PatternDetector，线程安全）</summary>
    public List<string> CollectRound()
    {
        lock (_lock)
        {
            var round = new List<string>(_currentRound);
            _currentRound.Clear();
            _currentRoundFull.Clear();
            return round;
        }
    }

    /// <summary>获取并清空当前轮次录制的完整 ToolCallRecord（线程安全）</summary>
    public List<ToolCallRecord> CollectRoundRecords()
    {
        lock (_lock)
        {
            var records = new List<ToolCallRecord>(_currentRoundFull);
            _currentRound.Clear();
            _currentRoundFull.Clear();
            return records;
        }
    }

    /// <summary>是否有未收集的录制数据（线程安全）</summary>
    public bool HasPendingRecords { get { lock (_lock) return _currentRound.Count > 0; } }

    /// <summary>
    /// 扫描当前录制的步骤，检测可提取为变量的重复字符串值。
    /// 返回建议的变量名→值映射，供 UI 展示或自动保存。
    /// </summary>
    public Dictionary<string, string> DetectVariableSuggestions()
    {
        List<ToolCallRecord> records;
        lock (_lock)
        {
            records = new List<ToolCallRecord>(_currentRoundFull);
        }

        return WorkflowExecutorService.DetectVariables(records);
    }

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
