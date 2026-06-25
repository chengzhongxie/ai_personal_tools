using System.Text.RegularExpressions;
using PersonalAssistant.Core.Interfaces;
using PersonalAssistant.Features.Workflow.Models;
using Serilog;

namespace PersonalAssistant.Features.Workflow.Services;

/// <summary>
/// 工作流本地执行器。
/// 回放已保存的工作流，直接调用工具逻辑而不经过 AI。
/// 支持变量引用 (${var}) 和条件分支。
/// 通过 IToolPluginHost 接口避免与 ChatAgentService 的循环依赖。
/// 资源成本：仅执行时消耗（按需），无后台 CPU 开销。
/// </summary>
public class WorkflowExecutorService
{
    private readonly IToolPluginHost _pluginHost;

    // ${variableName} 变量引用模式
    private static readonly Regex _varPattern = new(@"\$\{(\w+)\}", RegexOptions.Compiled);
    // 简单条件: ${var} == "value" 或 ${var} != "value"
    private static readonly Regex _condPattern = new(
        @"\$\{(\w+)\}\s*(==|!=)\s*""([^""]*)""", RegexOptions.Compiled);

    public WorkflowExecutorService(IToolPluginHost pluginHost)
    {
        _pluginHost = pluginHost;
    }

    /// <summary>
    /// 本地执行工作流的所有步骤，不调用 AI。
    /// 支持变量解析和条件分支。
    /// </summary>
    /// <param name="workflow">要执行的工作流</param>
    /// <param name="inputVariables">可选的输入变量覆盖值</param>
    /// <returns>每步执行结果的汇总文本</returns>
    public async Task<string> ExecuteAsync(WorkflowDefinition workflow,
        Dictionary<string, string>? inputVariables = null)
    {
        var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 初始化变量：默认值 → 输入覆盖
        if (workflow.Variables is not null)
        {
            foreach (var kv in workflow.Variables)
                context[kv.Key] = kv.Value;
        }
        if (inputVariables is not null)
        {
            foreach (var kv in inputVariables)
                context[kv.Key] = kv.Value;
        }

        var results = new List<string>();
        int stepIndex = 0;

        foreach (var step in workflow.Steps)
        {
            stepIndex++;

            // 评估条件
            if (!string.IsNullOrEmpty(step.Condition))
            {
                if (!EvaluateCondition(step.Condition, context))
                {
                    Log.Information("[WorkflowExecutor] 步骤 {Idx} 条件不满足，跳过: {Cond}",
                        stepIndex, step.Condition);
                    results.Add($"## {step.ToolName} (已跳过: 条件不满足)");
                    continue;
                }
            }

            // 解析变量引用
            var resolvedArgs = ResolveVariables(step.Args, context);
            Log.Information("[WorkflowExecutor] 步骤 {Idx}: {Tool}({Args})",
                stepIndex, step.ToolName, resolvedArgs);

            results.Add($"## {step.ToolName}");
            var result = await _pluginHost.ExecuteToolStepAsync(step.ToolName, resolvedArgs);
            results.Add(result);

            // 存储输出变量
            if (!string.IsNullOrEmpty(step.OutputVariable))
            {
                var outputValue = result?.Trim() ?? "";
                // 截取第一行非空值作为变量值
                var firstLine = outputValue.Split('\n')
                    .Select(l => l.Trim())
                    .FirstOrDefault(l => l.Length > 0 && !l.StartsWith("##")) ?? outputValue;
                context[step.OutputVariable] = firstLine;
                Log.Information("[WorkflowExecutor] 设置变量 {Var} = {Val}",
                    step.OutputVariable, firstLine.Length > 80 ? firstLine[..80] + "..." : firstLine);
            }
        }

        return string.Join("\n\n", results);
    }

    /// <summary>解析 Args 中的 ${var} 变量引用</summary>
    public static string ResolveVariables(string args, Dictionary<string, string> context)
    {
        if (string.IsNullOrEmpty(args)) return args;
        return _varPattern.Replace(args, match =>
        {
            var varName = match.Groups[1].Value;
            return context.TryGetValue(varName, out var val) ? val : match.Value;
        });
    }

    /// <summary>评估条件表达式：${var} == "value" 或 ${var} != "value"</summary>
    public static bool EvaluateCondition(string condition, Dictionary<string, string> context)
    {
        var match = _condPattern.Match(condition);
        if (!match.Success)
        {
            // 无法解析的条件，默认通过
            Log.Warning("[WorkflowExecutor] 无法解析条件表达式: {Cond}", condition);
            return true;
        }

        var varName = match.Groups[1].Value;
        var op = match.Groups[2].Value;
        var expected = match.Groups[3].Value;

        var actual = context.TryGetValue(varName, out var val) ? val : "";
        var result = op == "=="
            ? string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
            : !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

        return result;
    }

    /// <summary>
    /// 扫描步骤列表，检测可提取为变量的重复字符串。
    /// 返回建议的变量名和值（用于 WorkflowRecorder 自动建议）。
    /// </summary>
    public static Dictionary<string, string> DetectVariables(List<ToolCallRecord> steps)
    {
        var suggestions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (steps.Count < 2) return suggestions;

        // 收集所有非空的 Args 片段（路径、URL 等）
        var allTokens = new List<(string token, int stepIdx)>();

        for (int i = 0; i < steps.Count; i++)
        {
            var args = steps[i].Args;
            if (string.IsNullOrEmpty(args)) continue;

            // 按空格和引号分割提取 token
            var tokens = Regex.Matches(args, @"""([^""]*)""|(\S+)")
                .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
                .Where(t => t.Length > 3) // 忽略太短的 token
                .ToList();

            foreach (var token in tokens)
            {
                // 只关注看起来像路径、URL 或特定值的 token
                if (token.Contains('/') || token.Contains('\\') ||
                    token.Contains(':') || token.Contains('.') ||
                    char.IsUpper(token[0]))
                {
                    allTokens.Add((token, i));
                }
            }
        }

        // 找出出现在不同步骤中的相同值
        var valueGroups = allTokens
            .GroupBy(t => t.token, StringComparer.Ordinal)
            .Where(g => g.Select(x => x.stepIdx).Distinct().Count() >= 2)
            .Take(5); // 最多建议 5 个

        int varIndex = 1;
        foreach (var group in valueGroups)
        {
            var varName = "var" + varIndex++;
            suggestions[varName] = group.Key;
        }

        return suggestions;
    }
}
