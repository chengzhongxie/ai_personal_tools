namespace PersonalAssistant.Features.Chat.Models;

/// <summary>
/// Token 用量统计模型。按月份分桶，保留 12 个月。
/// </summary>
public class TokenUsageStats
{
    /// <summary>当月桶</summary>
    public Dictionary<string, MonthlyTokenBucket> MonthlyBuckets { get; set; } = new();

    /// <summary>获取当前月份的桶（自动创建）</summary>
    public MonthlyTokenBucket GetCurrentMonth()
    {
        var key = DateTime.Now.ToString("yyyy-MM");
        if (!MonthlyBuckets.TryGetValue(key, out var bucket))
        {
            bucket = new MonthlyTokenBucket { Month = key };
            MonthlyBuckets[key] = bucket;
        }
        return bucket;
    }

    /// <summary>清除 12 个月之前的数据</summary>
    public void PruneOldBuckets()
    {
        var cutoff = DateTime.Now.AddMonths(-12).ToString("yyyy-MM");
        var keys = MonthlyBuckets.Keys.Where(k => string.Compare(k, cutoff, StringComparison.Ordinal) < 0).ToList();
        foreach (var key in keys)
            MonthlyBuckets.Remove(key);
    }
}

/// <summary>
/// 单月用量桶
/// </summary>
public class MonthlyTokenBucket
{
    /// <summary>yyyy-MM 格式</summary>
    public string Month { get; set; } = "";

    /// <summary>输入 tokens</summary>
    public int InputTokens { get; set; }

    /// <summary>输出 tokens</summary>
    public int OutputTokens { get; set; }

    /// <summary>远程 API 调用次数</summary>
    public int RequestCount { get; set; }

    /// <summary>总 tokens</summary>
    public int TotalTokens => InputTokens + OutputTokens;

    /// <summary>格式化的总量显示（如 "12.3K"）</summary>
    public string TotalDisplay => TotalTokens >= 1000 ? $"{TotalTokens / 1000.0:F1}K" : TotalTokens.ToString();
}
