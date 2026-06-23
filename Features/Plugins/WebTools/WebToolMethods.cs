using System.ComponentModel;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

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

    private static List<(string title, string snippet, string url)> ParseDdgResults(string html)
    {
        var results = new List<(string title, string snippet, string url)>();

        var linkMatches = Regex.Matches(html,
            @"<a[^>]*class=""result__a""[^>]*href=""([^""]+)""[^>]*>([^<]+)</a>",
            RegexOptions.Singleline);
        var snippetMatches = Regex.Matches(html,
            @"<a[^>]*class=""result__snippet""[^>]*>([^<]*(?:<[^/][^>]*>[^<]*</[^>]*>[^<]*)*)</a>",
            RegexOptions.Singleline);

        for (int i = 0; i < linkMatches.Count && i < 10; i++)
        {
            var url = linkMatches[i].Groups[1].Value;
            var title = linkMatches[i].Groups[2].Value;
            var snippet = i < snippetMatches.Count
                ? StripTags(snippetMatches[i].Groups[1].Value)
                : "";

            if (!string.IsNullOrWhiteSpace(title))
                results.Add((title.Trim(), snippet.Trim(), url.Trim()));
        }

        return results;
    }

    private static string HtmlDecode(string text) =>
        HttpUtility.HtmlDecode(text).Trim();

    private static string StripTags(string html) =>
        Regex.Replace(html, @"<[^>]+>", "");
}
