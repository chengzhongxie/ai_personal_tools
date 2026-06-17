using System.Windows;
using System.Windows.Controls;
using PersonalAssistant.Infrastructure.Common.Services;

namespace PersonalAssistant.Features.Settings;

/// <summary>
/// 设置窗口：AI 模型配置 + 开机自启动，配置保存在用户目录
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly UserSettingsService _settingsService;
    private bool _apiKeyRevealed;

    public SettingsWindow(UserSettingsService settingsService)
    {
        _settingsService = settingsService;
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        ApiKeyPasswordBox.Password = _settingsService.ApiKey;
        ApiKeyTextBox.Text = _settingsService.ApiKey;
        ModelTextBox.Text = _settingsService.Model;
        EndpointTextBox.Text = _settingsService.Endpoint;
        AutoStartCheckBox.IsChecked = _settingsService.IsAutoStartEnabled;
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
        _settingsService.ApiKey = ApiKeyPasswordBox.Password;
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
}
