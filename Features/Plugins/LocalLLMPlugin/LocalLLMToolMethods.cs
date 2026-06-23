using System.ComponentModel;
using PersonalAssistant.Features.Chat.Services;

namespace PersonalAssistant.Features.Plugins.LocalLLMPlugin;

/// <summary>
/// 本地 LLM 工具方法静态实现：local_llm
/// </summary>
internal static class LocalLLMToolMethods
{
    [Description("Run inference on a small local LLM (Qwen2.5-0.5B, free, zero remote token).\n" +
        "Use for: text summarization, keyword extraction, simple translation, " +
        "sentiment analysis, spell checking, basic Q&A about provided text.\n" +
        "Do NOT use for: complex reasoning, creative writing, code generation, " +
        "anything requiring external tools or up-to-date knowledge.")]
    public static async Task<string> LocalLlm(
        [Description("The prompt to send to the local model")] string prompt,
        LocalModelService localModel)
    {
        try
        {
            return await localModel.InferAsync(prompt);
        }
        catch (Exception ex)
        {
            return $"本地模型推理出错: {ex.Message}";
        }
    }
}
