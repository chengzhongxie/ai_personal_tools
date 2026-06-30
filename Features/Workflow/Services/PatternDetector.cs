using PersonalAssistant.Features.Workflow.Models;

namespace PersonalAssistant.Features.Workflow.Services;

/// <summary>
/// 模式检测器。
/// 维护最近 50 轮会话的环形缓冲，检测 ≥3 次重复的工具名序列。
/// 已建议过的序列自动跳过，不重复提示。
/// </summary>
public class PatternDetector
{
    private const int MaxRounds = 50;
    private const int MinRepetitions = 4;
    private const int MinSequenceLength = 3;

    private readonly List<List<string>> _recentRounds = new();
    private readonly HashSet<string> _shownKeys = new();

    /// <summary>
    /// 添加一轮的工具序列并检测模式。
    /// </summary>
    /// <param name="toolSequence">本轮录制的工具名序列</param>
    /// <returns>检测到的模式（可能为 null）</returns>
    public PatternMatch? AddRound(List<string> toolSequence)
    {
        if (toolSequence.Count == 0)
            return null;

        _recentRounds.Add(toolSequence);
        while (_recentRounds.Count > MaxRounds)
            _recentRounds.RemoveAt(0);

        return DetectPattern(toolSequence);
    }

    private PatternMatch? DetectPattern(List<string> currentSequence)
    {
        if (currentSequence.Count < MinSequenceLength)
            return null;

        var seqKey = string.Join("→", currentSequence);

        // 已建议过的跳过
        if (_shownKeys.Contains(seqKey))
            return null;

        // 统计该序列在最近轮次中的出现次数
        var indices = new List<int>();
        for (int i = 0; i < _recentRounds.Count; i++)
        {
            if (SequencesEqual(_recentRounds[i], currentSequence))
                indices.Add(i);
        }

        if (indices.Count >= MinRepetitions)
        {
            _shownKeys.Add(seqKey);

            return new PatternMatch
            {
                ToolSequence = new List<string>(currentSequence),
                OccurrenceCount = indices.Count,
                RecentRoundIndices = indices,
                SuggestedName = string.Join("_", currentSequence)
            };
        }

        return null;
    }

    private static bool SequencesEqual(List<string> a, List<string> b)
    {
        if (a.Count != b.Count)
            return false;
        for (int i = 0; i < a.Count; i++)
            if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    /// <summary>清空检测历史（/clear 时调用）</summary>
    public void Reset()
    {
        _recentRounds.Clear();
        _shownKeys.Clear();
    }
}
