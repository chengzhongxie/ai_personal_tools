using PersonalAssistant.Features.Chat.Services;
using Serilog;

namespace PersonalAssistant.Features.Scheduler.Services;

/// <summary>
/// 每日定时任务调度器。
/// 使用 30s 间隔的 System.Threading.Timer 检查到期的定时任务并执行。
/// 资源成本：30s 定时器 + 每个任务一次 ExecuteToolStepAsync 调用，无任务时仅定时器 Tick 开销（趋近零 CPU）。
/// </summary>
public class SchedulerService : IDisposable
{
    /// <summary>已知工具名称列表，用于调度命令校验</summary>
    public static readonly HashSet<string> KnownTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "read_file", "write_file", "list_files", "web_fetch",
        "run_shell", "run_command", "find_app", "send_keys",
        "window_info", "focus_window"
    };

    private readonly ChatAgentService _chatAgent;
    private readonly SchedulerStorageService _storage;
    private readonly System.Threading.Timer _timer;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _disposed;

    public SchedulerService(ChatAgentService chatAgent, SchedulerStorageService storage)
    {
        _chatAgent = chatAgent;
        _storage = storage;

        // 30s 间隔检查，符合低功耗设计约束（≥1s）
        _timer = new System.Threading.Timer(
            callback: _ => _ = TickAsync(),
            state: null,
            dueTime: TimeSpan.FromSeconds(5),  // 启动后 5s 首次检查
            period: TimeSpan.FromSeconds(30));
    }

    private async Task TickAsync()
    {
        // SemaphoreSlim 防重入：上一次 Tick 未完成则跳过本次
        if (!await _semaphore.WaitAsync(0))
            return;

        try
        {
            var now = DateTime.Now;
            var currentTime = now.ToString("HH:mm");
            var today = now.ToString("yyyy-MM-dd");

            var tasks = _storage.LoadAllEnabled();
            foreach (var task in tasks)
            {
                if (task.TimeOfDay != currentTime)
                    continue;

                // 今天已运行过，跳过
                if (task.LastRunDate == today)
                    continue;

                Log.Information("[Scheduler] 执行定时任务: {Name} ({ToolName} {ToolArgs})",
                    task.Name, task.ToolName, task.ToolArgs);

                try
                {
                    var result = await _chatAgent.ExecuteToolStepAsync(task.ToolName, task.ToolArgs);
                    Log.Information("[Scheduler] 任务完成: {Name} → {Result}", task.Name, result);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[Scheduler] 任务执行失败: {Name}", task.Name);
                }

                // 无论成败都记录日期，防止同一天反复重试
                _storage.UpdateLastRunDate(task.Name, today);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Scheduler] Tick 异常");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer.Dispose();
        _semaphore.Dispose();
    }
}
