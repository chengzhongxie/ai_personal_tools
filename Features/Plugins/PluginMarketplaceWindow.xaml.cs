using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using PersonalAssistant.Features.Plugins.Services;
using Serilog;

namespace PersonalAssistant.Features.Plugins;

public partial class PluginMarketplaceWindow : Window
{
    private readonly PluginMarketplaceService _marketplace;
    private readonly IServiceProvider _serviceProvider;

    public PluginMarketplaceWindow(PluginMarketplaceService marketplace, IServiceProvider serviceProvider)
    {
        _marketplace = marketplace;
        _serviceProvider = serviceProvider;
        InitializeComponent();
        Loaded += async (_, _) => await LoadPluginsAsync();
    }

    private async Task LoadPluginsAsync()
    {
        LoadingText.Visibility = Visibility.Visible;
        PluginsItems.Visibility = Visibility.Collapsed;
        MessageText.Visibility = Visibility.Collapsed;

        try
        {
            var plugins = await _marketplace.SearchPluginsAsync();

            if (plugins.Count == 0)
            {
                LoadingText.Visibility = Visibility.Collapsed;
                MessageText.Text = "未找到可用插件。在 GitHub Gist 上创建包含 [personal-assistant-plugin] 标签的 .cs 文件即可发布。";
                MessageText.Visibility = Visibility.Visible;
                return;
            }

            PluginsItems.ItemsSource = plugins;
            LoadingText.Visibility = Visibility.Collapsed;
            PluginsItems.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            LoadingText.Visibility = Visibility.Collapsed;
            MessageText.Text = $"加载失败: {ex.Message}";
            MessageText.Visibility = Visibility.Visible;
            Log.Warning(ex, "[PluginMarketplace] 加载插件列表失败");
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not MarketPluginInfo plugin) return;
        btn.IsEnabled = false;
        btn.Content = "下载中...";

        try
        {
            var destPath = await _marketplace.DownloadPluginAsync(plugin);
            Log.Information("[PluginMarketplace] 插件已安装: {Name} → {Path}", plugin.Name, destPath);

            // Trigger hot reload
            var fileWatcher = _serviceProvider.GetRequiredService<PluginFileWatcher>();
            fileWatcher.TriggerReload(destPath);

            MessageBox.Show($"插件 \"{plugin.Name}\" 安装成功！已自动热重载。",
                "安装成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[PluginMarketplace] 安装插件失败: {Name}", plugin.Name);
            MessageBox.Show($"安装失败: {ex.Message}",
                "安装失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            btn.IsEnabled = true;
            btn.Content = "安装";
        }
    }
}
