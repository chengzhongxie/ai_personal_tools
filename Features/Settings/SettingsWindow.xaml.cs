using System.ClientModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenAI;
using OpenAI.Chat;
using PersonalAssistant.Infrastructure.Common.Services;

namespace PersonalAssistant.Features.Settings;

/// <summary>
/// 设置窗口：AI 模型配置 + 开机自启动，配置保存在用户目录
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly UserSettingsService _settingsService;
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

    public SettingsWindow(UserSettingsService settingsService)
    {
        _settingsService = settingsService;
        InitializeComponent();
        InitializePresets();
        LoadSettings();
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

        // 根据当前 Endpoint + Model 匹配预设
        _isLoadingPreset = true;
        var matched = ProviderPresets.FirstOrDefault(p =>
            p.Endpoint == _settingsService.Endpoint &&
            p.Model == _settingsService.Model);
        ProviderPresetComboBox.SelectedItem = matched ?? ProviderPresets.Last(); // 最后一个 = 自定义
        _isLoadingPreset = false;
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
