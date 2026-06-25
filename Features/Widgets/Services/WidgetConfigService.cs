using System.IO;
using System.Text.Json;
using PersonalAssistant.Features.Widgets.Models;

namespace PersonalAssistant.Features.Widgets.Services;

public class WidgetConfigService
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PersonalAssistant", "widget_config.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public WidgetConfig Config { get; private set; } = new();

    public WidgetConfigService()
    {
        Load();
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(ConfigPath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Config, JsonOptions));
    }

    private void Load()
    {
        if (!File.Exists(ConfigPath)) return;
        try
        {
            var json = File.ReadAllText(ConfigPath);
            Config = JsonSerializer.Deserialize<WidgetConfig>(json) ?? new();
        }
        catch
        {
            Config = new();
        }
    }
}
