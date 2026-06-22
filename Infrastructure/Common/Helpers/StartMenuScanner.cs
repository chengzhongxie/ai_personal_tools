using System.IO;

namespace PersonalAssistant.Infrastructure.Common.Helpers;

/// <summary>
/// 扫描开始菜单快捷方式，获取用户已安装的应用程序列表。
/// 仅在应用启动时调用一次，无后台消耗。
/// </summary>
public static class StartMenuScanner
{
    // 这些是 Windows 自带的，不算是"用户安装的第三方工具"
    private static readonly HashSet<string> SystemTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "Accessibility", "Accessories", "Administrative Tools", "Command Prompt",
        "Control Panel", "File Explorer", "Getting Started", "Maintenance",
        "System Tools", "Windows Accessories", "Windows Administrative Tools",
        "Windows Ease of Access", "Windows PowerShell", "Windows Security",
        "Windows System", "Windows Tools", "Snipping Tool", "Notepad",
        "Microsoft Edge", "Microsoft Store",
    };

    /// <summary>
    /// 扫描开始菜单，返回用户安装的应用程序列表，按分类组织。
    /// </summary>
    public static IReadOnlyList<string> Scan()
    {
        var results = new List<string>();
        var startMenuPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs)),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)),
        };

        // 先扫描 .lnk 文件（顶级，无文件夹分组的应用）
        foreach (var startMenu in startMenuPaths)
        {
            if (!Directory.Exists(startMenu)) continue;

            var directLinks = Directory.GetFiles(startMenu, "*.lnk");
            foreach (var link in directLinks)
            {
                var name = Path.GetFileNameWithoutExtension(link);
                if (SystemTools.Contains(name)) continue;
                results.Add(name);
            }

            // 再扫描子文件夹（分类/应用组）
            var subDirs = Directory.GetDirectories(startMenu);
            foreach (var subDir in subDirs)
            {
                var folderName = Path.GetFileName(subDir);
                if (SystemTools.Contains(folderName)) continue;

                var links = Directory.GetFiles(subDir, "*.lnk", SearchOption.AllDirectories);
                if (links.Length == 0) continue;

                // 如果文件夹下只有 1-2 个同名链接，只显示应用名
                if (links.Length <= 2)
                {
                    foreach (var link in links)
                    {
                        var linkName = Path.GetFileNameWithoutExtension(link);
                        if (linkName.Contains("Uninstall", StringComparison.OrdinalIgnoreCase) ||
                            linkName.Contains("卸载", StringComparison.OrdinalIgnoreCase))
                            continue;
                        results.Add(linkName);
                    }
                }
                else
                {
                    // 多个应用的分类文件夹，显示分类名
                    results.Add($"{folderName}/ ({links.Length} apps)");
                }
            }
        }

        return results.Distinct().OrderBy(x => x).ToList();
    }
}
