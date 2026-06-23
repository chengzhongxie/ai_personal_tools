using Microsoft.Extensions.AI;

namespace PersonalAssistant.Core.Interfaces;

/// <summary>
/// 插件聚合器接口：聚合所有插件的工具和提示词，提供统一的工具执行入口。
/// ChatAgentService、WorkflowExecutorService、SchedulerService 通过此接口调用工具，避免循环依赖。
/// </summary>
public interface IToolPluginHost
{
    /// <summary>获取所有插件注册的 AIFunction 数组</summary>
    AIFunction[] GetAllTools();

    /// <summary>
    /// 执行指定工具，遍历所有插件找到匹配的工具并执行。
    /// 自动集成 WorkflowRecorder 录制（系统工具录制，管理工具和 local_llm 不录制）。
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="args">工具参数（JSON 字符串）</param>
    /// <returns>执行结果文本</returns>
    Task<string> ExecuteToolStepAsync(string toolName, string args);

    /// <summary>聚合所有插件的提示词片段，返回完整系统提示词的基础部分</summary>
    string GetAggregatedPrompt();
}
