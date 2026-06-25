using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace PersonalAssistant.Features.Plugins.Services;

/// <summary>
/// 插件市场服务：从 GitHub Gist 搜索公开插件。
/// 按需消耗：仅打开市场窗口时发起 HTTP 请求，缓存 1 小时。
/// </summary>
public class PluginMarketplaceService
{
    private readonly HttpClient _http;
    private List<MarketPluginInfo>? _cached;
    private DateTime _cacheTime = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    public PluginMarketplaceService()
    {
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
            DefaultRequestHeaders = { { "User-Agent", "PersonalAssistant" } }
        };
    }

    public async Task<List<MarketPluginInfo>> SearchPluginsAsync()
    {
        if (_cached is not null && DateTime.Now - _cacheTime < CacheDuration)
            return _cached;

        try
        {
            // Search GitHub for gists tagged as personal-assistant-plugin
            var url = "https://api.github.com/gists?per_page=50";
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("[PluginMarketplace] GitHub API 返回 {Code}", response.StatusCode);
                return _cached ?? new();
            }

            var gists = await response.Content.ReadFromJsonAsync<List<GitHubGist>>();
            if (gists is null)
                return _cached ?? new();

            var plugins = new List<MarketPluginInfo>();
            foreach (var gist in gists)
            {
                // Only include gists with .cs files and matching description
                var csFile = gist.Files?
                    .Where(f => f.Key.EndsWith(".cs") ||
                                (f.Value?.Filename?.EndsWith(".cs") ?? false))
                    .Select(f => f.Value)
                    .FirstOrDefault();
                if (csFile is null) continue;

                var desc = gist.Description ?? "";
                if (!desc.Contains("[personal-assistant-plugin]")) continue;

                var owner = gist.Owner?.Login ?? "unknown";
                plugins.Add(new MarketPluginInfo
                {
                    Name = Path.GetFileNameWithoutExtension(csFile.Filename ?? ""),
                    Description = desc.Replace("[personal-assistant-plugin]", "").Trim(),
                    Author = owner,
                    DownloadUrl = csFile.RawUrl ?? "",
                    GistUrl = gist.HtmlUrl ?? "",
                    FileName = csFile.Filename ?? ""
                });
            }

            _cached = plugins;
            _cacheTime = DateTime.Now;
            Log.Information("[PluginMarketplace] 搜索完成，找到 {Count} 个插件", plugins.Count);
            return plugins;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[PluginMarketplace] 搜索失败");
            return _cached ?? new();
        }
    }

    public async Task<string> DownloadPluginAsync(MarketPluginInfo plugin)
    {
        var source = await _http.GetStringAsync(plugin.DownloadUrl);

        var pluginDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PersonalAssistant", "Plugins");
        if (!Directory.Exists(pluginDir))
            Directory.CreateDirectory(pluginDir);

        var dest = Path.Combine(pluginDir, plugin.FileName);
        await File.WriteAllTextAsync(dest, source);
        return dest;
    }

    private class GitHubGist
    {
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("owner")]
        public GitHubOwner? Owner { get; set; }

        [JsonPropertyName("files")]
        public Dictionary<string, GitHubGistFile>? Files { get; set; }
    }

    private class GitHubOwner
    {
        [JsonPropertyName("login")]
        public string? Login { get; set; }
    }

    private class GitHubGistFile
    {
        [JsonPropertyName("filename")]
        public string? Filename { get; set; }

        [JsonPropertyName("raw_url")]
        public string? RawUrl { get; set; }
    }
}

public class MarketPluginInfo
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Author { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string GistUrl { get; set; } = "";
    public string FileName { get; set; } = "";
}
