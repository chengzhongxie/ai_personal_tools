using System.ComponentModel;
using PersonalAssistant.Core.Interfaces;
using PersonalAssistant.Core.Services;
using PersonalAssistant.Features.Workflow.Models;
using PersonalAssistant.Features.Workflow.Services;

namespace PersonalAssistant.Features.Plugins.WorkflowPlugin;

/// <summary>
/// 工作流工具方法静态实现：list_workflows、run_workflow、delete_workflow、save_workflow
/// </summary>
internal static class WorkflowToolMethods
{
    [Description("List all saved workflow definitions")]
    public static string ListWorkflows(WorkflowStorageService storage)
    {
        var names = storage.ListAll();
        if (names.Count == 0)
            return "没有已保存的工作流。重复相同操作多次后，系统会自动检测模式并建议保存。";

        var lines = new List<string> { "已保存的工作流:" };
        foreach (var name in names)
        {
            var wf = storage.Load(name);
            if (wf is null) continue;
            lines.Add($"  - {wf.Name} ({wf.Steps.Count} 个步骤): {wf.Description}");
        }
        return string.Join("\n", lines);
    }

    [Description("Execute a saved workflow locally without calling AI. Returns execution results.")]
    public static async Task<string> RunWorkflow(
        [Description("Name of the workflow to execute")] string name,
        WorkflowStorageService storage,
        IToolPluginHost pluginHost)
    {
        var wf = storage.Load(name);
        if (wf is null)
            return $"工作流 \"{name}\" 未找到。使用 list_workflows 查看已保存列表。";

        try
        {
            var results = new List<string>();
            foreach (var step in wf.Steps)
            {
                results.Add($"## {step.ToolName}");
                var result = await pluginHost.ExecuteToolStepAsync(step.ToolName, step.Args);
                results.Add(result);
            }

            var summary = string.Join("\n\n", results);
            return $"工作流 \"{name}\" 执行完成:\n{summary}";
        }
        catch (Exception ex)
        {
            return $"执行工作流出错: {ex.Message}";
        }
    }

    [Description("Delete a saved workflow by name")]
    public static string DeleteWorkflow(
        [Description("Name of the workflow to delete")] string name,
        WorkflowStorageService storage,
        IDangerousToolPolicy policy)
    {
        if (!policy.ConfirmDangerous("delete_workflow", name))
            return $"用户取消了删除工作流 \"{name}\" 的操作。";
        var deleted = storage.Delete(name);
        return deleted
            ? $"工作流 \"{name}\" 已删除。"
            : $"工作流 \"{name}\" 未找到。使用 list_workflows 查看已保存列表。";
    }

    [Description("Save the most recently detected repeated tool pattern as a named workflow.")]
    public static string SaveWorkflow(
        [Description("Name for the new workflow")] string name,
        WorkflowStorageService storage,
        PluginSharedState sharedState)
    {
        var pending = sharedState.PendingSuggestion;
        if (pending is null)
            return "没有待保存的模式建议。请先执行一些操作，系统会在检测到重复模式后自动建议保存。";

        var records = WorkflowRecorder.SequenceToRecords(pending.ToolSequence);
        var wf = new WorkflowDefinition
        {
            Name = name,
            Description = $"自动检测的重复模式: {string.Join(" → ", pending.ToolSequence)} " +
                          $"(出现 {pending.OccurrenceCount} 次)",
            Steps = records,
            CreatedAt = DateTime.Now
        };
        storage.Save(wf);

        var seq = string.Join(" → ", pending.ToolSequence);
        sharedState.PendingSuggestion = null;

        return $"工作流 \"{name}\" 已保存。包含步骤: {seq}\n使用 run_workflow(\"{name}\") 执行，list_workflows 查看所有。";
    }
}
