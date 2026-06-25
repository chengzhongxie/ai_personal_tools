using System.IO;
using Serilog;

namespace PersonalAssistant.Features.Plugins;

/// <summary>
/// 插件文件监控器：使用 FileSystemWatcher 监控外部插件目录的 .cs 文件变更。
/// 500ms debounce 防止重复事件。
/// 资源成本：FileSystemWatcher 基于 OS 事件，空闲时零 CPU 消耗。
/// </summary>
public class PluginFileWatcher : IDisposable
{
    private readonly FileSystemWatcher? _watcher;
    private readonly Dictionary<string, DateTime> _lastFired = new();
    private readonly object _lock = new();
    private bool _disposed;

    private static readonly string PluginsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PersonalAssistant", "Plugins");

    /// <summary>插件文件变更事件（参数为文件路径）</summary>
    public event Action<string>? PluginFileChanged;

    public PluginFileWatcher()
    {
        if (!Directory.Exists(PluginsDir))
            Directory.CreateDirectory(PluginsDir);

        _watcher = new FileSystemWatcher(PluginsDir, "*.cs")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime |
                           NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnFileEvent;
        _watcher.Created += OnFileEvent;
        _watcher.Renamed += (_, e) => OnFileEvent(null, new FileSystemEventArgs(
            WatcherChangeTypes.Changed, PluginsDir, e.Name!));

        Log.Information("[PluginFileWatcher] 开始监控目录: {Dir}", PluginsDir);
    }

    /// <summary>手动触发重载（供插件市场安装后调用）</summary>
    public void TriggerReload(string filePath)
    {
        Log.Information("[PluginFileWatcher] 手动触发重载: {File}", Path.GetFileName(filePath));
        PluginFileChanged?.Invoke(filePath);
    }

    /// <summary>
    /// 启动监控（已通过构造函数自动启动，此方法为空操作兼容接口）。
    /// </summary>
    public void Start()
    {
        if (_watcher is not null)
            _watcher.EnableRaisingEvents = true;
    }

    /// <summary>停止监控</summary>
    public void Stop()
    {
        if (_watcher is not null)
            _watcher.EnableRaisingEvents = false;
    }

    private void OnFileEvent(object? sender, FileSystemEventArgs e)
    {
        var path = e.FullPath;
        var now = DateTime.UtcNow;

        lock (_lock)
        {
            // 500ms debounce：同一文件短时间内多次事件只触发一次
            if (_lastFired.TryGetValue(path, out var last) &&
                (now - last).TotalMilliseconds < 500)
                return;

            _lastFired[path] = now;
        }

        Log.Information("[PluginFileWatcher] 检测到插件变更: {File}", Path.GetFileName(path));
        PluginFileChanged?.Invoke(path);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }
    }
}
