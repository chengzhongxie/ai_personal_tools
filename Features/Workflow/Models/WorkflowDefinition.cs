namespace PersonalAssistant.Features.Workflow.Models;

/// <summary>
/// 用户命名的可回放工作流，包含工具步骤序列和变量绑定
/// </summary>
public class WorkflowDefinition
{
    /// <summary>工作流名称（用户指定）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>工作流描述（自动生成）</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>工具步骤序列</summary>
    public List<ToolCallRecord> Steps { get; set; } = new();

    /// <summary>输入变量默认值（如 "target_dir": "C:\\Users"）</summary>
    public Dictionary<string, string>? Variables { get; set; }

    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
