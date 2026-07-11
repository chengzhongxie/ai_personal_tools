using System.ComponentModel;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PersonalAssistant.Core.Interfaces;

namespace PersonalAssistant.Features.Plugins.WeatherPlugin;

/// <summary>
/// 天气插件：提供 get_weather 工具 + IProactivePlugin 主动触发。
/// 覆盖：穿衣、运动、洗车、户外活动、饮食、健康防护。
/// 数据源：wttr.in 免费气象服务，无需 API Key。
/// 资源成本：1个单例，工具调用时 HTTP 请求（~500ms），空闲时零开销。
/// </summary>
public class WeatherPlugin : IToolPlugin, IProactivePlugin
{
    public string Name => "Weather";
    public string Description => "查询实时天气 + 智能衣食住行生活建议（穿衣/运动/洗车/户外/饮食/健康）";

    // ═══════════════════════════════════════════════
    // IProactivePlugin — 主动触发（不依赖 AI 调工具）
    // ═══════════════════════════════════════════════

    private static readonly Regex _weatherPattern = new(
        @"(天气|气温|下雨|下雪|降雨|刮风|雾霾|台风|冰雹|暴雪|雷暴|" +
        @"空气质量|湿度|风力|温度|紫外线|晴|阴天|多云|" +
        @"热不热|冷不冷|热吗|冷吗|闷热|寒冷|炎热|暖和|凉爽|" +
        @"weather|temperature|forecast)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public Regex? IntentPattern => _weatherPattern;

    public async Task<ProactiveResult?> ExecuteProactivelyAsync(string userMessage)
    {
        var city = ExtractCity(userMessage);
        var report = await GetWeatherReportAsync(city);
        if (report is null || report.StartsWith("获取天气数据失败"))
            return null;

        // 报告已是完整自然语言（天气数据 + 6 类生活建议），无需 AI 二次加工。
        // BypassAI = true → 零 token，直接展示。
        return new ProactiveResult(report, BypassAI: true);
    }

    private static string? ExtractCity(string text)
    {
        var m = Regex.Match(text,
            @"(天气|气温|热|冷|下雨|下雪|怎么样|如何|预报|情况|温度|空气质量|湿度|风力|紫外线)");
        if (!m.Success) return null;
        var before = text[..m.Index];
        var timeWords = new[] { "今天", "明天", "后天", "昨天", "本周", "下周", "这周",
            "今晚", "明早", "现在", "今日", "明日", "未来", "最近", "周末", "外面",
            "当地", "本地", "这里", "那里", "的" };
        var cleaned = before;
        foreach (var tw in timeWords.OrderByDescending(t => t.Length))
            cleaned = cleaned.Replace(tw, "");
        var cm = Regex.Match(cleaned.Trim(), @"([一-鿿]{2,4})$");
        return cm.Success ? cm.Groups[1].Value : null;
    }

    // ═══════════════════════════════════════════════
    // IToolPlugin 接口
    // ═══════════════════════════════════════════════

    public AIFunction[] GetTools()
    {
        return new[]
        {
            AIFunctionFactory.Create(new Func<string?, Task<string>>(GetWeatherWrapper), name: "get_weather"),
        };
    }

    public async Task<string?> TryExecuteToolAsync(string toolName, string args)
    {
        if (toolName == "get_weather")
        {
            var city = string.IsNullOrWhiteSpace(args) ? null : args.Trim();
            return await GetWeatherReportAsync(city);
        }
        return null;
    }

    public string? GetPromptFragment()
    {
        return """
        === WEATHER & LIFESTYLE TOOL ===
        get_weather(city?) — get comprehensive weather report with smart lifestyle advice.
          - city: city name in English or Chinese (e.g. "Beijing", "上海"). Leave empty for auto-detect.
          - Returns: current conditions, forecast, AND recommendations:
            👔 Clothing  |  🏃 Sports  |  🚗 Car wash  |  🌳 Outdoor  |  🍜 Food  |  ⚠️ Health
          - USE THIS for ANY weather, temperature, rain, snow, or lifestyle question.
          - Present results in a friendly, personalized way.
        """;
    }

    [Description(
        "Get comprehensive weather report with smart lifestyle recommendations.\n" +
        "Includes: current conditions, forecast, clothing advice, sports suggestions, car wash timing,\n" +
        "outdoor activity ideas, food recommendations, and health alerts.\n" +
        "Use whenever the user asks about weather, temperature, rain, snow, or weather-dependent lifestyle questions.\n" +
        "City: name in English or Chinese (e.g. 'Beijing', '上海'). Leave empty for auto-detect.")]
    private static Task<string> GetWeatherWrapper(
        [Description("City name (English/Chinese). Leave empty for auto-detect.")] string? city = null) =>
        GetWeatherReportAsync(city);

    // ═══════════════════════════════════════════════
    // 天气数据获取 + 智能生活建议引擎
    // ═══════════════════════════════════════════════

    private static readonly HttpClient _client = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "Mozilla/5.0" } },
        Timeout = TimeSpan.FromSeconds(10)
    };

    /// <summary>供 ChatViewModel 预搜索直接调用的公开入口</summary>
    internal static async Task<string> GetWeatherReportAsync(string? city)
    {
        try
        {
            var url = string.IsNullOrWhiteSpace(city)
                ? "https://wttr.in/?format=j1"
                : $"https://wttr.in/{Uri.EscapeDataString(city)}?format=j1";

            var json = await _client.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // 当前天气
            var cur = root.GetProperty("current_condition").GetArrayLength() > 0
                ? root.GetProperty("current_condition")[0] : default;
            var temp = SafeStr(cur, "temp_C");
            var feelsLike = SafeStr(cur, "FeelsLikeC");
            var humidity = SafeStr(cur, "humidity");
            var windSpeed = SafeStr(cur, "windspeedKmph");
            var windDir = SafeStr(cur, "winddir16Point");
            var visibility = SafeStr(cur, "visibility");
            var uvIndex = SafeStr(cur, "uvIndex");
            var weatherDesc = cur.ValueKind != JsonValueKind.Undefined
                ? SafeStr(cur.GetProperty("weatherDesc")[0], "value") : "未知";

            // 预报
            var weatherArr = root.GetProperty("weather");
            string todayDate = "", todayLow = "", todayHigh = "", todayCondition = "";
            string tomorrowDate = "", tomorrowLow = "", tomorrowHigh = "", tomorrowCondition = "";
            int todayRain = 0, tomorrowRain = 0;

            if (weatherArr.GetArrayLength() > 0)
            {
                var today = weatherArr[0];
                todayDate = SafeStr(today, "date");
                todayLow = SafeStr(today, "mintempC"); todayHigh = SafeStr(today, "maxtempC");
                todayCondition = GetDayCondition(today);
                if (today.TryGetProperty("hourly", out var h)) todayRain = GetMaxRainChance(h);
            }
            if (weatherArr.GetArrayLength() > 1)
            {
                var tom = weatherArr[1];
                tomorrowDate = SafeStr(tom, "date");
                tomorrowLow = SafeStr(tom, "mintempC"); tomorrowHigh = SafeStr(tom, "maxtempC");
                tomorrowCondition = GetDayCondition(tom);
                if (tom.TryGetProperty("hourly", out var h)) tomorrowRain = GetMaxRainChance(h);
            }

            // 地区名：用户指定的直接用；自动定位的优先尝试获取中文名
            var areaName = city ?? "未知";
            var isAutoDetected = string.IsNullOrEmpty(city);
            if (isAutoDetected)
            {
                if (root.TryGetProperty("nearest_area", out var a) && a.GetArrayLength() > 0)
                    areaName = SafeStr(a[0].GetProperty("areaName")[0], "value");
                // 加一次快速调用获取中文城市名
                var cnName = await TryGetChineseCityName();
                if (!string.IsNullOrEmpty(cnName))
                    areaName = cnName;
            }

            // 组装报告
            var t = ParseDbl(todayHigh);
            var tLow = ParseDbl(todayLow);
            var humidityPct = ParseDbl(humidity);
            var uv = string.IsNullOrEmpty(uvIndex) ? EstimateUv(weatherDesc) : ParseDbl(uvIndex);
            var wind = ParseDbl(windSpeed);

            var sb = new StringBuilder();
            sb.Append("**📍 ");
            sb.Append(areaName);
            sb.AppendLine(" 天气报告**");
            sb.AppendLine();
            if (isAutoDetected)
            {
                sb.AppendLine($"> 🏠 未指定城市，已根据IP自动定位至 **{areaName}**，如需查其他城市请说「北京天气」");
                sb.AppendLine();
            }

            // 当前实况 — 键值对格式，比表格更兼容
            sb.AppendLine("**🌡 当前实况**");
            sb.AppendLine();
            sb.AppendLine($"🌡 温度：**{temp}°C**（体感 {feelsLike}°C）  ");
            sb.AppendLine($"☁ 天气：{weatherDesc}  ");
            if (!string.IsNullOrEmpty(humidity)) sb.AppendLine($"💧 湿度：{humidity}%  ");
            if (!string.IsNullOrEmpty(windSpeed)) sb.AppendLine($"🌬 风速：{windSpeed} km/h {windDir}  ");
            if (!string.IsNullOrEmpty(visibility)) sb.AppendLine($"👁 能见度：{visibility} km  ");
            if (uv > 0) sb.AppendLine($"☀ UV 指数：{uv:F0}  ");
            sb.AppendLine();

            // 预报
            sb.AppendLine("**📅 预报**");
            sb.AppendLine();
            sb.AppendLine($"> **今日 {todayDate}**　**{todayLow}°C ~ {todayHigh}°C**　{todayCondition}");
            if (todayRain > 0) sb.AppendLine($"> 🌧 降雨概率 **{todayRain}%**");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(tomorrowDate))
            {
                sb.AppendLine($"> **明日 {tomorrowDate}**　**{tomorrowLow}°C ~ {tomorrowHigh}°C**　{tomorrowCondition}");
                if (tomorrowRain > 0) sb.AppendLine($"> 🌧 降雨概率 **{tomorrowRain}%**");
                sb.AppendLine();
            }

            sb.AppendLine("**🎯 智能生活建议**");
            sb.AppendLine();
            sb.AppendLine($"- {Clothing(t, tLow, weatherDesc, wind)}");
            sb.AppendLine($"- {Sports(t, weatherDesc, todayRain, wind)}");
            sb.AppendLine($"- {CarWash(todayRain, tomorrowRain)}");
            sb.AppendLine($"- {Outdoor(t, weatherDesc, uv, todayRain, wind)}");
            sb.AppendLine($"- {Food(t, weatherDesc, humidityPct)}");
            sb.AppendLine($"- {Health(t, uv, humidityPct, weatherDesc, todayRain, wind)}");
            sb.AppendLine();
            sb.AppendLine("> 💡 数据来源: wttr.in ｜ 建议依据温度/湿度/风速/UV/降雨概率综合计算");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"获取天气数据失败: {ex.Message}。请检查网络连接后重试。";
        }
    }

    // ═══════════════════════════════════════════════
    // 生活建议规则引擎
    // ═══════════════════════════════════════════════

    private static string Clothing(double hi, double lo, string cond, double wind)
    {
        var s = "👔 **穿衣**：";
        if (hi > 35) s += "高温！短袖短裤+防晒衣+帽子墨镜，浅色透气棉麻衣物。";
        else if (hi > 30) s += "炎热，短袖短裤+防晒外套，透气吸汗棉质衣物。";
        else if (hi > 25) s += "温暖，短袖或薄长袖，备薄外套应对早晚温差。";
        else if (hi > 20) s += "舒适，长袖T恤或衬衫，早晚加薄外套。";
        else if (hi > 15) s += "稍凉，长袖+薄外套/卫衣，备围巾。";
        else if (hi > 10) s += "偏冷，毛衣/卫衣+夹克/风衣。";
        else if (hi > 5) s += "寒冷，厚毛衣+厚外套/羽绒服+围巾手套。";
        else s += "非常冷！羽绒服+帽子+围巾+手套全套防寒。";
        if (hi - lo > 10) s += $" 温差{hi - lo:F0}°C，洋葱式穿搭。";
        if (cond.Contains("rain", StringComparison.OrdinalIgnoreCase)) s += " 带伞！";
        if (wind > 30) s += " 穿防风外套。";
        return s;
    }

    private static string Sports(double t, string cond, int rain, double wind)
    {
        var s = "🏃 **运动**：";
        if (rain > 50 || cond.Contains("rain", StringComparison.OrdinalIgnoreCase))
            s += "有雨，室内运动：健身房、游泳、瑜伽、跳绳。";
        else if (cond.Contains("snow", StringComparison.OrdinalIgnoreCase))
            s += "下雪天，滑雪⛷或室内运动。";
        else if (wind > 30)
            s += $"风大({wind:F0}km/h)，室内游泳、健身房、瑜伽。";
        else if (t > 35)
            s += "高温！晨跑/夜跑/游泳🏊，避开中午，多补水！";
        else if (t > 30)
            s += "游泳🏊、晨跑、夜跑、骑行、室内球类，注意补水。";
        else if (t > 20)
            s += "黄金温度！跑步🏃、骑行🚴、球类⚽、登山🧗、游泳🏊皆宜。";
        else if (t > 10)
            s += "慢跑、快走、骑行、登山，充分热身。";
        else if (t > 0)
            s += "偏冷，跑步（穿暖）、快走、室内健身、羽毛球。务必热身！";
        else
            s += "寒冷，室内运动：健身、恒温泳池、瑜伽。";
        return s;
    }

    private static string CarWash(int todayRain, int tomorrowRain)
    {
        if (todayRain > 50) return "🚗 **洗车**：❌ 今天降雨概率高，强烈不建议洗车。";
        if (tomorrowRain > 50) return "🚗 **洗车**：⚠️ 明天有雨，过两天再洗。";
        if (todayRain > 20 || tomorrowRain > 20) return "🚗 **洗车**：⚠️ 有降雨可能，可以再等等。";
        return "🚗 **洗车**：✅ 未来两天无雨，适合洗车！洗完打蜡保护车漆。";
    }

    private static string Outdoor(double t, string cond, double uv, int rain, double wind)
    {
        var s = "🌳 **户外**：";
        if (rain > 40 || cond.Contains("rain", StringComparison.OrdinalIgnoreCase))
            s += "有雨不建议户外，可改为博物馆、商场、电影院。";
        else if (t > 35)
            s += "高温预警！避免中午户外，推荐水上乐园🏊、漂流🛶。做好防晒！";
        else if (t > 25 && cond.Contains("sunny", StringComparison.OrdinalIgnoreCase) && rain < 20)
            s += "天气绝佳！郊游🌿、野餐🧺、爬山⛰、骑行🚴、放风筝🪁。记得防晒！";
        else if (t > 15)
            s += "舒适宜人，公园散步、登山、钓鱼🎣、摄影📷。";
        else if (t > 5)
            s += "稍凉但可户外：登山、徒步、骑行，穿暖和。";
        else
            s += "偏冷，短时间户外可，温泉♨、散步。";
        if (uv > 6) s += " ⚠️紫外线强，防晒必备！";
        if (wind > 25) s += " 风大，不宜露营划船。";
        return s;
    }

    private static string Food(double t, string cond, double humidity)
    {
        var s = "🍜 **饮食**：";
        if (t > 30)
            s += "炎热宜清淡：凉拌菜🥗、绿豆汤🫘、西瓜🍉、冷面🍜。少吃油腻辛辣，多补水。";
        else if (t > 20)
        {
            if (cond.Contains("rain", StringComparison.OrdinalIgnoreCase))
                s += "下雨天配热食：馄饨🥟、汤面🍜、小火锅🍲、姜茶☕。";
            else
                s += "气温舒适，均衡饮食即可，多吃时令蔬果。";
        }
        else if (t > 10)
            s += "转凉宜温补：煲汤🍲、炖菜🥘、热粥、姜茶。";
        else
            s += "寒冷需暖身：火锅🍲、羊肉汤🐑、炖牛肉🥩、热巧克力☕。";
        if (humidity > 80) s += " 湿度大，吃点祛湿的：薏米、红豆、冬瓜、山药。";
        return s;
    }

    private static string Health(double t, double uv, double humidity, string cond, int rain, double wind)
    {
        var s = "⚠️ **健康**：";
        var alerts = new List<string>();
        if (t > 35) alerts.Add("🔴 高温警报！避免长时间户外，多喝水，防中暑。");
        else if (t > 30) alerts.Add("🟡 注意防暑，多喝水，避免中午暴晒。");
        if (uv >= 6) alerts.Add("☀ UV指数高，户外涂防晒(SPF30+)、戴帽子墨镜。");
        if (humidity > 85) alerts.Add("💧 湿度很高，注意防潮防霉。");
        if (rain > 50) alerts.Add("🌧 降雨概率高，出门带伞，路面湿滑小心。");
        if (wind > 40) alerts.Add("💨 大风，注意高空坠物。");
        if (t < 5) alerts.Add("🥶 低温注意防冻保暖。");
        if (alerts.Count == 0) s += "无特殊健康风险，保持正常作息。";
        else foreach (var a in alerts) { s += "\n  "; s += a; }
        return s;
    }

    // ═══════════════════════════════════════════════
    // JSON 解析工具
    // ═══════════════════════════════════════════════

    private static string SafeStr(JsonElement el, string prop)
        => el.ValueKind != JsonValueKind.Undefined && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

    private static double ParseDbl(string s) => double.TryParse(s, out var v) ? v : 0;

    private static string GetDayCondition(JsonElement day)
    {
        if (!day.TryGetProperty("hourly", out var hourly)) return "";
        var counts = new Dictionary<string, int>();
        foreach (var h in hourly.EnumerateArray())
        {
            if (h.TryGetProperty("weatherDesc", out var d) && d.GetArrayLength() > 0)
            {
                var desc = SafeStr(d[0], "value");
                if (desc.Length > 0) { counts.TryGetValue(desc, out var c); counts[desc] = c + 1; }
            }
        }
        return counts.Count > 0 ? counts.MaxBy(kv => kv.Value).Key : "";
    }

    private static int GetMaxRainChance(JsonElement hourly)
    {
        var max = 0;
        foreach (var h in hourly.EnumerateArray())
            if (int.TryParse(SafeStr(h, "chanceofrain"), out var p) && p > max) max = p;
        return max;
    }

    private static async Task<string?> TryGetChineseCityName()
    {
        try
        {
            var r = await _client.GetStringAsync("https://wttr.in/?format=4&lang=zh");
            var idx = r.IndexOf(':');
            return idx > 0 ? r[..idx].Trim() : null;
        }
        catch { return null; }
    }

    private static double EstimateUv(string desc)
    {
        var d = desc.ToLowerInvariant();
        if (d.Contains("sunny") || d.Contains("clear")) return 7;
        if (d.Contains("partly cloudy")) return 4;
        if (d.Contains("cloudy") || d.Contains("overcast")) return 2;
        if (d.Contains("rain")) return 1;
        return 3;
    }
}
