using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PersonalAssistant.Features.Scheduler.Models;

namespace PersonalAssistant.Features.Scheduler.Services;

/// <summary>
/// 定时任务持久化服务。
/// 将调度任务保存到 %APPDATA%\PersonalAssistant\schedules\ 目录的 JSON 文件中。
/// </summary>
public class SchedulerStorageService
{
    private static readonly string ScheduleDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PersonalAssistant", "schedules");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>保存调度任务到磁盘</summary>
    public void Save(ScheduledTask task)
    {
        if (!Directory.Exists(ScheduleDir))
            Directory.CreateDirectory(ScheduleDir);

        var path = GetPath(task.Name);
        var json = JsonSerializer.Serialize(task, JsonOptions);
        var tmpPath = path + ".tmp";
        File.WriteAllText(tmpPath, json);
        File.Move(tmpPath, path, overwrite: true);
    }

    /// <summary>更新任务的 LastRunTimestamp 并持久化</summary>
    public void UpdateLastRunTimestamp(string name, string timestamp)
    {
        var task = Load(name);
        if (task is null) return;

        task.LastRunTimestamp = timestamp;
        Save(task);
    }

    /// <summary>[兼容旧版] 更新任务的 LastRunTimestamp 并持久化</summary>
    [Obsolete("Use UpdateLastRunTimestamp instead")]
    public void UpdateLastRunDate(string name, string date)
    {
        // 旧版 date 是 "yyyy-MM-dd"，添加默认分钟
        UpdateLastRunTimestamp(name, date + " 00:00");
    }

    /// <summary>加载单个调度任务</summary>
    public ScheduledTask? Load(string name)
    {
        var path = GetPath(name);
        if (!File.Exists(path))
            return null;

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ScheduledTask>(json);
    }

    /// <summary>列出所有已保存的调度任务名称</summary>
    public List<string> ListAll()
    {
        if (!Directory.Exists(ScheduleDir))
            return new List<string>();

        return Directory.GetFiles(ScheduleDir, "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .ToList();
    }

    /// <summary>加载所有已启用的调度任务</summary>
    public List<ScheduledTask> LoadAllEnabled()
    {
        var names = ListAll();
        var tasks = new List<ScheduledTask>();
        foreach (var name in names)
        {
            var task = Load(name);
            if (task is { IsEnabled: true })
                tasks.Add(task);
        }
        return tasks;
    }

    /// <summary>删除调度任务</summary>
    public bool Delete(string name)
    {
        var path = GetPath(name);
        if (!File.Exists(path))
            return false;

        File.Delete(path);
        return true;
    }

    private static string GetPath(string name)
    {
        // 防止路径遍历攻击
        var safeName = Path.GetFileName(name);
        return Path.Combine(ScheduleDir, $"{safeName}.json");
    }
}
