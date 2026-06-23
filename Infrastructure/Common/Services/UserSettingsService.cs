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
            IsAutoStartEnabled = IsAutoStartEnabled
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
    }
}
