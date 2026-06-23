using PersonalAssistant.Core.Interfaces;
using Serilog;

namespace PersonalAssistant.Features.Scheduler.Services;

/// <summary>
/// 每日定时任务调度器。
/// 使用 30s 间隔的 System.Threading.Timer 检查到期的定时任务并执行。
/// 通过 IToolPluginHost 接口避免与 ChatAgentService 的循环依赖。
/// 任务列表内存缓存（5 分钟刷新），避免每次 Tick 读取磁盘。
/// 资源成本：30s 定时器 + 每个任务一次 ExecuteToolStepAsync 调用，无任务时仅定时器 Tick 开销（趋近零 CPU）。
/// </summary>
public class SchedulerService : IDisposable
{
    private readonly IToolPluginHost _pluginHost;
    private readonly SchedulerStorageService _storage;
    private readonly System.Threading.Timer _timer;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private List<Models.ScheduledTask>? _cachedTasks;
    private DateTime _lastCacheRefresh = DateTime.MinValue;
    private static readonly TimeSpan CacheRefreshInterval = TimeSpan.FromMinutes(5);
    private bool _disposed;

    public SchedulerService(IToolPluginHost pluginHost, SchedulerStorageService storage)
    {
        _pluginHost = pluginHost;
        _storage = storage;

        // 30s 间隔检查，符合低功耗设计约束（≥1s）
        _timer = new System.Threading.Timer(
            callback: async _ =>
            {
                try { await TickAsync(); }
                catch (Exception ex) { Log.Error(ex, "[Scheduler] Timer 回调异常"); }
            },
            state: null,
            dueTime: TimeSpan.FromSeconds(5),
            period: TimeSpan.FromSeconds(30));
    }

    private async Task TickAsync()
    {
        if (!await _semaphore.WaitAsync(0))
            return;

        try
        {
            var now = DateTime.Now;
            var currentTime = now.ToString("HH:mm");
            var today = now.ToString("yyyy-MM-dd");

            // 内存缓存：5 分钟内不重复读取磁盘
            if (_cachedTasks is null || now - _lastCacheRefresh > CacheRefreshInterval)
            {
                _cachedTasks = _storage.LoadAllEnabled();
                _lastCacheRefresh = now;
            }

            foreach (var task in _cachedTasks)
            {
                if (task.TimeOfDay != currentTime)
                    continue;

                if (task.LastRunDate == today)
                    continue;

                Log.Information("[Scheduler] 执行定时任务: {Name} ({ToolName} {ToolArgs})",
                    task.Name, task.ToolName, task.ToolArgs);

                try
                {
                    var result = await _pluginHost.ExecuteToolStepAsync(task.ToolName, task.ToolArgs);
                    Log.Information("[Scheduler] 任务完成: {Name} → {Result}", task.Name, result);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[Scheduler] 任务执行失败: {Name}", task.Name);
                }

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
