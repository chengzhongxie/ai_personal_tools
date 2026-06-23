using PersonalAssistant.Core.Interfaces;
using PersonalAssistant.Features.Workflow.Models;

namespace PersonalAssistant.Features.Workflow.Services;

/// <summary>
/// 工作流本地执行器。
/// 回放已保存的工作流，直接调用工具逻辑而不经过 AI。
/// 通过 IToolPluginHost 接口避免与 ChatAgentService 的循环依赖。
/// 资源成本：仅执行时消耗（按需），无后台 CPU 开销。
/// </summary>
public class WorkflowExecutorService
{
    private readonly IToolPluginHost _pluginHost;

    public WorkflowExecutorService(IToolPluginHost pluginHost)
    {
        _pluginHost = pluginHost;
    }

    /// <summary>
    /// 本地执行工作流的所有步骤，不调用 AI。
    /// </summary>
    /// <param name="workflow">要执行的工作流</param>
    /// <returns>每步执行结果的汇总文本</returns>
    public async Task<string> ExecuteAsync(WorkflowDefinition workflow)
    {
        var results = new List<string>();

        foreach (var step in workflow.Steps)
        {
            results.Add($"## {step.ToolName}");
            var result = await _pluginHost.ExecuteToolStepAsync(step.ToolName, step.Args);
            results.Add(result);
        }

        return string.Join("\n\n", results);
    }
}
