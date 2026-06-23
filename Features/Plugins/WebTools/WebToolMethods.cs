using System.ComponentModel;
using System.Net.Http;
using System.Text;
using System.Web;
using HtmlAgilityPack;

namespace PersonalAssistant.Features.Plugins.WebTools;

/// <summary>
/// Web 工具方法静态实现：web_fetch、web_search
/// </summary>
internal static class WebToolMethods
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private static readonly HttpClient _ddgClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36" } },
        Timeout = TimeSpan.FromSeconds(10)
    };

    [Description("Fetch and return text content from a URL")]
    public static async Task<string> WebFetch(
        [Description("The URL to fetch content from")] string url)
    {
        try
        {
            var response = await _httpClient.GetStringAsync(url);
            if (response.Length > 8000) response = response[..8000] + "\n... (已截断)";
            return response;
        }
        catch (Exception ex) { return $"抓取网页出错: {ex.Message}"; }
    }

    [Description(
        "Search the web using DuckDuckGo (free, no API key needed).\n" +
        "Returns top 10 results with title, snippet, and URL.\n" +
        "Use for current events, documentation lookup, or any info beyond your knowledge cutoff.")]
    public static async Task<string> WebSearch(
        [Description("Search query")] string query)
    {
        try
        {
            var encoded = HttpUtility.UrlEncode(query);
            var url = $"https://html.duckduckgo.com/html/?q={encoded}";

            var html = await _ddgClient.GetStringAsync(url);

            var results = ParseDdgResults(html);
            if (results.Count == 0)
                return $"未找到相关结果。查询: {query}";

            var sb = new StringBuilder();
            sb.AppendLine($"搜索: {query}");
            sb.AppendLine();
            for (int i = 0; i < results.Count; i++)
            {
                var (title, snippet, link) = results[i];
                sb.AppendLine($"{i + 1}. {HtmlDecode(title)}");
                sb.AppendLine($"   {HtmlDecode(snippet)}");
                sb.AppendLine($"   {link}");
                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }
        catch (TaskCanceledException)
        {
            return "搜索超时 (10秒)。请稍后再试或缩短查询词。";
        }
        catch (Exception ex)
        {
            return $"搜索出错: {ex.Message}";
        }
    }

    /// <summary>
    /// 使用 HtmlAgilityPack 解析 DuckDuckGo 搜索结果。
    /// 比正则表达式更健壮，正确处理格式错误的 HTML。
    /// </summary>
    private static List<(string title, string snippet, string url)> ParseDdgResults(string html)
    {
        var results = new List<(string title, string snippet, string url)>();

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var resultNodes = doc.DocumentNode.SelectNodes(
            "//div[contains(@class,'result__body')]");

        if (resultNodes is null)
            return results;

        foreach (var node in resultNodes)
        {
            if (results.Count >= 10)
                break;

            var linkNode = node.SelectSingleNode(".//a[contains(@class,'result__a')]");
            var snippetNode = node.SelectSingleNode(".//a[contains(@class,'result__snippet')]");

            var url = linkNode?.GetAttributeValue("href", "");
            var title = linkNode?.InnerText.Trim() ?? "";
            var snippet = snippetNode?.InnerText.Trim() ?? "";

            if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(url))
                results.Add((title, snippet, url));
        }

        return results;
    }

    private static string HtmlDecode(string text) =>
        HttpUtility.HtmlDecode(text).Trim();
}
