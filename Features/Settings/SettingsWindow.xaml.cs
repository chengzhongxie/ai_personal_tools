using System.ClientModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using OpenAI;
using OpenAI.Chat;
using PersonalAssistant.Features.KnowledgeBase.Services;
using PersonalAssistant.Infrastructure.Common.Helpers;
using PersonalAssistant.Infrastructure.Common.Services;

namespace PersonalAssistant.Features.Settings;

/// <summary>
/// 设置窗口：AI 模型配置 + 开机自启动，配置保存在用户目录
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly UserSettingsService _settingsService;
    private readonly KnowledgeBaseService _kbService;
    private bool _apiKeyRevealed;
    private bool _isLoadingPreset;

    /// <summary>提供商预设，含名称、端点地址和默认模型</summary>
    private sealed record ProviderPreset(string Name, string Endpoint, string Model);

    private static readonly ProviderPreset[] ProviderPresets =
    [
        new("DeepSeek", "https://api.deepseek.com", "deepseek-v4-flash"),
        new("智谱 GLM-4.7-Flash", "https://open.bigmodel.cn/api/paas/v4/", "glm-4.7-flash"),
        new("自定义", "", "")
    ];

    public SettingsWindow(UserSettingsService settingsService, KnowledgeBaseService kbService)
    {
        _settingsService = settingsService;
        _kbService = kbService;
        InitializeComponent();
        InitializePresets();
        LoadSettings();
        LoadKbStatus();
    }

    private void LoadKbStatus()
    {
        if (_kbService.IsIndexed)
        {
            KbDirTextBox.Text = _kbService.SourceDirectory ?? "";
            KbStatus.Text = $"已索引 {_kbService.IndexedFileCount} 个文件";
            KbStatus.Foreground = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E));
        }
        else
        {
            KbStatus.Text = "未建立索引";
        }
    }

    private void BrowseKbDir_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择知识库文档目录",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            KbDirTextBox.Text = dialog.FolderName;
        }
    }

    private async void IndexKb_Click(object sender, RoutedEventArgs e)
    {
        var dir = KbDirTextBox.Text;
        if (string.IsNullOrWhiteSpace(dir) || dir == "未选择目录")
        {
            KbStatus.Text = "请先选择目录";
            KbStatus.Foreground = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B));
            return;
        }

        IndexKbBtn.IsEnabled = false;
        KbStatus.Text = "正在索引...";
        KbStatus.Foreground = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x6B, 0x72, 0x80));

        try
        {
            await _kbService.IndexDirectoryAsync(dir,
                progress: msg =>
                {
                    Dispatcher.Invoke(() => KbStatus.Text = msg);
                });

            Dispatcher.Invoke(() =>
            {
                KbStatus.Text = $"索引完成! {_kbService.IndexedFileCount} 个文件, {_kbService.IndexedFileCount} 个文档";
                KbStatus.Foreground = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E));
            });
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                KbStatus.Text = $"索引失败: {ex.Message}";
                KbStatus.Foreground = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44));
            });
        }
        finally
        {
            Dispatcher.Invoke(() => IndexKbBtn.IsEnabled = true);
        }
    }

    private void InitializePresets()
    {
        foreach (var preset in ProviderPresets)
            ProviderPresetComboBox.Items.Add(preset);
    }

    private void LoadSettings()
    {
        ApiKeyPasswordBox.Password = _settingsService.ApiKey;
        ApiKeyTextBox.Text = _settingsService.ApiKey;
        ModelTextBox.Text = _settingsService.Model;
        EndpointTextBox.Text = _settingsService.Endpoint;
        AutoStartCheckBox.IsChecked = _settingsService.IsAutoStartEnabled;

        // 加载快捷键配置
        MainHotkeyBox.CapturedModifiers = ModifiersFromWin32(_settingsService.HotkeyModifiers);
        MainHotkeyBox.CapturedKey = KeyFromVk(_settingsService.HotkeyKey);
        MainHotkeyBox.HotkeyText = ""; // 强制刷新显示

        SelectTextHotkeyBox.CapturedModifiers = ModifiersFromWin32(_settingsService.SelectTextModifiers);
        SelectTextHotkeyBox.CapturedKey = KeyFromVk(_settingsService.SelectTextKey);
        SelectTextHotkeyBox.HotkeyText = ""; // 强制刷新显示

        // 根据当前 Endpoint + Model 匹配预设
        _isLoadingPreset = true;
        var matched = ProviderPresets.FirstOrDefault(p =>
            p.Endpoint == _settingsService.Endpoint &&
            p.Model == _settingsService.Model);
        ProviderPresetComboBox.SelectedItem = matched ?? ProviderPresets.Last(); // 最后一个 = 自定义
        _isLoadingPreset = false;
    }

    private static ModifierKeys ModifiersFromWin32(uint mods)
    {
        var result = ModifierKeys.None;
        if ((mods & 0x0001) != 0) result |= ModifierKeys.Alt;
        if ((mods & 0x0002) != 0) result |= ModifierKeys.Control;
        if ((mods & 0x0004) != 0) result |= ModifierKeys.Shift;
        if ((mods & 0x0008) != 0) result |= ModifierKeys.Windows;
        return result;
    }

    private static Key KeyFromVk(uint vk) => vk switch
    {
        0x20 => Key.Space,
        0x0D => Key.Enter,
        0x1B => Key.Escape,
        0x08 => Key.Back,
        0x2E => Key.Delete,
        0x09 => Key.Tab,
        0x21 => Key.PageUp,
        0x22 => Key.PageDown,
        0x23 => Key.End,
        0x24 => Key.Home,
        0x25 => Key.Left,
        0x26 => Key.Up,
        0x27 => Key.Right,
        0x28 => Key.Down,
        >= 0x30 and <= 0x39 => (Key)(vk - 0x30 + (int)Key.D0),
        >= 0x41 and <= 0x5A => (Key)(vk - 0x41 + (int)Key.A),
        >= 0x70 and <= 0x7B => (Key)(vk - 0x70 + (int)Key.F1),
        _ => Key.None
    };

    private static uint VkFromKey(Key key) => key switch
    {
        Key.Space => 0x20,
        Key.Enter => 0x0D,
        Key.Escape => 0x1B,
        Key.Back => 0x08,
        Key.Delete => 0x2E,
        Key.Tab => 0x09,
        Key.PageUp => 0x21,
        Key.PageDown => 0x22,
        Key.End => 0x23,
        Key.Home => 0x24,
        Key.Left => 0x25,
        Key.Up => 0x26,
        Key.Right => 0x27,
        Key.Down => 0x28,
        >= Key.D0 and <= Key.D9 => (uint)(key - Key.D0 + 0x30),
        >= Key.A and <= Key.Z => (uint)(key - Key.A + 0x41),
        >= Key.F1 and <= Key.F12 => (uint)(key - Key.F1 + 0x70),
        _ => 0
    };

    private static uint Win32Modifiers(ModifierKeys mods)
    {
        uint result = 0;
        if (mods.HasFlag(ModifierKeys.Alt)) result |= 0x0001;
        if (mods.HasFlag(ModifierKeys.Control)) result |= 0x0002;
        if (mods.HasFlag(ModifierKeys.Shift)) result |= 0x0004;
        if (mods.HasFlag(ModifierKeys.Windows)) result |= 0x0008;
        return result;
    }

    private void ProviderPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingPreset || ProviderPresetComboBox.SelectedItem is not ProviderPreset preset)
            return;

        // "自定义" 不覆盖已有值
        if (preset.Name == "自定义")
            return;

        ModelTextBox.Text = preset.Model;
        EndpointTextBox.Text = preset.Endpoint;
    }

    private void ApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _settingsService.ApiKey = ApiKeyPasswordBox.Password;
    }

    private void ApiKeyTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _settingsService.ApiKey = ApiKeyTextBox.Text;
    }

    private void ToggleApiKeyVisibility_Click(object sender, RoutedEventArgs e)
    {
        _apiKeyRevealed = !_apiKeyRevealed;
        if (_apiKeyRevealed)
        {
            ApiKeyPasswordBox.Visibility = Visibility.Collapsed;
            ApiKeyTextBox.Visibility = Visibility.Visible;
            ApiKeyTextBox.Text = ApiKeyPasswordBox.Password;
            ToggleApiKeyBtn.Content = "隐藏";
        }
        else
        {
            ApiKeyTextBox.Visibility = Visibility.Collapsed;
            ApiKeyPasswordBox.Visibility = Visibility.Visible;
            ApiKeyPasswordBox.Password = ApiKeyTextBox.Text;
            ToggleApiKeyBtn.Content = "显示";
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.ApiKey = (_apiKeyRevealed ? ApiKeyTextBox.Text : ApiKeyPasswordBox.Password).Trim();
        _settingsService.Model = ModelTextBox.Text.Trim();
        _settingsService.Endpoint = EndpointTextBox.Text.Trim();
        _settingsService.IsAutoStartEnabled = AutoStartCheckBox.IsChecked == true;
        _settingsService.HotkeyModifiers = Win32Modifiers(MainHotkeyBox.CapturedModifiers);
        _settingsService.HotkeyKey = VkFromKey(MainHotkeyBox.CapturedKey);
        _settingsService.SelectTextModifiers = Win32Modifiers(SelectTextHotkeyBox.CapturedModifiers);
        _settingsService.SelectTextKey = VkFromKey(SelectTextHotkeyBox.CapturedKey);
        _settingsService.Save();

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        TestBtn.IsEnabled = false;
        TestStatus.Text = "正在测试...";
        TestStatus.Foreground = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x6B, 0x72, 0x80));

        var apiKey = (_apiKeyRevealed ? ApiKeyTextBox.Text : ApiKeyPasswordBox.Password).Trim();
        var endpoint = EndpointTextBox.Text.Trim();
        var model = ModelTextBox.Text.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "sk-your-key-here")
        {
            TestStatus.Text = "请先填写 API Key";
            TestStatus.Foreground = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B));
            TestBtn.IsEnabled = true;
            return;
        }

        try
        {
            var options = new OpenAIClientOptions
            {
                Endpoint = new Uri(endpoint)
            };
            var client = new OpenAIClient(new ApiKeyCredential(apiKey), options);
            var chatClient = client.GetChatClient(model);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await chatClient.CompleteChatAsync(
                [new UserChatMessage("Hi")],
                new ChatCompletionOptions { MaxOutputTokenCount = 5 },
                cts.Token);

            TestStatus.Text = "连接成功";
            TestStatus.Foreground = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E));
        }
        catch (Exception ex)
        {
            TestStatus.Text = $"连接失败: {ex.Message}";
            TestStatus.Foreground = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44));
        }
        finally
        {
            TestBtn.IsEnabled = true;
        }
    }
}
