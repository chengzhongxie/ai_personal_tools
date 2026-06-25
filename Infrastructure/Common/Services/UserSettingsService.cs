using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using PersonalAssistant.Features.Chat.Models;
using Serilog;

namespace PersonalAssistant.Infrastructure.Common.Services;

/// <summary>
/// 用户级设置服务：读写 %APPDATA%\PersonalAssistant\settings.json，
/// 管理 AI 模型配置和开机自启动。每个电脑独立配置，不入库。
/// </summary>
public class UserSettingsService
{
    private const string RunRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppKeyName = "PersonalAssistant";

    private static readonly string SettingsFilePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PersonalAssistant",
        "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>DeepSeek API 密钥</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>模型名称</summary>
    public string Model { get; set; } = "deepseek-v4-flash";

    /// <summary>API 端点地址（OpenAI 兼容格式）</summary>
    public string Endpoint { get; set; } = "https://api.deepseek.com";

    /// <summary>是否开机自启动</summary>
    public bool IsAutoStartEnabled { get; set; }

    /// <summary>主窗口热键修饰符（默认 Alt）</summary>
    public uint HotkeyModifiers { get; set; } = 0x0001; // MOD_ALT

    /// <summary>主窗口热键虚拟键码（默认 Space）</summary>
    public uint HotkeyKey { get; set; } = 0x20; // VK_SPACE

    /// <summary>选中文本热键修饰符（默认 Ctrl+Alt）</summary>
    public uint SelectTextModifiers { get; set; } = 0x0001 | 0x0002; // MOD_ALT | MOD_CONTROL

    /// <summary>选中文本热键虚拟键码（默认 Space）</summary>
    public uint SelectTextKey { get; set; } = 0x20; // VK_SPACE

    /// <summary>是否使用深色主题（默认 true）</summary>
    public bool IsDarkTheme { get; set; } = true;

    /// <summary>从文件加载设置，若文件不存在则用默认值</summary>
    public UserSettingsService()
    {
        Load();
    }

    /// <summary>导出为 ChatSettings 对象（供 ChatService 使用）</summary>
    public ChatSettings GetChatSettings() => new()
    {
        ApiKey = ApiKey,
        Model = Model.ToLowerInvariant(),
        Endpoint = Endpoint
    };

    /// <summary>保存设置到用户目录并同步注册表</summary>
    public void Save()
    {
        var dir = System.IO.Path.GetDirectoryName(SettingsFilePath)!;
        if (!System.IO.Directory.Exists(dir))
            System.IO.Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(new SettingsData
        {
            ApiKey = ApiKey,
            Model = Model.ToLowerInvariant(),
            Endpoint = Endpoint,
            IsAutoStartEnabled = IsAutoStartEnabled,
            HotkeyModifiers = HotkeyModifiers,
            HotkeyKey = HotkeyKey,
            SelectTextModifiers = SelectTextModifiers,
            SelectTextKey = SelectTextKey,
            IsDarkTheme = IsDarkTheme
        }, JsonOptions);

        System.IO.File.WriteAllText(SettingsFilePath, json);
        SyncAutoStart();
    }

    private void Load()
    {
        if (!System.IO.File.Exists(SettingsFilePath))
        {
            IsAutoStartEnabled = IsAutoStartInRegistry();
            return;
        }

        try
        {
            var json = System.IO.File.ReadAllText(SettingsFilePath);
            var data = JsonSerializer.Deserialize<SettingsData>(json);
            if (data is not null)
            {
                ApiKey = data.ApiKey ?? string.Empty;
                Model = data.Model ?? "deepseek-v4-flash";
                Endpoint = data.Endpoint ?? "https://api.deepseek.com";
                IsAutoStartEnabled = data.IsAutoStartEnabled;
                HotkeyModifiers = data.HotkeyModifiers != 0 ? data.HotkeyModifiers : 0x0001;
                HotkeyKey = data.HotkeyKey != 0 ? data.HotkeyKey : 0x20;
                SelectTextModifiers = data.SelectTextModifiers != 0 ? data.SelectTextModifiers : (0x0001 | 0x0002);
                SelectTextKey = data.SelectTextKey != 0 ? data.SelectTextKey : 0x20;
                IsDarkTheme = data.IsDarkTheme;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[UserSettings] 配置文件损坏，将使用默认值");
        }
    }

    private void SyncAutoStart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: true);
        if (IsAutoStartEnabled)
        {
            var exePath = System.IO.Path.ChangeExtension(
                System.Reflection.Assembly.GetExecutingAssembly().Location,
                ".exe");
            key?.SetValue(AppKeyName, $"\"{exePath}\"");
        }
        else
        {
            key?.DeleteValue(AppKeyName, throwOnMissingValue: false);
        }
    }

    private static bool IsAutoStartInRegistry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunRegistryPath);
        return key?.GetValue(AppKeyName) != null;
    }

    private sealed record SettingsData
    {
        public string? ApiKey { get; init; }
        public string? Model { get; init; }
        public string? Endpoint { get; init; }
        public bool IsAutoStartEnabled { get; init; }
        public uint HotkeyModifiers { get; init; }
        public uint HotkeyKey { get; init; }
        public uint SelectTextModifiers { get; init; }
        public uint SelectTextKey { get; init; }
        public bool IsDarkTheme { get; init; }
    }
}
