using System.IO;
using System.Text;
using Microsoft.Win32;
using PersonalAssistant.Features.Chat.Models;
using PersonalAssistant.Features.Chat.Models.Enums;
using PersonalAssistant.Infrastructure.Common.Services;
using Serilog;

namespace PersonalAssistant.Features.Chat.Services;

/// <summary>
/// 对话导出服务：将聊天消息列表导出为 Markdown 文件。
/// 资源成本：仅导出时消耗，空闲时零开销。
/// </summary>
public class ChatExportService
{
    private readonly IChatHistoryService _historyService;

    public ChatExportService(IChatHistoryService historyService)
    {
        _historyService = historyService;
    }

    /// <summary>
    /// 将当前对话历史导出为 Markdown 文件。
    /// 弹出 SaveFileDialog 让用户选择保存位置。
    /// </summary>
    public void ExportToMarkdown()
    {
        var messages = _historyService.Load();
        if (messages.Count == 0)
        {
            Log.Information("[ChatExport] 无对话历史可导出");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Markdown 文件 (*.md)|*.md|文本文件 (*.txt)|*.txt",
            DefaultExt = ".md",
            FileName = $"对话记录_{DateTime.Now:yyyyMMdd_HHmmss}.md",
            Title = "导出对话记录"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var markdown = GenerateMarkdown(messages);
            File.WriteAllText(dialog.FileName, markdown, Encoding.UTF8);
            Log.Information("[ChatExport] 导出成功: {Path}", dialog.FileName);

            // 托盘通知
            System.Windows.MessageBox.Show(
                $"对话记录已导出到:\n{dialog.FileName}",
                "导出成功",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ChatExport] 导出失败");
            System.Windows.MessageBox.Show(
                $"导出失败: {ex.Message}",
                "导出错误",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 生成对话记录的 Markdown 文本。
    /// </summary>
    public static string GenerateMarkdown(IEnumerable<ChatMessage> messages)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 个人 AI 助手 - 对话记录");
        sb.AppendLine();
        sb.AppendLine($"**导出时间**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        foreach (var msg in messages)
        {
            var roleLabel = msg.Role switch
            {
                MessageRole.User => "### 用户",
                MessageRole.Assistant => "### AI 助手",
                MessageRole.System => "### 系统",
                MessageRole.Tool => "### 工具",
                _ => "### 未知"
            };

            sb.AppendLine(roleLabel);
            sb.AppendLine($"*{msg.Timestamp:yyyy-MM-dd HH:mm:ss}*");
            sb.AppendLine();

            // 处理 Markdown 内容中的代码块（直接嵌入）
            sb.AppendLine(msg.Content);
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
