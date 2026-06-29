using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PersonalAssistant.Features.Chat.Models;
using Serilog;

namespace PersonalAssistant.Features.Chat.Services;

/// <summary>
/// Token 用量统计服务：内存计数 + JSON 持久化到 %APPDATA%\PersonalAssistant\token_usage.json。
/// 资源成本：仅读写时触发磁盘 I/O，空闲时零开销。
/// </summary>
public class TokenUsageService
{
    private static readonly string UsagePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PersonalAssistant", "token_usage.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _lock = new();
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private TokenUsageStats _stats;

    public TokenUsageService()
    {
        _stats = Load();
    }

    /// <summary>当前累计统计</summary>
    public TokenUsageStats Stats
    {
        get { lock (_lock) return _stats; }
    }

    /// <summary>本轮用量显示文本</summary>
    public string CurrentRoundDisplay => $"本轮: {_currentRoundInput + _currentRoundOutput} tokens";

    private int _currentRoundInput;
    private int _currentRoundOutput;

    /// <summary>当月用量显示文本</summary>
    public string MonthlyDisplay
    {
        get
        {
            var bucket = Stats.GetCurrentMonth();
            return $"本月: {bucket.TotalDisplay} tokens";
        }
    }

    /// <summary>
    /// 记录一轮对话的 token 用量（在 ChatViewModel finally 中调用）。
    /// 输入 token 数通过估算（~4 chars/token for mixed text）。
    /// </summary>
    /// <param name="inputText">用户输入文本</param>
    /// <param name="outputText">AI 完整输出文本</param>
    /// <param name="isRemoteApi">是否调用了远程 API</param>
    public void RecordUsage(string inputText, string outputText, bool isRemoteApi)
    {
        // 估算 token 数（混合中英文约 4 chars/token）
        var inputTokens = Math.Max(1, (int)Math.Ceiling(inputText.Length / 4.0));
        var outputTokens = Math.Max(1, (int)Math.Ceiling(outputText.Length / 4.0));

        lock (_lock)
        {
            _currentRoundInput = inputTokens;
            _currentRoundOutput = outputTokens;

            var bucket = _stats.GetCurrentMonth();
            bucket.InputTokens += inputTokens;
            bucket.OutputTokens += outputTokens;
            if (isRemoteApi)
                bucket.RequestCount++;

            _stats.PruneOldBuckets();
        }

        // 异步持久化（信号量序列化，防止并发写损坏文件）
        _ = Task.Run(async () =>
        {
            await _saveLock.WaitAsync();
            try
            {
                Save(_stats);
            }
            catch (Exception ex) { Log.Warning(ex, "[TokenUsage] 持久化失败"); }
            finally { _saveLock.Release(); }
        });
    }

    /// <summary>获取格式化显示文本</summary>
    public string GetDisplayText()
    {
        lock (_lock)
        {
            var bucket = _stats.GetCurrentMonth();
            return $"本轮: {_currentRoundInput + _currentRoundOutput} tokens | 本月: {bucket.TotalDisplay} tokens";
        }
    }

    private static TokenUsageStats Load()
    {
        if (!File.Exists(UsagePath))
            return new TokenUsageStats();

        try
        {
            var json = File.ReadAllText(UsagePath);
            return JsonSerializer.Deserialize<TokenUsageStats>(json, JsonOptions) ?? new TokenUsageStats();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[TokenUsage] 加载历史统计失败，将重置");
            return new TokenUsageStats();
        }
    }

    private static void Save(TokenUsageStats stats)
    {
        var dir = Path.GetDirectoryName(UsagePath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(stats, JsonOptions);
        var tmpPath = UsagePath + ".tmp";
        File.WriteAllText(tmpPath, json);
        File.Move(tmpPath, UsagePath, overwrite: true);
    }
}
