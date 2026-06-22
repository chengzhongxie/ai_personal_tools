using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PersonalAssistant.Features.Workflow.Models;

namespace PersonalAssistant.Features.Workflow.Services;

/// <summary>
/// 工作流持久化服务。
/// 将工作流定义保存到 %APPDATA%\PersonalAssistant\workflows\ 目录的 JSON 文件中。
/// </summary>
public class WorkflowStorageService
{
    private static readonly string WorkflowDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PersonalAssistant", "workflows");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>保存工作流定义到磁盘</summary>
    public void Save(WorkflowDefinition workflow)
    {
        if (!Directory.Exists(WorkflowDir))
            Directory.CreateDirectory(WorkflowDir);

        var path = GetPath(workflow.Name);
        var json = JsonSerializer.Serialize(workflow, JsonOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>加载单个工作流</summary>
    public WorkflowDefinition? Load(string name)
    {
        var path = GetPath(name);
        if (!File.Exists(path))
            return null;

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<WorkflowDefinition>(json);
    }

    /// <summary>列出所有已保存的工作流名称</summary>
    public List<string> ListAll()
    {
        if (!Directory.Exists(WorkflowDir))
            return new List<string>();

        return Directory.GetFiles(WorkflowDir, "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .ToList();
    }

    /// <summary>删除工作流</summary>
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
        return Path.Combine(WorkflowDir, $"{safeName}.json");
    }
}
