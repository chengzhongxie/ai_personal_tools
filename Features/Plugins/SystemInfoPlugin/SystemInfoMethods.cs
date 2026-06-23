using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using PersonalAssistant.Features.Plugins.SystemTools;
using Serilog;
using WinBitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PersonalAssistant.Features.Plugins.SystemInfoPlugin;

/// <summary>
/// 系统信息工具方法静态实现：system_info、screenshot
/// </summary>
internal static class SystemInfoMethods
{
    [Description(
        "Get system status information: memory usage, disk space, top processes, battery, uptime.\n" +
        "Call with no args or \"all\" for full summary.\n" +
        "  \"memory\" — RAM usage: total, available, used, percentage\n" +
        "  \"disk\"   — all fixed drives: total, free, used space\n" +
        "  \"processes\" — top 10 processes by memory usage\n" +
        "  \"battery\" — battery percentage, charging status, remaining time\n" +
        "  \"all\"    — everything above in one report")]
    public static string SystemInfo(
        [Description("Category: memory, disk, processes, battery, or empty/all for full summary")] string? category = null)
    {
        try
        {
            var cat = category?.Trim().ToLowerInvariant() ?? "all";
            var sb = new StringBuilder();

            if (cat is "all" or "memory")
                Win32Native.AppendMemoryInfo(sb);

            if (cat is "all" or "disk")
                Win32Native.AppendDiskInfo(sb);

            if (cat is "all" or "processes")
                Win32Native.AppendProcessInfo(sb);

            if (cat is "all" or "battery")
                Win32Native.AppendBatteryInfo(sb);

            if (cat is "all")
            {
                var uptime = TimeSpan.FromMilliseconds(Win32Native.GetTickCount64());
                sb.AppendLine($"System Uptime: {uptime:dd\\.hh\\:mm\\:ss}");
                sb.AppendLine($"Machine: {Environment.MachineName}");
                sb.AppendLine($"OS: {Environment.OSVersion}");
                sb.AppendLine($"Processors: {Environment.ProcessorCount}");
                sb.AppendLine($"CLR: {Environment.Version}");
            }

            var result = sb.ToString().TrimEnd();
            return string.IsNullOrEmpty(result) ? $"未知类别: {category}" : result;
        }
        catch (Exception ex)
        {
            return $"获取系统信息出错: {ex.Message}";
        }
    }

    [Description(
        "Capture a screenshot of the current screen, save as PNG, and run Windows built-in OCR.\n" +
        "Returns the file path, image dimensions, and any text found on screen.\n" +
        "100% local — no cloud AI needed. Use to read error dialogs, browser text, or UI content.")]
    public static async Task<string> Screenshot()
    {
        try
        {
            int w = Win32Native.GetSystemMetrics(Win32Native.SM_CXSCREEN);
            int h = Win32Native.GetSystemMetrics(Win32Native.SM_CYSCREEN);

            var screenDC = Win32Native.CreateDC("DISPLAY", null, null, IntPtr.Zero);
            var memDC = Win32Native.CreateCompatibleDC(screenDC);
            var bitmap = Win32Native.CreateCompatibleBitmap(screenDC, w, h);
            var oldBitmap = Win32Native.SelectObject(memDC, bitmap);

            if (!Win32Native.BitBlt(memDC, 0, 0, w, h, screenDC, 0, 0, Win32Native.SRCCOPY))
            {
                Win32Native.CleanupGDI(screenDC, memDC, bitmap, oldBitmap);
                return "截图失败: BitBlt 返回 false";
            }

            var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                bitmap, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            var path = Path.Combine(Path.GetTempPath(),
                $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            using (var stream = new FileStream(path, FileMode.Create))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                encoder.Save(stream);
            }

            Win32Native.CleanupGDI(screenDC, memDC, bitmap, oldBitmap);

            var size = new FileInfo(path).Length;
            var sb = new StringBuilder();
            sb.AppendLine($"截图已保存: {path}");
            sb.AppendLine($"分辨率: {w}x{h}, 大小: {Win32Native.FormatBytes((ulong)size)}");

            var ocrText = await RunLocalOcrAsync(path);
            if (!string.IsNullOrWhiteSpace(ocrText))
            {
                sb.AppendLine();
                sb.AppendLine("=== 屏幕文字 (本地OCR) ===");
                sb.Append(ocrText);
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"截图出错: {ex.Message}";
        }
    }

    private static async Task<string> RunLocalOcrAsync(string imagePath)
    {
        try
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine is null) return "";

            var file = await StorageFile.GetFileFromPathAsync(imagePath);
            using var stream = await file.OpenReadAsync();
            var decoder = await WinBitmapDecoder.CreateAsync(stream);
            var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

            var result = await engine.RecognizeAsync(softwareBitmap);
            return result.Text;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[Screenshot] 本地 OCR 失败");
            return "";
        }
    }
}
