using System.Text.RegularExpressions;
using Serilog;

namespace PersonalAssistant.Features.Chat.Services;

/// <summary>
/// 模型路由服务：使用本地小模型做语义级意图分类，精准判断走本地还是远程。
///
/// 算法流程（3 层漏斗）：
///   1. 快速预判：极短消息、纯表情、明显需工具的关键词 → 快速路由
///   2. 意图分类：本地 Qwen 0.5B 语义分析 → "conversation"/"question" → 本地 | "action"/"creation"/"system" → 远程
///   3. 质量评估：本地回答后检查质量，不合格自动回退远程
///
/// 资源成本：空闲时零开销。分类阶段用 MaxTokens=10，约 1-2s 完成。
/// </summary>
public class ModelRoutingService
{
    private readonly LocalModelService _localModel;

    /// <summary>意图分类用的系统提示词 — 精准区分"纯对话/知识问答"和"需要工具的操作"</summary>
    private const string IntentClassifierPrompt =
        "You are an intent classifier. Analyze the user message and output exactly ONE label:\n" +
        "- conversation: casual chat, greeting, thanks, chitchat, emotional expression\n" +
        "- question: asking for factual knowledge, explanation, advice, recommendation, opinion, translation, summary, definition\n" +
        "- action: requesting to DO something on the computer (open apps, search files, manage windows, run commands, control system, send keys, manage clipboard, take screenshot)\n" +
        "- creation: requesting to CREATE/SAVE/WRITE files, code, documents, or data to disk\n" +
        "- system: requesting system automation (schedule tasks, manage workflows, set timers)\n\n" +
        "IMPORTANT rules:\n" +
        "- 'write a poem' = question (generating text, not saving to file)\n" +
        "- 'write a poem and save it' = creation (explicitly saving)\n" +
        "- 'recommend a movie' = question\n" +
        "- 'search for movies' = action (needs web search)\n" +
        "- 'what time is it' = question (just asking, not setting)\n" +
        "- 'set an alarm for 8am' = system (scheduling)\n" +
        "- 'tell me a joke' = conversation\n" +
        "- 'open notepad' = action\n" +
        "- 'explain quantum physics' = question\n\n" +
        "Output ONLY the label, nothing else.";

    /// <summary>质量检查用的系统提示词 — 判断回答是否合格</summary>
    private const string QualityCheckPrompt =
        "You are a quality evaluator. Given a user question and an AI response, " +
        "evaluate if the response adequately answers the question.\n" +
        "Output ONLY 'YES' or 'NO'.\n" +
        "A response is INADEQUATE (NO) if:\n" +
        "- It says 'I don't know', 'I cannot', '无法', '不知道' or similar\n" +
        "- It's nonsensical, completely off-topic, or repetitive gibberish\n" +
        "- It's an error message rather than an answer\n" +
        "A response is ADEQUATE (YES) even if imperfect — as long as it makes a genuine attempt to answer.\n" +
        "Output ONLY 'YES' or 'NO', no other text.";

    /// <summary>快速预判：极短消息或纯表情 → 无需分类，直接本地</summary>
    private static readonly Regex TrivialMessagePattern = new(
        @"^[\s\p{So}\u0021-\u002F\u003A-\u0040\u005B-\u0060\u007B-\u007E]{0,5}$",
        RegexOptions.Compiled);

    /// <summary>快速预判：明显需要工具的关键词（减少不必要的分类延迟）</summary>
    private static readonly Regex ObviousActionPattern = new(
        @"\b(打开|关闭|启动|运行|执行|搜索|查找|删除|创建|保存|写入|截屏|截图|" +
        @"发送按键|输入文本|窗口.*(切换|聚焦|最大化|最小化)|剪切板|剪贴板|" +
        @"(添加|新建|创建).*(定时|计划|提醒|闹钟|任务)|(删除|移除).*(定时|计划|工作流|任务)|" +
        @"open|close|launch|start|run|execute|search|find|delete|remove|create|save|write|" +
        @"screenshot|send keys|type text|clipboard|schedule|task)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public ModelRoutingService(LocalModelService localModel)
    {
        _localModel = localModel;
    }

    /// <summary>
    /// 判断用户消息是否应该先尝试本地模型。
    /// 返回 true = 先试本地，false = 直接远程。
    /// </summary>
    public bool ShouldTryLocal(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return true;

        // Layer 1: 极短/纯表情 → 直接本地（连分类都省了）
        if (TrivialMessagePattern.IsMatch(message.Trim()))
            return true;

        // Layer 1: 明显需工具 → 直接远程（省分类延迟）
        if (ObviousActionPattern.IsMatch(message))
            return false;

        // Layer 2: 交给意图分类器决定
        return true;
    }

    /// <summary>
    /// 意图分类：用本地模型做语义级分析，返回意图标签。
    /// 使用 MaxTokens=10，约 1-2 秒完成。
    /// </summary>
    public async Task<string> ClassifyIntentAsync(string message)
    {
        try
        {
            var result = await _localModel.InferAsync(
                prompt: $"User message: \"{message}\"",
                maxTokens: 10,
                systemPrompt: IntentClassifierPrompt);

            return NormalizeLabel(result);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[ModelRouting] 意图分类失败，默认走远程");
            return "action"; // 保守策略：分类失败 → 远程
        }
    }

    /// <summary>
    /// 判断意图是否适合本地模型处理。
    /// </summary>
    public static bool IsLocalIntent(string intent)
    {
        return intent is "conversation" or "question";
    }

    /// <summary>
    /// 尝试用本地模型回答（完整推理，MaxTokens=256）。
    /// 返回 (回复, 质量是否合格)。
    /// </summary>
    public async Task<(string response, bool isAdequate)> TryLocalAsync(string message)
    {
        try
        {
            var response = await _localModel.InferAsync(message);
            var adequate = await EvaluateQualityAsync(message, response);
            if (!adequate)
                Log.Debug("[ModelRouting] 本地回答质量差，回退远程: {Msg}",
                    message.Truncate(80));
            return (response, adequate);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[ModelRouting] 本地推理失败，回退远程");
            return (ex.Message, false);
        }
    }

    /// <summary>
    /// 质量评估：用本地模型判断回答是否合格（MaxTokens=5，极快）。
    /// 先做快速结构检查，再调用语义判断。
    /// </summary>
    private async Task<bool> EvaluateQualityAsync(string question, string response)
    {
        // 快速结构检查（微秒级）
        if (string.IsNullOrWhiteSpace(response))
            return false;
        var trimmed = response.Trim();
        if (trimmed.Length < 5)
            return false;
        if (trimmed.Contains("本地模型文件未找到") || trimmed.Contains("模型初始化失败"))
            return false;
        if (trimmed.Contains("本地模型服务已释放"))
            return false;
        if (HasExcessiveRepetition(trimmed))
            return false;

        // 语义质量检查（本地模型，MaxTokens=5，~1s）
        try
        {
            var qCheckResult = await _localModel.InferAsync(
                prompt: $"Question: {question.Truncate(200)}\nResponse: {trimmed.Truncate(300)}",
                maxTokens: 5,
                systemPrompt: QualityCheckPrompt);

            var normalized = qCheckResult.Trim().ToUpperInvariant();
            return normalized.StartsWith("YES");
        }
        catch
        {
            // 质量检查失败 → 保守起见，认为合格（至少结构检查过了）
            return true;
        }
    }

    /// <summary>
    /// 检测文本中是否有高度重复的内容（模型崩溃的典型症状）。
    /// </summary>
    private static bool HasExcessiveRepetition(string text)
    {
        if (text.Length < 30)
            return false;
        var sample = text[..Math.Min(15, text.Length)];
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(sample, idx, StringComparison.Ordinal)) != -1)
        {
            count++;
            idx += sample.Length;
            if (count >= 4)
                return true;
        }
        return false;
    }

    /// <summary>标准化意图标签，容错处理</summary>
    private static string NormalizeLabel(string raw)
    {
        var trimmed = raw.Trim().ToLowerInvariant();

        // 精确匹配
        if (trimmed == "conversation" || trimmed.StartsWith("conversation")) return "conversation";
        if (trimmed == "question" || trimmed.StartsWith("question")) return "question";
        if (trimmed == "action" || trimmed.StartsWith("action")) return "action";
        if (trimmed == "creation" || trimmed.StartsWith("creation")) return "creation";
        if (trimmed == "system" || trimmed.StartsWith("system")) return "system";

        // 模糊匹配（模型可能输出中文或其他变体）
        if (trimmed.Contains("conversation") || trimmed.Contains("chat") || trimmed.Contains("对话") || trimmed.Contains("聊天") || trimmed.Contains("闲聊"))
            return "conversation";
        if (trimmed.Contains("question") || trimmed.Contains("ask") || trimmed.Contains("问题") || trimmed.Contains("提问") || trimmed.Contains("询问"))
            return "question";
        if (trimmed.Contains("action") || trimmed.Contains("操作") || trimmed.Contains("执行") || trimmed.Contains("打开") || trimmed.Contains("运行"))
            return "action";
        if (trimmed.Contains("creation") || trimmed.Contains("创建") || trimmed.Contains("保存") || trimmed.Contains("写入") || trimmed.Contains("生成"))
            return "creation";
        if (trimmed.Contains("system") || trimmed.Contains("系统") || trimmed.Contains("定时") || trimmed.Contains("调度"))
            return "system";

        // 无法识别 → 保守走远程
        Log.Debug("[ModelRouting] 无法识别的意图标签: {Label}，默认走远程", raw.Truncate(50));
        return "action";
    }
}

/// <summary>
/// 字符串扩展方法
/// </summary>
file static class StringExtensions
{
    public static string Truncate(this string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";
}
