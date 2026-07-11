using System.ComponentModel;
using System.Net.Http;
using System.Text;
using System.Web;
using AngleSharp;
using AngleSharp.Dom;
using PersonalAssistant.Core.Services;
using Polly;
using Polly.Retry;

namespace PersonalAssistant.Features.Plugins.WebTools;

/// <summary>
/// Web 工具方法：web_fetch、web_search
/// AngleSharp 解析 HTML，Polly 管理 HTTP 重试，Humanizer 格式化输出。
/// </summary>
internal static class WebToolMethods
{
    private static readonly HttpClient _httpClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36" } },
        Timeout = TimeSpan.FromSeconds(15)
    };

    private static readonly HttpClient _ddgClient = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36" } },
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static readonly ResiliencePipeline _retryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 2,
            Delay = TimeSpan.FromSeconds(1),
            BackoffType = DelayBackoffType.Exponential,
            ShouldHandle = new PredicateBuilder()
                .Handle<HttpRequestException>()
                .Handle<TaskCanceledException>()
        })
        .Build();

    private static PluginSharedState? _sharedState;
    public static void SetSharedState(PluginSharedState sharedState) => _sharedState = sharedState;

    [Description("Fetch and return text content from a URL")]
    public static async Task<string> WebFetch(
        [Description("The URL to fetch content from")] string url)
    {
        if (_sharedState?.IsOffline == true)
            return "当前离线，网页抓取不可用";

        try
        {
            return await _retryPipeline.ExecuteAsync(async _ =>
            {
                var response = await _httpClient.GetStringAsync(url);
                if (response.Length > 8000) response = response[..8000] + "\n... (已截断)";
                return response;
            });
        }
        catch (Exception ex)
        {
            return $"抓取网页出错: {ex.Message}";
        }
    }

    [Description(
        "Search the web using DuckDuckGo (free, no API key needed).\n" +
        "Returns top 10 results with title, snippet, and URL.\n" +
        "Use for current events, documentation lookup, or any info beyond your knowledge cutoff.")]
    public static async Task<string> WebSearch(
        [Description("Search query")] string query)
    {
        if (_sharedState?.IsOffline == true)
            return "当前离线，网页搜索不可用";

        try
        {
            return await _retryPipeline.ExecuteAsync(async _ =>
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
                    sb.AppendLine($"{i + 1}. {HttpUtility.HtmlDecode(title).Trim()}");
                    sb.AppendLine($"   {HttpUtility.HtmlDecode(snippet).Trim()}");
                    sb.AppendLine($"   {link}");
                    sb.AppendLine();
                }

                return sb.ToString().TrimEnd();
            });
        }
        catch (Exception ex)
        {
            return $"搜索出错: {ex.Message}";
        }
    }

    /// <summary>AngleSharp 解析 DuckDuckGo 搜索结果</summary>
    private static List<(string title, string snippet, string url)> ParseDdgResults(string html)
    {
        var results = new List<(string title, string snippet, string url)>();
        var config = Configuration.Default;
        var ctx = BrowsingContext.New(config);
        var doc = ctx.OpenAsync(req => req.Content(html)).Result;

        var resultNodes = doc.QuerySelectorAll(".result__body");
        foreach (var node in resultNodes)
        {
            if (results.Count >= 10) break;

            var linkNode = node.QuerySelector(".result__a");
            var snippetNode = node.QuerySelector(".result__snippet");

            var url = linkNode?.GetAttribute("href") ?? "";
            var title = linkNode?.TextContent.Trim() ?? "";
            var snippet = snippetNode?.TextContent.Trim() ?? "";

            if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(url))
                results.Add((title, snippet, url));
        }

        return results;
    }
}
