using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using PersonalAssistant.Core.Interfaces;
using PersonalAssistant.Core.Services;
using PersonalAssistant.Features.Workflow.Services;

namespace PersonalAssistant.Features.Plugins.WorkflowPlugin;

/// <summary>
/// 工作流插件：提供 list_workflows、run_workflow、delete_workflow、save_workflow 四个 AI 工具。
/// 资源成本：1个单例，工具调用时磁盘 I/O + 工作流回放按需消耗。
/// </summary>
public class WorkflowPlugin : IToolPlugin
{
    private readonly WorkflowStorageService _storage;
    private readonly IServiceProvider _services;
    private IToolPluginHost? _pluginHost;
    private IDangerousToolPolicy? _policy;
    private readonly PluginSharedState _sharedState;
    private IToolPluginHost PluginHost => _pluginHost ??= _services.GetRequiredService<IToolPluginHost>();
    private IDangerousToolPolicy Policy => _policy ??= _services.GetRequiredService<IDangerousToolPolicy>();

    public string Name => "Workflow";

    public WorkflowPlugin(WorkflowStorageService storage,
        IServiceProvider services,
        PluginSharedState sharedState)
    {
        _storage = storage;
        _services = services;
        _sharedState = sharedState;
    }

    public AIFunction[] GetTools()
    {
        return new[]
        {
            AIFunctionFactory.Create(new Func<string>(ListWorkflows), name: "list_workflows"),
            AIFunctionFactory.Create(new Func<string, Task<string>>(RunWorkflowWrapper), name: "run_workflow"),
            AIFunctionFactory.Create(new Func<string, string>(DeleteWorkflowWrapper), name: "delete_workflow"),
            AIFunctionFactory.Create(new Func<string, string>(SaveWorkflowWrapper), name: "save_workflow"),
        };
    }

    public async Task<string?> TryExecuteToolAsync(string toolName, string args)
    {
        return toolName switch
        {
            "list_workflows" => WorkflowToolMethods.ListWorkflows(_storage),
            "run_workflow" => await WorkflowToolMethods.RunWorkflow(args, _storage, PluginHost),
            "delete_workflow" => WorkflowToolMethods.DeleteWorkflow(args, _storage, Policy),
            "save_workflow" => WorkflowToolMethods.SaveWorkflow(args, _storage, _sharedState),
            _ => null
        };
    }

    public string? GetPromptFragment() => null;

    [Description("List all saved workflow definitions")]
    private string ListWorkflows() => WorkflowToolMethods.ListWorkflows(_storage);

    [Description("Execute a saved workflow locally without calling AI. Returns execution results.")]
    private Task<string> RunWorkflowWrapper(
        [Description("Name of the workflow to execute")] string name) =>
        WorkflowToolMethods.RunWorkflow(name, _storage, PluginHost);

    [Description("Delete a saved workflow by name")]
    private string DeleteWorkflowWrapper(
        [Description("Name of the workflow to delete")] string name) =>
        WorkflowToolMethods.DeleteWorkflow(name, _storage, Policy);

    [Description("Save the most recently detected repeated tool pattern as a named workflow.")]
    private string SaveWorkflowWrapper(
        [Description("Name for the new workflow")] string name) =>
        WorkflowToolMethods.SaveWorkflow(name, _storage, _sharedState);
}
