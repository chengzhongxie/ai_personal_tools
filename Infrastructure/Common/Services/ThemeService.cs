using System.IO;
using System.Windows;
using System.Windows.Media;
using Serilog;

namespace PersonalAssistant.Infrastructure.Common.Services;

/// <summary>
/// 主题管理服务：深色/浅色主题切换。
/// 通过替换 Application 的 ResourceDictionary 实现运行时切换。
/// 资源成本：仅切换时触发资源刷新（一次性 CPU），空闲时零开销。
/// </summary>
public class ThemeService
{
    private readonly UserSettingsService _settings;

    public ThemeService(UserSettingsService settings)
    {
        _settings = settings;
    }

    public bool IsDarkTheme
    {
        get => _settings.IsDarkTheme;
        set
        {
            _settings.IsDarkTheme = value;
            _settings.Save();
            ApplyTheme(value);
        }
    }

    /// <summary>初始化时加载主题</summary>
    public void Initialize()
    {
        ApplyTheme(_settings.IsDarkTheme);
    }

    private static void ApplyTheme(bool isDark)
    {
        var themeName = isDark ? "DarkTheme.xaml" : "LightTheme.xaml";
        var themePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Infrastructure", "Common", "Themes", themeName);

        // 如果文件不存在（开发环境），从项目路径读取
        if (!File.Exists(themePath))
        {
            themePath = Path.Combine(
                Path.GetDirectoryName(typeof(ThemeService).Assembly.Location)!,
                "Infrastructure", "Common", "Themes", themeName);
        }

        var app = Application.Current;
        var targetDict = new ResourceDictionary();

        if (File.Exists(themePath))
        {
            targetDict.Source = new Uri(themePath, UriKind.Absolute);
            Log.Information("[ThemeService] 加载主题文件: {Path}", themePath);
        }
        else
        {
            // 回退：使用内建颜色
            Log.Warning("[ThemeService] 主题文件未找到: {Path}，使用回退", themePath);
            targetDict = isDark ? BuildDarkFallback() : BuildLightFallback();
        }

        // 移除旧的主题字典并添加新的
        var oldDict = app.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source is not null &&
                (d.Source.OriginalString.Contains("DarkTheme") ||
                 d.Source.OriginalString.Contains("LightTheme")));

        if (oldDict is not null)
            app.Resources.MergedDictionaries.Remove(oldDict);

        app.Resources.MergedDictionaries.Add(targetDict);
    }

    private static ResourceDictionary BuildDarkFallback()
    {
        var dict = new ResourceDictionary();
        dict["Brush.BackgroundDeep"] = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x0B, 0x0E, 0x14));
        dict["Brush.BackgroundPrimary"] = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x13, 0x17, 0x20));
        dict["Brush.BackgroundSecondary"] = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x1A, 0x1F, 0x2E));
        dict["Brush.BorderPrimary"] = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x1E, 0x24, 0x30));
        dict["Brush.BorderSecondary"] = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x2A, 0x30, 0x40));
        dict["Brush.ForegroundPrimary"] = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xE8, 0xEC, 0xF1));
        dict["Brush.ForegroundSecondary"] = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xD1, 0xD5, 0xDB));
        dict["Brush.ForegroundTertiary"] = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x9C, 0xA3, 0xAF));
        dict["Brush.ForegroundMuted"] = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x6B, 0x72, 0x80));
        dict["Brush.ForegroundSubtle"] = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x4B, 0x55, 0x63));
        dict["Brush.StatusOnline"] = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E));
        dict["Brush.StatusOffline"] = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B));
        dict["Brush.AccentStart"] = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x3B, 0x82, 0xF6));
        dict["Brush.AccentEnd"] = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x63, 0x66, 0xF1));

        var userBubble = new LinearGradientBrush(
            System.Windows.Media.Color.FromRgb(0x3B, 0x82, 0xF6),
            System.Windows.Media.Color.FromRgb(0x63, 0x66, 0xF1),
            new System.Windows.Point(0, 0), new System.Windows.Point(1, 1));
        dict["Brush.UserBubble"] = userBubble;

        var sendBtn = new LinearGradientBrush(
            System.Windows.Media.Color.FromRgb(0x3B, 0x82, 0xF6),
            System.Windows.Media.Color.FromRgb(0x63, 0x66, 0xF1),
            new System.Windows.Point(0, 0), new System.Windows.Point(1, 0));
        dict["Brush.SendBtn"] = sendBtn;

        var sendBtnHover = new LinearGradientBrush(
            System.Windows.Media.Color.FromRgb(0x4F, 0x8E, 0xF7),
            System.Windows.Media.Color.FromRgb(0x7B, 0x73, 0xF5),
            new System.Windows.Point(0, 0), new System.Windows.Point(1, 0));
        dict["Brush.SendBtnHover"] = sendBtnHover;

        return dict;
    }

    private static ResourceDictionary BuildLightFallback()
    {
        var dict = new ResourceDictionary();
        dict["Brush.BackgroundDeep"] = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xF3, 0xF4, 0xF6));
        dict["Brush.BackgroundPrimary"] = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF));
        dict["Brush.BackgroundSecondary"] = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xF9, 0xFA, 0xFB));
        dict["Brush.BorderPrimary"] = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xE5, 0xE7, 0xEB));
        dict["Brush.BorderSecondary"] = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xD1, 0xD5, 0xDB));
        dict["Brush.ForegroundPrimary"] = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x11, 0x18, 0x27));
        dict["Brush.ForegroundSecondary"] = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x37, 0x41, 0x51));
        dict["Brush.ForegroundTertiary"] = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x6B, 0x72, 0x80));
        dict["Brush.ForegroundMuted"] = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x9C, 0xA3, 0xAF));
        dict["Brush.ForegroundSubtle"] = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xD1, 0xD5, 0xDB));
        dict["Brush.StatusOnline"] = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x16, 0xA3, 0x4A));
        dict["Brush.StatusOffline"] = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xD9, 0x77, 0x06));
        dict["Brush.AccentStart"] = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x3B, 0x82, 0xF6));
        dict["Brush.AccentEnd"] = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x63, 0x66, 0xF1));

        var userBubble = new LinearGradientBrush(
            System.Windows.Media.Color.FromRgb(0x3B, 0x82, 0xF6),
            System.Windows.Media.Color.FromRgb(0x63, 0x66, 0xF1),
            new System.Windows.Point(0, 0), new System.Windows.Point(1, 1));
        dict["Brush.UserBubble"] = userBubble;

        var sendBtn = new LinearGradientBrush(
            System.Windows.Media.Color.FromRgb(0x3B, 0x82, 0xF6),
            System.Windows.Media.Color.FromRgb(0x63, 0x66, 0xF1),
            new System.Windows.Point(0, 0), new System.Windows.Point(1, 0));
        dict["Brush.SendBtn"] = sendBtn;

        var sendBtnHover = new LinearGradientBrush(
            System.Windows.Media.Color.FromRgb(0x4F, 0x8E, 0xF7),
            System.Windows.Media.Color.FromRgb(0x7B, 0x73, 0xF5),
            new System.Windows.Point(0, 0), new System.Windows.Point(1, 0));
        dict["Brush.SendBtnHover"] = sendBtnHover;

        return dict;
    }
}
