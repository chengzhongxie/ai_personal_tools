namespace PersonalAssistant.Features.Workflow.Models;

/// <summary>
/// 单条工具调用记录，由 WorkflowRecorder 在工具执行时生成
/// </summary>
public class ToolCallRecord
{
    /// <summary>工具名称（read_file / write_file / list_files / web_fetch / run_shell）</summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>工具参数摘要（路径、URL 或命令前 200 字符）。支持 ${var} 变量引用。</summary>
    public string Args { get; set; } = string.Empty;

    /// <summary>此步骤输出的变量名（null 表示不输出变量）</summary>
    public string? OutputVariable { get; set; }

    /// <summary>执行条件表达式：如 '$result == "success"'，null 表示无条件执行</summary>
    public string? Condition { get; set; }

    /// <summary>调用时间戳</summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
