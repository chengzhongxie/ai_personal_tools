using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WinForms = System.Windows.Forms;

namespace PersonalAssistant.Infrastructure.Common.Services;

/// <summary>
/// 系统托盘服务：托盘图标、右键菜单（显示主窗口、设置、退出）
/// </summary>
public class TrayService : IDisposable
{
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly IServiceProvider _serviceProvider;

    public TrayService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;

        var contextMenu = new WinForms.ContextMenuStrip();
        contextMenu.Items.Add("显示主窗口", null, OnShowWindow);
        contextMenu.Items.Add("设置", null, OnOpenSettings);
        contextMenu.Items.Add(new WinForms.ToolStripSeparator());
        contextMenu.Items.Add("退出", null, OnExit);

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Text = "个人 AI 助手",
            ContextMenuStrip = contextMenu,
            Visible = true
        };
        _notifyIcon.DoubleClick += OnShowWindow;
    }

    /// <summary>绘制 AI 主题托盘图标：蓝紫渐变圆角方块 + "AI" 文字</summary>
    private static Icon CreateTrayIcon()
    {
        var size = 64;
        using var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        g.Clear(Color.Transparent);

        // 圆角矩形路径
        var rect = new Rectangle(4, 4, size - 8, size - 8);
        var radius = 12;
        using var path = RoundedRect(rect, radius);

        // 蓝紫渐变填充
        using var brush = new LinearGradientBrush(
            rect, Color.FromArgb(59, 130, 246), Color.FromArgb(99, 102, 241),
            LinearGradientMode.ForwardDiagonal);
        g.FillPath(brush, path);

        // "AI" 白色文字
        using var font = new Font(new FontFamily("Segoe UI"), 26,
            System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Color.White);
        var text = "AI";
        var textSize = g.MeasureString(text, font);
        g.DrawString(text, font, textBrush,
            (size - textSize.Width) / 2,
            (size - textSize.Height) / 2);

        return Icon.FromHandle(bitmap.GetHicon());
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void OnShowWindow(object? sender, EventArgs e)
    {
        _serviceProvider.GetRequiredService<MainWindow>().ShowWindow();
    }

    private void OnOpenSettings(object? sender, EventArgs e)
    {
        var settingsWindow = _serviceProvider.GetRequiredService<
            Features.Settings.SettingsWindow>();
        settingsWindow.ShowDialog();
    }

    private void OnExit(object? sender, EventArgs e)
    {
        _notifyIcon.Visible = false;
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
