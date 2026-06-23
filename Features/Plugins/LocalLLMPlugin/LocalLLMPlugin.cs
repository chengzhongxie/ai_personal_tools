using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PersonalAssistant.Core.Interfaces;
using PersonalAssistant.Features.Chat.Services;

namespace PersonalAssistant.Features.Plugins.LocalLLMPlugin;

/// <summary>
/// 本地 LLM 插件：提供 local_llm 工具（Qwen2.5-0.5B，零 token 消耗）。
/// 资源成本：1个单例，首次推理时加载 ~550MB 内存，空闲时零 CPU。
/// </summary>
public class LocalLLMPlugin : IToolPlugin
{
    private readonly LocalModelService _localModel;

    public string Name => "LocalLLM";

    public LocalLLMPlugin(LocalModelService localModel)
    {
        _localModel = localModel;
    }

    public AIFunction[] GetTools()
    {
        return new[]
        {
            AIFunctionFactory.Create(new Func<string, Task<string>>(LocalLlm), name: "local_llm"),
        };
    }

    public async Task<string?> TryExecuteToolAsync(string toolName, string args)
    {
        if (toolName == "local_llm")
            return await LocalLLMToolMethods.LocalLlm(args, _localModel);

        return null;
    }

    public string? GetPromptFragment() => null;

    [Description("Run inference on a small local LLM (Qwen2.5-0.5B, free, zero remote token).\n" +
        "Use for: text summarization, keyword extraction, simple translation, " +
        "sentiment analysis, spell checking, basic Q&A about provided text.\n" +
        "Do NOT use for: complex reasoning, creative writing, code generation, " +
        "anything requiring external tools or up-to-date knowledge.")]
    private Task<string> LocalLlm(
        [Description("The prompt to send to the local model")] string prompt) =>
        LocalLLMToolMethods.LocalLlm(prompt, _localModel);
}
