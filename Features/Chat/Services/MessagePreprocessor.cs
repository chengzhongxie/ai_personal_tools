using System.Text;
using System.Text.RegularExpressions;
using PersonalAssistant.Core.Interfaces;
using PersonalAssistant.Features.Clipboard.Services;
using PersonalAssistant.Features.Plugins.WebTools;
using Serilog;

namespace PersonalAssistant.Features.Chat.Services;

/// <summary>
/// 消息预处理管线：在发送给 AI 之前，依次执行本地拦截、本地计算、
/// 主动插件触发（IProactivePlugin）、实时查询预搜索。
///
/// 职责：决定消息是否需要 AI，以及发给 AI 的文本内容。
/// ChatViewModel 不再包含这些逻辑，防止其持续膨胀。
///
/// 资源成本：按需消耗（仅在用户发送消息时运行），空闲时零开销。
/// </summary>
public class MessagePreprocessor
{
    private readonly LocalCommandInterceptor _localCmd;
    private readonly IReadOnlyList<IProactivePlugin> _proactivePlugins;
    private const int MaxPrefetchChars = 3000;

    public MessagePreprocessor(LocalCommandInterceptor localCmd, IEnumerable<IToolPlugin> plugins)
    {
        _localCmd = localCmd;
        _proactivePlugins = plugins.OfType<IProactivePlugin>().ToList();
    }

    /// <summary>
    /// 预处理结果
    /// </summary>
    /// <param name="AiInput">发给 AI 的文本（可能与用户原始输入不同）</param>
    /// <param name="HandledLocally">是否已在本地处理完毕，不需要 AI</param>
    /// <param name="LocalResponse">本地处理的结果（HandledLocally=true 时有效）</param>
    public readonly record struct Result(string AiInput, bool HandledLocally, string? LocalResponse);

    /// <summary>
    /// 执行完整的预处理管线。返回处理结果。
    /// </summary>
    public async Task<Result> ProcessAsync(string text)
    {
        // 1. 本地命令拦截（40+ 条确定性系统指令）
        var localResult = _localCmd.TryIntercept(text);
        if (localResult is not null)
            return new Result(text, true, localResult);

        // 2. 本地计算/日期查询（纯数学、日期时间）
        var computeResult = TryComputeLocally(text);
        if (computeResult is not null)
            return new Result(text, true, computeResult);

        // 3. 主动插件触发（IProactivePlugin）
        var pluginResult = await TryProactivePluginsAsync(text);
        if (pluginResult is not null)
            return pluginResult.Value;

        // 4. 实时查询预搜索（非插件覆盖的新闻/股票等）
        var searchResult = await TryRealtimeSearchAsync(text);
        if (searchResult is not null)
            return new Result(searchResult, false, null);

        // 无需预处理，原样发送
        return new Result(text, false, null);
    }

    // ═══════════════════════════════════════════════
    // 主动插件循环
    // ═══════════════════════════════════════════════

    private async Task<Result?> TryProactivePluginsAsync(string text)
    {
        foreach (var plugin in _proactivePlugins)
        {
            if (plugin.IntentPattern?.IsMatch(text) != true)
                continue;

            try
            {
                var pr = await plugin.ExecuteProactivelyAsync(text);
                if (pr is not { Text: var data, BypassAI: var bypass } || string.IsNullOrWhiteSpace(data))
                    continue;

                if (bypass)
                {
                    // 插件输出已是完整自然语言 → 直接展示，零 AI token
                    Log.Debug("[Preprocessor] 主动插件 {Plugin} → 绕过 AI，直接输出", plugin.GetType().Name);
                    return new Result(data, true, data);
                }

                // 注入 AI 上下文，让 AI 格式化
                var wrapped = "请把以下信息完整转告用户，不要遗漏任何内容：\n\n" + data;
                return new Result(wrapped, false, null);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[Preprocessor] 主动插件 {Plugin} 失败", plugin.GetType().Name);
            }
        }
        return null;
    }

    // ═══════════════════════════════════════════════
    // 通用实时查询预搜索 (DuckDuckGo)
    // ═══════════════════════════════════════════════

    private static async Task<string?> TryRealtimeSearchAsync(string text)
    {
        if (!ModelRoutingService.IsRealTimeQuery(text))
            return null;

        try
        {
            var results = await WebToolMethods.WebSearch(text);
            if (string.IsNullOrWhiteSpace(results) ||
                results.StartsWith("当前离线") ||
                results.StartsWith("未找到相关结果"))
                return null;

            if (results.Length > MaxPrefetchChars)
                results = results[..MaxPrefetchChars] + "\n... (已截断)";

            var sb = new StringBuilder();
            sb.AppendLine("[系统] 以下是从搜索引擎获取的最新信息：");
            sb.AppendLine(results);
            sb.AppendLine();
            sb.AppendLine($"用户问题: {text}");
            sb.Append("请根据以上搜索结果回答用户的问题。务必注明信息来源（链接）。");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[Preprocessor] 预搜索失败");
            return null;
        }
    }

    // ═══════════════════════════════════════════════
    // 本地计算/日期查询
    // ═══════════════════════════════════════════════

    private static string? TryComputeLocally(string input)
    {
        var text = input.Trim();

        if (IsDateQuery(text))
            return AnswerDateQuery(text);

        if (ClipboardToolHelper.IsMathExpression(text))
            return ClipboardToolHelper.EvaluateMath(text);

        var calcPrefixes = new[] { "计算 ", "计算:", "算 ", "算一下 ", "等于多少 " };
        foreach (var prefix in calcPrefixes)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var expr = text[prefix.Length..].Trim();
                if (ClipboardToolHelper.IsMathExpression(expr))
                    return $"{expr} = {EvaluateSimple(expr)}";
            }
        }

        return null;
    }

    private static bool IsDateQuery(string text)
    {
        var t = text.ToLowerInvariant().Replace("？", "").Replace("?", "").Trim();
        return t is "今天几号" or "今天日期" or "今天星期几" or "今天周几"
            or "现在几点" or "当前时间" or "几点了" or "现在时间"
            or "今天" or "日期" or "时间" or "星期几" or "周几"
            or "今年是哪年" or "今年" or "几月" or "几号";
    }

    private static string AnswerDateQuery(string text)
    {
        var now = DateTime.Now;
        var t = text.ToLowerInvariant().Replace("？", "").Replace("?", "").Trim();

        if (t.Contains("时间") || t.Contains("几点"))
            return $"现在是 {now:yyyy年M月d日 HH:mm:ss}";
        if (t.Contains("星期几") || t.Contains("周几"))
            return $"今天是 {now:yyyy年M月d日}，星期{GetChineseWeekday(now.DayOfWeek)}";
        if (t.Contains("哪年"))
            return $"今年是 {now.Year} 年";
        if (t.Contains("几月"))
            return $"现在是 {now.Month} 月";
        if (t.Contains("几号") || t.Contains("日期"))
            return $"今天是 {now:yyyy年M月d日}，星期{GetChineseWeekday(now.DayOfWeek)}";

        return $"今天是 {now:yyyy年M月d日}，星期{GetChineseWeekday(now.DayOfWeek)}，{now:HH:mm:ss}";
    }

    private static string GetChineseWeekday(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "一",
        DayOfWeek.Tuesday => "二",
        DayOfWeek.Wednesday => "三",
        DayOfWeek.Thursday => "四",
        DayOfWeek.Friday => "五",
        DayOfWeek.Saturday => "六",
        DayOfWeek.Sunday => "日",
        _ => "?"
    };

    private static string EvaluateSimple(string expr)
    {
        try
        {
            var cleaned = expr.Trim()
                .Replace('×', '*').Replace('÷', '/').Replace("×", "*").Replace("÷", "/")
                .Replace("π", Math.PI.ToString()).Replace("pi", Math.PI.ToString())
                .Replace("Pi", Math.PI.ToString());

            var result = new System.Data.DataTable().Compute(cleaned, null);
            return result.ToString() ?? "计算错误";
        }
        catch { return "无法计算"; }
    }
}
