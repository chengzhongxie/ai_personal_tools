using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PersonalAssistant.Core.Interfaces;

namespace PersonalAssistant.Features.Plugins.WebTools;

/// <summary>
/// Web 工具插件：提供 web_fetch、web_search 两个 AI 工具。
/// 资源成本：1个单例 + 2个静态 HttpClient，空闲零开销。
/// </summary>
public class WebToolsPlugin : IToolPlugin
{
    public string Name => "WebTools";
    public string Description => "提供 2 个 Web 工具：网页内容抓取 和 DuckDuckGo 搜索引擎查询";

    public AIFunction[] GetTools()
    {
        return new[]
        {
            AIFunctionFactory.Create(new Func<string, Task<string>>(WebFetch), name: "web_fetch"),
            AIFunctionFactory.Create(new Func<string, Task<string>>(WebSearch), name: "web_search"),
        };
    }

    public async Task<string?> TryExecuteToolAsync(string toolName, string args)
    {
        return toolName switch
        {
            "web_fetch" => await WebToolMethods.WebFetch(args),
            "web_search" => await WebToolMethods.WebSearch(args),
            _ => null
        };
    }

    public string? GetPromptFragment() => null;

    [Description("Fetch and return text content from a URL")]
    private static Task<string> WebFetch(
        [Description("The URL to fetch content from")] string url) =>
        WebToolMethods.WebFetch(url);

    [Description(
        "Search the web using DuckDuckGo (free, no API key needed).\n" +
        "Returns top 10 results with title, snippet, and URL.\n" +
        "Use for current events, documentation lookup, or any info beyond your knowledge cutoff.")]
    private static Task<string> WebSearch(
        [Description("Search query")] string query) =>
        WebToolMethods.WebSearch(query);
}
