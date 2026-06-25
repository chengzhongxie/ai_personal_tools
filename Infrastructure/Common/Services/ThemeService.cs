using System.IO;
using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Serilog;

namespace PersonalAssistant.Infrastructure.Common.Services;

/// <summary>
/// 主题管理服务：深色/浅色主题切换。
/// 在 MergedDictionaries 中维护一个持久的 ResourceDictionary 槽位，
/// 切换时原子替换该槽位，保证 DynamicResource 正确触发所有 UI 刷新。
/// 资源成本：仅切换时触发资源刷新，空闲时零开销。
/// </summary>
public class ThemeService
{
    private readonly UserSettingsService _settings;

    /// <summary>主题字典在 MergedDictionaries 中的索引（全局唯一）</summary>
    private static int s_themeDictIndex = -1;

    public ThemeService(UserSettingsService settings)
    {
        _settings = settings;
    }

    public bool IsDarkTheme
    {
        get => _settings.IsDarkTheme;
        set
        {
            if (_settings.IsDarkTheme == value) return;
            _settings.IsDarkTheme = value;
            _settings.Save();
            ApplyTheme(value);
        }
    }

    /// <summary>启动时加载主题（必须在 MainWindow 创建前调用）</summary>
    public void Initialize()
    {
        var app = Application.Current;

        // 移除陈旧的主题字典
        for (int i = app.Resources.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            var src = app.Resources.MergedDictionaries[i].Source;
            if (src is not null &&
                (src.OriginalString.Contains("DarkTheme") ||
                 src.OriginalString.Contains("LightTheme") ||
                 src.OriginalString.Contains("ThemeColors")))
            {
                app.Resources.MergedDictionaries.RemoveAt(i);
            }
        }

        // 主题字典（原子替换依赖此索引）
        var themeDict = LoadThemeDict(_settings.IsDarkTheme);
        app.Resources.MergedDictionaries.Add(themeDict);
        s_themeDictIndex = app.Resources.MergedDictionaries.Count - 1;

        Log.Information("[ThemeService] 主题初始化完成: {Theme} (index={Idx})",
            _settings.IsDarkTheme ? "Dark" : "Light", s_themeDictIndex);
    }

    private static void ApplyTheme(bool isDark)
    {
        if (s_themeDictIndex < 0)
        {
            Log.Warning("[ThemeService] 主题字典索引未初始化，跳过切换");
            return;
        }

        var app = Application.Current;
        var newDict = LoadThemeDict(isDark);

        // 先移除旧字典，再插入新字典（比索引赋值更可靠地触发资源失效）
        var oldSource = app.Resources.MergedDictionaries[s_themeDictIndex].Source?.OriginalString ?? "(runtime)";
        app.Resources.MergedDictionaries.RemoveAt(s_themeDictIndex);
        app.Resources.MergedDictionaries.Insert(s_themeDictIndex, newDict);
        Log.Information("[ThemeService] 主题切换: {Old} → {New}", oldSource, newDict.Source?.OriginalString ?? "(runtime)");

        // 强制所有窗口完整布局刷新（UpdateLayout 比 InvalidateVisual 更彻底）
        foreach (Window window in app.Windows)
        {
            window.UpdateLayout();
        }
    }

    private static ResourceDictionary LoadThemeDict(bool isDark)
    {
        var themeName = isDark ? "DarkTheme.xaml" : "LightTheme.xaml";
        var themePath = GetThemeFilePath(themeName);

        if (File.Exists(themePath))
            return new ResourceDictionary { Source = new Uri(themePath, UriKind.Absolute) };

        Log.Warning("[ThemeService] 主题文件未找到: {Path}，使用内建回退", themePath);
        return isDark ? BuildDarkFallback() : BuildLightFallback();
    }

    private static string GetThemeFilePath(string fileName)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var path = Path.Combine(baseDir, "Infrastructure", "Common", "Themes", fileName);
        if (File.Exists(path)) return path;

        return Path.Combine(
            Path.GetDirectoryName(typeof(ThemeService).Assembly.Location)!,
            "Infrastructure", "Common", "Themes", fileName);
    }

    // ═══════════════════════════════════════════════════════════════
    // 内建回退：主题文件缺失时用代码构建（确保程序不崩溃）
    // ═══════════════════════════════════════════════════════════════

    private static ResourceDictionary BuildDarkFallback()
    {
        var d = BuildCommonBrushes(
            deep: 0x0B0E14, primary: 0x131720, secondary: 0x1A1F2E,
            border1: 0x1E2430, border2: 0x2A3040,
            fg1: 0xE8ECF1, fg2: 0xD1D5DB, fg3: 0x9CA3AF, fg4: 0x6B7280, fg5: 0x4B5563,
            accent1: 0x3B82F6, accent2: 0x6366F1,
            accentH1: 0x4F8EF7, accentH2: 0x7B73F5,
            bubble1: 0x3B82F6, bubble2: 0x6366F1,
            online: 0x22C55E, offline: 0xF59E0B);

        d["Brush.AccentBtn"] = new LinearGradientBrush(
            Color.FromRgb(0x3B, 0x82, 0xF6),
            Color.FromRgb(0x63, 0x66, 0xF1),
            new Point(0, 0), new Point(1, 0));
        d["Brush.InputCaret"] = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));

        return d;
    }

    private static ResourceDictionary BuildLightFallback()
    {
        var d = BuildCommonBrushes(
            deep: 0xF3F4F6, primary: 0xFFFFFF, secondary: 0xF9FAFB,
            border1: 0xE5E7EB, border2: 0xD1D5DB,
            fg1: 0x111827, fg2: 0x374151, fg3: 0x6B7280, fg4: 0x9CA3AF, fg5: 0xD1D5DB,
            accent1: 0x2563EB, accent2: 0x4F46E5,
            accentH1: 0x3B82F6, accentH2: 0x6366F1,
            bubble1: 0x2563EB, bubble2: 0x4F46E5,
            online: 0x16A34A, offline: 0xD97706);

        d["Brush.AccentBtn"] = new LinearGradientBrush(
            Color.FromRgb(0x25, 0x63, 0xEB),
            Color.FromRgb(0x4F, 0x46, 0xE5),
            new Point(0, 0), new Point(1, 0));
        d["Brush.InputCaret"] = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));

        return d;
    }

    private static ResourceDictionary BuildCommonBrushes(
        uint deep, uint primary, uint secondary,
        uint border1, uint border2,
        uint fg1, uint fg2, uint fg3, uint fg4, uint fg5,
        uint accent1, uint accent2,
        uint accentH1, uint accentH2,
        uint bubble1, uint bubble2,
        uint online, uint offline)
    {
        var d = new ResourceDictionary();

        d["Brush.BackgroundDeep"] = ToBrush(deep);
        d["Brush.BackgroundPrimary"] = ToBrush(primary);
        d["Brush.BackgroundSecondary"] = ToBrush(secondary);
        d["Brush.BorderPrimary"] = ToBrush(border1);
        d["Brush.BorderSecondary"] = ToBrush(border2);
        d["Brush.ForegroundPrimary"] = ToBrush(fg1);
        d["Brush.ForegroundSecondary"] = ToBrush(fg2);
        d["Brush.ForegroundTertiary"] = ToBrush(fg3);
        d["Brush.ForegroundMuted"] = ToBrush(fg4);
        d["Brush.ForegroundSubtle"] = ToBrush(fg5);
        d["Brush.AccentStart"] = ToBrush(accent1);
        d["Brush.AccentEnd"] = ToBrush(accent2);
        d["Brush.StatusOnline"] = ToBrush(online);
        d["Brush.StatusOffline"] = ToBrush(offline);

        d["Brush.UserBubble"] = new LinearGradientBrush(
            ToColor(bubble1), ToColor(bubble2), new Point(0, 0), new Point(1, 1));
        d["Brush.SendBtn"] = new LinearGradientBrush(
            ToColor(accent1), ToColor(accent2), new Point(0, 0), new Point(1, 0));
        d["Brush.SendBtnHover"] = new LinearGradientBrush(
            ToColor(accentH1), ToColor(accentH2), new Point(0, 0), new Point(1, 0));

        return d;
    }

    private static SolidColorBrush ToBrush(uint hex) =>
        new(ToColor(hex));

    private static Color ToColor(uint hex) =>
        Color.FromRgb((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);
}
