using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PersonalAssistant.Features.Clipboard.Services;

/// <summary>
/// 剪贴板工具方法静态辅助类。所有方法为零 token 本地执行。
/// 资源成本：仅调用时消耗 CPU，空闲时零开销。
/// </summary>
public static class ClipboardToolHelper
{
    // ──── 文件路径 ────

    /// <summary>复制文件/文件夹的完整路径到剪贴板</summary>
    public static void CopyFullPath(string path, ClipboardMonitor monitor)
    {
        monitor.SuppressNextUpdate();
        System.Windows.Clipboard.SetText(path);
    }

    /// <summary>复制文件名（不含扩展名）到剪贴板</summary>
    public static void CopyFileNameWithoutExtension(string path, ClipboardMonitor monitor)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        monitor.SuppressNextUpdate();
        System.Windows.Clipboard.SetText(name);
    }

    /// <summary>在 Windows 终端中打开路径所在目录</summary>
    public static void OpenInTerminal(string path)
    {
        try
        {
            var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Process.Start(new ProcessStartInfo("wt.exe", $"-d \"{dir}\"") { UseShellExecute = true });
                return;
            }
            Process.Start(new ProcessStartInfo("wt.exe") { UseShellExecute = true });
        }
        catch
        {
            // fallback: cmd
            try { Process.Start(new ProcessStartInfo("cmd.exe") { UseShellExecute = true }); }
            catch { }
        }
    }

    /// <summary>在资源管理器中打开路径并选中</summary>
    public static void OpenInExplorer(string path)
    {
        try { Process.Start("explorer.exe", $"/select,\"{path}\""); }
        catch { }
    }

    // ──── 文本统计 ────

    public static string GetTextStatistics(string text)
    {
        var charCount = text.Length;
        var charNoSpaces = text.Count(c => !char.IsWhiteSpace(c));
        var wordCount = Regex.Matches(text, @"[\p{L}\p{N}]+").Count;
        var lineCount = text.Count(c => c == '\n') + 1;
        var byteCount = Encoding.UTF8.GetByteCount(text);

        var sb = new StringBuilder();
        sb.AppendLine($"字符数: {charCount:N0}");
        sb.AppendLine($"字符数(去空格): {charNoSpaces:N0}");
        sb.AppendLine($"单词数: {wordCount:N0}");
        sb.AppendLine($"行数: {lineCount:N0}");
        sb.AppendLine($"UTF-8 字节: {byteCount:N0}");
        return sb.ToString().TrimEnd();
    }

    // ──── Base64 ────

    public static bool IsBase64(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length < 20 || trimmed.Length % 4 != 0) return false;
        return Regex.IsMatch(trimmed, @"^[A-Za-z0-9+/]*={0,2}$");
    }

    public static string Base64Decode(string text)
    {
        try
        {
            var trimmed = text.Trim();
            var bytes = Convert.FromBase64String(trimmed);
            // 先尝试 UTF-8 文本
            var decoded = Encoding.UTF8.GetString(bytes);
            // 检查是否为可读文本
            if (decoded.All(c => !char.IsControl(c) || c == '\n' || c == '\r' || c == '\t'))
                return decoded;
            // 否则返回十六进制
            return BitConverter.ToString(bytes);
        }
        catch (Exception ex) { return $"Base64 解码失败: {ex.Message}"; }
    }

    public static string Base64Encode(string text)
    {
        try { return Convert.ToBase64String(Encoding.UTF8.GetBytes(text)); }
        catch (Exception ex) { return $"Base64 编码失败: {ex.Message}"; }
    }

    // ──── JSON ────

    public static bool IsJson(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length < 2) return false;
        if (!((trimmed[0] == '{' && trimmed[^1] == '}') ||
              (trimmed[0] == '[' && trimmed[^1] == ']')))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            return true;
        }
        catch { return false; }
    }

    public static string FormatJson(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text.Trim());
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex) { return $"JSON 格式化失败: {ex.Message}"; }
    }

    public static string MinifyJson(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text.Trim());
            return JsonSerializer.Serialize(doc.RootElement);
        }
        catch (Exception ex) { return $"JSON 压缩失败: {ex.Message}"; }
    }

    // ──── 时间戳 ────

    public static bool IsTimestamp(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length != 10 && trimmed.Length != 13) return false;
        if (!long.TryParse(trimmed, out var ts)) return false;

        // 10 位秒级：2000-2100 年范围
        if (trimmed.Length == 10)
            return ts is >= 946684800 and <= 4102444800;

        // 13 位毫秒级
        return ts is >= 946684800000 and <= 4102444800000;
    }

    public static string ConvertTimestamp(string text)
    {
        try
        {
            var trimmed = text.Trim();
            var ts = long.Parse(trimmed);
            var dt = trimmed.Length == 10
                ? DateTimeOffset.FromUnixTimeSeconds(ts)
                : DateTimeOffset.FromUnixTimeMilliseconds(ts);
            var local = dt.ToLocalTime();
            return $"UTC: {dt:yyyy-MM-dd HH:mm:ss}\n本地: {local:yyyy-MM-dd HH:mm:ss}\n星期: {local:dddd}";
        }
        catch (Exception ex) { return $"时间戳转换失败: {ex.Message}"; }
    }

    // ──── 颜色检测 ────

    public static bool IsHexColor(string text)
    {
        var trimmed = text.Trim();
        return Regex.IsMatch(trimmed, @"^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$");
    }

    public static bool IsRgbColor(string text)
    {
        var trimmed = text.Trim();
        return Regex.IsMatch(trimmed, @"^rgb\s*\(\s*\d{1,3}\s*,\s*\d{1,3}\s*,\s*\d{1,3}\s*\)$");
    }

    public static (byte r, byte g, byte b) ParseColor(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("#"))
        {
            var hex = trimmed.TrimStart('#');
            if (hex.Length == 3)
                hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
            return (
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16));
        }
        // rgb(r,g,b)
        var match = Regex.Match(trimmed, @"(\d{1,3}),\s*(\d{1,3}),\s*(\d{1,3})");
        return (
            byte.Parse(match.Groups[1].Value),
            byte.Parse(match.Groups[2].Value),
            byte.Parse(match.Groups[3].Value));
    }

    // ──── 数学表达式 ────

    public static bool IsMathExpression(string text)
    {
        var trimmed = text.Trim();
        return Regex.IsMatch(trimmed, @"^[\d\s+\-*/().,%^√πe]+$")
            && Regex.IsMatch(trimmed, @"[\d]")
            && Regex.IsMatch(trimmed, @"[+\-*/%^]");
    }

    public static string EvaluateMath(string text)
    {
        try
        {
            var expr = text.Trim()
                .Replace('×', '*')
                .Replace('÷', '/')
                .Replace("π", Math.PI.ToString())
                .Replace("pi", Math.PI.ToString())
                .Replace("e", Math.E.ToString())
                .Replace("^", "**")  // not supported by DataTable, handle separately
                .Replace("%", "/100.0");

            // Handle ^ operator
            expr = Regex.Replace(expr, @"(\d+(?:\.\d+)?)\s*\*\*\s*(\d+(?:\.\d+)?)", m =>
            {
                var b = double.Parse(m.Groups[1].Value);
                var e = double.Parse(m.Groups[2].Value);
                return Math.Pow(b, e).ToString();
            });

            var result = new System.Data.DataTable().Compute(expr, null);
            return $"{text.Trim()} = {result}";
        }
        catch (Exception ex) { return $"计算失败: {ex.Message}"; }
    }

    // ──── 剪贴板图片 OCR ────

    public static bool HasClipboardImage()
    {
        try { return System.Windows.Clipboard.ContainsImage(); }
        catch { return false; }
    }
}
