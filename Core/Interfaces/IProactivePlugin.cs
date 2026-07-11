using System.Text.RegularExpressions;

namespace PersonalAssistant.Core.Interfaces;

/// <summary>
/// 主动插件接口：插件不仅作为 AI 工具可用，还可以在发送消息前
/// 被代码层自动触发（不依赖 AI 的工具调用决策）。
///
/// 两种工作模式：
///   1. BypassAI = true  → 插件输出直接展示给用户，零 AI token
///      适用：输出已是完整自然语言（如天气报告含全部建议）
///   2. BypassAI = false → 插件输出注入 AI 上下文，AI 格式化后回复
///      适用：需要 AI 理解/筛选/个性化处理的原始数据
///
/// 工作流程：
///   用户消息 → MessagePreprocessor 检查 IntentPattern
///   → 匹配则调 ExecuteProactivelyAsync
///   → BypassAI 则直接展示 / 否则注入 AI 上下文
///
/// 插件开发者只需关注两个问题：
///   1. IntentPattern — 什么情况下触发？
///   2. ExecuteProactivelyAsync — 返回什么结果？是否需要 AI？
/// 不需要编写 AI prompt 指令（框架统一处理）。
///
/// 实现类通过 DI 自动发现（同 IToolPlugin），无需额外注册。
/// </summary>
public interface IProactivePlugin
{
    /// <summary>
    /// 意图检测正则。用户消息匹配此模式时，系统在发送给 AI 之前主动调用此插件。
    /// 返回 null 表示不主动触发（仅作为普通 AI 工具可用）。
    /// </summary>
    Regex? IntentPattern { get; }

    /// <summary>
    /// 主动执行插件逻辑。
    /// 返回 null 表示不注入（跳过此插件）。
    /// </summary>
    /// <param name="userMessage">用户原始消息</param>
    Task<ProactiveResult?> ExecuteProactivelyAsync(string userMessage);
}

/// <summary>
/// 主动插件执行结果
/// </summary>
/// <param name="Text">输出文本</param>
/// <param name="BypassAI">
///   true  = 文本直接展示给用户，不经过 AI（零 token）
///   false = 文本注入 AI 上下文，由 AI 格式化后回复
/// </param>
public readonly record struct ProactiveResult(string Text, bool BypassAI = false);
