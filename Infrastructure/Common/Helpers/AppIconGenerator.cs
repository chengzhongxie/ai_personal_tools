using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace PersonalAssistant.Infrastructure.Common.Helpers;

/// <summary>
/// 生成应用图标（蓝紫渐变圆角 + "AI" 文字），
/// 同时提供 WinForms Icon（托盘）和 WPF System.Windows.Media.ImageSource（任务栏/窗口）。
/// 资源成本：仅在启动时创建一次，之后零消耗。
/// </summary>
public static class AppIconGenerator
{
    private const int Size = 64;

    /// <summary>已缓存的 WPF 图标（System.Windows.Media.ImageSource），供窗口设置 Icon</summary>
    public static System.Windows.Media.ImageSource WpfIcon => _wpfIcon ??= CreateWpfIcon();
    private static System.Windows.Media.ImageSource? _wpfIcon;

    /// <summary>创建 WinForms 托盘图标</summary>
    public static Icon CreateTrayIcon()
    {
        using var bitmap = DrawIconBitmap();
        return Icon.FromHandle(bitmap.GetHicon());
    }

    /// <summary>创建 WPF System.Windows.Media.ImageSource（用于 Window.Icon）</summary>
    private static System.Windows.Media.ImageSource CreateWpfIcon()
    {
        using var bitmap = DrawIconBitmap();
        var hIcon = bitmap.GetHicon();
        try
        {
            return Imaging.CreateBitmapSourceFromHIcon(
                hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            // 不要过早 DestroyIcon，System.Windows.Media.ImageSource 持有句柄引用
        }
    }

    private static Bitmap DrawIconBitmap()
    {
        var bitmap = new Bitmap(Size, Size);
        var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        // 外圈发光衬底（深色环境下可见）
        var glowRect = new Rectangle(2, 2, Size - 4, Size - 4);
        using var glowPath = RoundedRect(glowRect, 14);
        using var glowBrush = new SolidBrush(Color.FromArgb(20, 96, 165, 250));
        g.FillPath(glowBrush, glowPath);

        // 主体圆角矩形
        var rect = new Rectangle(4, 4, Size - 8, Size - 8);
        using var path = RoundedRect(rect, 12);

        // 蓝紫渐变
        using var brush = new LinearGradientBrush(
            rect,
            Color.FromArgb(59, 130, 246),   // #3B82F6 蓝
            Color.FromArgb(99, 102, 241),   // #6366F1 紫
            LinearGradientMode.ForwardDiagonal);
        g.FillPath(brush, path);

        // 微细边框，增强科技感
        using var pen = new Pen(Color.FromArgb(80, 147, 197, 253), 1.5f);
        g.DrawPath(pen, path);

        // "AI" 文字
        using var font = new Font(new FontFamily("Segoe UI"), 25,
            System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Color.White);
        const string text = "AI";
        var textSize = g.MeasureString(text, font);
        g.DrawString(text, font, textBrush,
            (Size - textSize.Width) / 2f,
            (Size - textSize.Height) / 2f - 1);

        g.Dispose();
        return bitmap;
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
}
