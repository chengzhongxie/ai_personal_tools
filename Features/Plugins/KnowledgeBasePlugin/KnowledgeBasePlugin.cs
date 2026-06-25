using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.AI;
using PersonalAssistant.Core.Interfaces;
using PersonalAssistant.Features.KnowledgeBase.Services;

namespace PersonalAssistant.Features.Plugins.KnowledgeBasePlugin;

/// <summary>
/// 知识库插件：提供 knowledge_search 工具，在用户本地文档中搜索。
/// </summary>
public class KnowledgeBasePlugin : IToolPlugin
{
    private readonly KnowledgeBaseService _kbService;

    public string Name => "KnowledgeBase";
    public string? GetPromptFragment() =>
        "You have a knowledge_search tool to search through the user's local documents.\n" +
        "Use this when the user asks about their own notes, documents, or local files.\n" +
        "The user must first index their documents via the Settings window (知识库 tab).\n" +
        "If no index exists, tell the user to go to Settings → 知识库 to index their documents first.";

    public KnowledgeBasePlugin(KnowledgeBaseService kbService)
    {
        _kbService = kbService;
    }

    public AIFunction[] GetTools()
    {
        return new[]
        {
            AIFunctionFactory.Create(KnowledgeSearch)
        };
    }

    public Task<string?> TryExecuteToolAsync(string toolName, string args)
    {
        if (!string.Equals(toolName, "knowledge_search", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<string?>(null);

        return Task.FromResult<string?>(KnowledgeSearch(args));
    }

    [Description(
        "Search through the user's locally indexed documents (knowledge base).\n" +
        "Use this when asked to find information in the user's own notes, documents, or project files.\n" +
        "Returns the most relevant text chunks from indexed documents.\n" +
        "Parameters:\n" +
        "  query: the search query or question")]
    private string KnowledgeSearch(
        [Description("Search query to find relevant information in local documents")] string query)
    {
        if (!_kbService.IsIndexed)
            return "知识库尚未建立索引。请在设置 → 知识库 中选择文档目录并建立索引。";

        var results = _kbService.Search(query, topK: 5);
        if (results.Count == 0)
            return $"在已索引的文档中未找到与 \"{query}\" 相关的内容。";

        var sb = new StringBuilder();
        sb.AppendLine($"找到 {results.Count} 个相关结果:");
        sb.AppendLine();

        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i];
            var fileName = System.IO.Path.GetFileName(r.Chunk.SourceFile);
            sb.AppendLine($"### [{i + 1}] {fileName} (相关度: {r.Score:F2})");
            sb.AppendLine(r.Preview);
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
