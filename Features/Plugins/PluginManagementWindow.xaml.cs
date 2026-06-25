using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Microsoft.Extensions.DependencyInjection;
using PersonalAssistant.Core.Plugins;
using PersonalAssistant.Core.Services;
using PersonalAssistant.Features.Plugins.Services;
using PersonalAssistant.Infrastructure.Common.Services;

namespace PersonalAssistant.Features.Plugins;

/// <summary>
/// 插件管理窗口：查看、启用/禁用、删除和导入外部插件。
/// Transient 生命周期，每次打开都重新加载最新状态。
/// 资源成本：按需消耗（仅在窗口打开时），关闭后零开销。
/// </summary>
public partial class PluginManagementWindow : Window
{
    private readonly PluginAggregator _pluginAggregator;
    private readonly PluginLoader _pluginLoader;
    private readonly PluginStateService _pluginState;

    private sealed record PluginDisplayInfo
    {
        public string Name { get; init; } = "";
        public string Type { get; init; } = "内置";   // "内置" | "外部"
        public bool IsEnabled { get; set; }
        public string[] ToolNames { get; init; } = [];
        public string? SourceFile { get; init; }       // 仅外部插件：完整路径
        public bool HasSourceFile => SourceFile is not null;
    }

    public PluginManagementWindow(
        PluginAggregator pluginAggregator,
        PluginLoader pluginLoader,
        PluginStateService pluginState)
    {
        _pluginAggregator = pluginAggregator;
        _pluginLoader = pluginLoader;
        _pluginState = pluginState;

        InitializeComponent();
        BuildPluginList();
    }

    private void BuildPluginList()
    {
        var displayList = new List<PluginDisplayInfo>();

        foreach (var plugin in _pluginAggregator.AllPlugins)
        {
            var allToolNames = plugin.GetTools().Select(t => t.Name).ToArray();

            // 工具名超 8 个时截断显示
            var displayToolNames = allToolNames.Length > 8
                ? allToolNames.Take(8).Append($"+{allToolNames.Length - 8} more").ToArray()
                : allToolNames;

            var isExternal = plugin is ExternalPluginAdapter;
            var sourceFile = isExternal
                ? ((ExternalPluginAdapter)plugin).SourcePlugin.SourceFilePath
                : null;

            displayList.Add(new PluginDisplayInfo
            {
                Name = plugin.Name,
                Type = isExternal ? "外部" : "内置",
                IsEnabled = _pluginState.IsEnabled(plugin.Name),
                ToolNames = displayToolNames,
                SourceFile = sourceFile
            });
        }

        PluginsItemsControl.ItemsSource = displayList;
    }

    private void PluginCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox ||
            checkBox.DataContext is not PluginDisplayInfo info)
            return;

        _pluginState.SetEnabled(info.Name, info.IsEnabled);

        // 运行时同步：无需重启即可生效
        _pluginAggregator.RefreshActivePlugins();
    }

    private void DeletePlugin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.DataContext is not PluginDisplayInfo info ||
            info.SourceFile is null)
            return;

        var result = MessageBox.Show(
            $"确定要删除插件 \"{info.Name}\" 吗？\n\n此操作将删除源文件：\n{Path.GetFileName(info.SourceFile)}",
            "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            File.Delete(info.SourceFile);
            _pluginState.RemoveState(info.Name);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"删除文件失败:\n{ex.Message}",
                "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        RestartTip.Visibility = Visibility.Visible;

        // 从列表中移除
        if (PluginsItemsControl.ItemsSource is List<PluginDisplayInfo> list)
        {
            list.Remove(info);
            PluginsItemsControl.ItemsSource = null;
            PluginsItemsControl.ItemsSource = list;
        }
    }

    private void ImportPlugin_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择插件文件",
            Filter = "C# 源文件 (*.cs)|*.cs",
            Multiselect = true
        };

        if (dialog.ShowDialog() != true)
            return;

        var pluginsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PersonalAssistant", "Plugins");

        if (!Directory.Exists(pluginsDir))
            Directory.CreateDirectory(pluginsDir);

        var copiedFiles = new List<string>();

        foreach (var sourcePath in dialog.FileNames)
        {
            var fileName = Path.GetFileName(sourcePath);
            var destPath = Path.Combine(pluginsDir, fileName);

            // 检查是否已存在
            if (File.Exists(destPath))
            {
                var overwrite = MessageBox.Show(
                    $"文件 \"{fileName}\" 已存在，是否覆盖？",
                    "文件已存在", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (overwrite != MessageBoxResult.Yes)
                    continue;
            }

            try
            {
                File.Copy(sourcePath, destPath, overwrite: true);
                copiedFiles.Add(destPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"复制文件 \"{fileName}\" 失败:\n{ex.Message}",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        if (copiedFiles.Count == 0)
            return;

        // 重新加载所有外部插件以验证编译
        var loadedPlugins = _pluginLoader.LoadPlugins();
        var loadedNames = loadedPlugins.Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var failedFiles = new List<string>();
        foreach (var file in copiedFiles)
        {
            var fileName = Path.GetFileName(file);
            // 检查是否有新插件被加载（通过源文件路径匹配）
            var hasMatch = loadedPlugins.Any(p =>
                string.Equals(p.SourceFilePath, file, StringComparison.OrdinalIgnoreCase));
            if (!hasMatch)
                failedFiles.Add(fileName);
        }

        if (failedFiles.Count > 0)
        {
            MessageBox.Show(
                $"以下文件编译失败，已保留在插件目录中:\n\n{string.Join("\n", failedFiles)}\n\n" +
                $"请检查代码后重启应用，系统会自动跳过编译失败的插件。",
                "编译警告", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            MessageBox.Show(
                $"成功导入 {copiedFiles.Count} 个插件文件。",
                "导入成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        RestartTip.Visibility = Visibility.Visible;

        // 刷新列表（仅添加新导入的外部插件）
        BuildPluginList();
    }

    private void OpenMarketplace_Click(object sender, RoutedEventArgs e)
    {
        var marketplace = new PluginMarketplaceWindow(
            App.Services.GetRequiredService<PluginMarketplaceService>(),
            App.Services);
        marketplace.ShowDialog();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
