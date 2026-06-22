namespace PersonalAssistant.Features.Workflow.Models;

/// <summary>
/// 单条工具调用记录，由 WorkflowRecorder 在工具执行时生成
/// </summary>
public class ToolCallRecord
{
    /// <summary>工具名称（read_file / write_file / list_files / web_fetch / run_shell）</summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>工具参数摘要（路径、URL 或命令前 200 字符）</summary>
    public string Args { get; set; } = string.Empty;

    /// <summary>调用时间戳</summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
