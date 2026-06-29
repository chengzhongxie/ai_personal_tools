using System.IO;
using System.Text.Json;
using Serilog;

namespace PersonalAssistant.Infrastructure.Common.Services;

/// <summary>
/// 插件启用/禁用状态持久化服务。
/// 维护 HashSet&lt;string&gt; _disabledPlugins，持久化到 %APPDATA%\PersonalAssistant\plugin_state.json。
/// 资源成本：零定时器/线程，空闲时零开销。仅在 GetEnabled/SetEnabled 时触发磁盘 I/O。
/// </summary>
public class PluginStateService
{
    private static readonly string StateFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PersonalAssistant", "plugin_state.json");

    private readonly HashSet<string> _disabledPlugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public PluginStateService()
    {
        Load();
    }

    /// <summary>
    /// 判断指定名称的插件是否启用。默认启用（不在禁用集合中）。
    /// </summary>
    public bool IsEnabled(string pluginName)
    {
        lock (_lock)
            return !_disabledPlugins.Contains(pluginName);
    }

    /// <summary>
    /// 设置插件启用/禁用状态，并立即持久化。
    /// </summary>
    public void SetEnabled(string pluginName, bool enabled)
    {
        lock (_lock)
        {
            if (enabled)
                _disabledPlugins.Remove(pluginName);
            else
                _disabledPlugins.Add(pluginName);
        }
        Save();
    }

    /// <summary>
    /// 删除插件时清理其状态记录。
    /// </summary>
    public void RemoveState(string pluginName)
    {
        lock (_lock)
            _disabledPlugins.Remove(pluginName);
        Save();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(StateFilePath))
            {
                var json = File.ReadAllText(StateFilePath);
                var data = JsonSerializer.Deserialize<PluginStateData>(json);
                if (data?.DisabledPlugins is not null)
                {
                    foreach (var name in data.DisabledPlugins)
                        _disabledPlugins.Add(name);
                }
                Log.Debug("[PluginStateService] 加载状态: {Count} 个已禁用插件",
                    _disabledPlugins.Count);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[PluginStateService] 加载状态文件失败，使用空集合");
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(StateFilePath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string[] names;
            lock (_lock)
                names = _disabledPlugins.ToArray();

            var data = new PluginStateData { DisabledPlugins = names };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            var tmpPath = StateFilePath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, StateFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PluginStateService] 保存状态文件失败");
        }
    }

    private sealed class PluginStateData
    {
        public string[]? DisabledPlugins { get; set; }
    }
}
