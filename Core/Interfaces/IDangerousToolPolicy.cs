namespace PersonalAssistant.Core.Interfaces;

/// <summary>
/// 危险操作确认策略接口。
/// 解耦 MessageBox UI 与插件层，由 ChatViewModel 设置确认回调。
/// </summary>
public interface IDangerousToolPolicy
{
    /// <summary>
    /// 高危工具执行前的用户确认回调。
    /// 参数：工具名、参数摘要。返回 true 表示同意执行，false 表示拒绝。
    /// 由 ChatViewModel 设置为弹窗确认。
    /// </summary>
    Func<string, string, bool>? DangerConfirmation { get; set; }

    /// <summary>检查是否需要弹窗确认，不需要则直接放行</summary>
    bool ConfirmDangerous(string toolName, string argsSummary);

    /// <summary>检查指定工具是否为高危工具</summary>
    bool IsDangerous(string toolName);
}
