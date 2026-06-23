using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PersonalAssistant.Features.Plugins.SystemTools;

/// <summary>
/// Win32 P/Invoke 和辅助方法（从 ChatAgentService.Win32.cs 提取）。
/// 全部为 static 方法，无状态，供 SystemToolsPlugin 和 SystemInfoPlugin 使用。
/// </summary>
internal static class Win32Native
{
    // ═══════════════════════════════════════════════════════════════
    // SendInput — hardware-level input injection
    // ═══════════════════════════════════════════════════════════════

    public const int INPUT_KEYBOARD = 1;
    public const int KEYEVENTF_KEYDOWN = 0x0000;
    public const int KEYEVENTF_KEYUP = 0x0002;
    public const int KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const int KEYEVENTF_UNICODE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public int dwFlags;
        public int time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUT
    {
        [FieldOffset(0)] public int type;
        [FieldOffset(8)] public KEYBDINPUT ki;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern ushort MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    public static extern short VkKeyScan(char ch);

    // ═══════════════════════════════════════════════════════════════
    // Window management
    // ═══════════════════════════════════════════════════════════════

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder className, int count);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public const uint GW_OWNER = 4;

    // ═══════════════════════════════════════════════════════════════
    // System info — memory
    // ═══════════════════════════════════════════════════════════════

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    // ═══════════════════════════════════════════════════════════════
    // Battery
    // ═══════════════════════════════════════════════════════════════

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte Reserved;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    public static extern bool GetSystemPowerStatus(ref SYSTEM_POWER_STATUS lpSystemPowerStatus);

    [DllImport("kernel32.dll")]
    public static extern ulong GetTickCount64();

    // ═══════════════════════════════════════════════════════════════
    // GDI — screenshot
    // ═══════════════════════════════════════════════════════════════

    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;
    public const uint SRCCOPY = 0x00CC0020;

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateDC(string lpszDriver, string? lpszDevice, string? lpszOutput, IntPtr lpInitData);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    public static extern bool BitBlt(IntPtr hdc, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, uint rop);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr h);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    // ═══════════════════════════════════════════════════════════════
    // Virtual key map
    // ═══════════════════════════════════════════════════════════════

    public static readonly Dictionary<string, ushort> VkMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Alt"] = 0x12,        ["Ctrl"] = 0x11,       ["Shift"] = 0x10,
        ["Win"] = 0x5B,        ["Enter"] = 0x0D,       ["Tab"] = 0x09,
        ["Escape"] = 0x1B,     ["Esc"] = 0x1B,         ["Space"] = 0x20,
        ["Backspace"] = 0x08,  ["Delete"] = 0x2E,      ["Del"] = 0x2E,
        ["Insert"] = 0x2D,     ["Ins"] = 0x2D,         ["Home"] = 0x24,
        ["End"] = 0x23,        ["PgUp"] = 0x21,        ["PgDn"] = 0x22,
        ["Up"] = 0x26,         ["Down"] = 0x28,        ["Left"] = 0x25,
        ["Right"] = 0x27,      ["PrtSc"] = 0x2C,       ["PrintScreen"] = 0x2C,
        ["Scroll"] = 0x91,     ["Pause"] = 0x13,
        ["F1"] = 0x70,  ["F2"] = 0x71,  ["F3"] = 0x72,  ["F4"] = 0x73,
        ["F5"] = 0x74,  ["F6"] = 0x75,  ["F7"] = 0x76,  ["F8"] = 0x77,
        ["F9"] = 0x78,  ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B,
    };

    // ═══════════════════════════════════════════════════════════════
    // Window helpers
    // ═══════════════════════════════════════════════════════════════

    public static string GetWindowTitle(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string GetWindowClass(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    // ═══════════════════════════════════════════════════════════════
    // SendInput helpers
    // ═══════════════════════════════════════════════════════════════

    public static bool TryGetVk(string key, out ushort vk)
    {
        if (VkMap.TryGetValue(key, out vk))
            return true;

        if (key.Length == 1)
        {
            var result = VkKeyScan(key[0]);
            if (result != -1)
            {
                vk = (ushort)(result & 0xFF);
                return true;
            }
        }

        return false;
    }

    public static string SendCombo(string hotkey)
    {
        var parts = hotkey.Split('+', StringSplitOptions.TrimEntries);
        var inputs = new List<INPUT>();

        foreach (var part in parts)
        {
            if (!TryGetVk(part, out ushort vk))
                return $"无法识别的按键: '{part}'";

            bool isExtended = vk is 0x12 or 0x5B;
            var scan = (ushort)MapVirtualKey(vk, 0);

            inputs.Add(new INPUT
            {
                type = INPUT_KEYBOARD,
                ki = new KEYBDINPUT
                {
                    wVk = vk, wScan = scan,
                    dwFlags = KEYEVENTF_KEYDOWN | (isExtended ? KEYEVENTF_EXTENDEDKEY : 0),
                }
            });
        }

        for (int i = inputs.Count - 1; i >= 0; i--)
        {
            var ki = inputs[i].ki;
            ki.dwFlags = KEYEVENTF_KEYUP | (ki.dwFlags & KEYEVENTF_EXTENDEDKEY);
            inputs.Add(new INPUT { type = INPUT_KEYBOARD, ki = ki });
        }

        var array = inputs.ToArray();
        var sent = SendInput((uint)array.Length, array, Marshal.SizeOf<INPUT>());
        return sent == array.Length
            ? $"已发送按键: {hotkey}"
            : $"按键发送不全 (sent {sent}/{array.Length})";
    }

    public static string TypeTextImpl(string text)
    {
        ReleaseModifiers();

        var inputs = new List<INPUT>();

        foreach (var ch in text)
        {
            if (ch < ' ' && ch != '\t') continue;

            short scanResult = VkKeyScan(ch);
            if (scanResult != -1 && (scanResult & 0xFF) != 0xFF)
            {
                ushort vk = (ushort)(scanResult & 0xFF);
                bool needsShift = (scanResult & 0x100) != 0;
                if (needsShift)
                {
                    inputs.Add(new INPUT
                    {
                        type = INPUT_KEYBOARD,
                        ki = new KEYBDINPUT { wVk = 0x10, dwFlags = KEYEVENTF_KEYDOWN }
                    });
                }

                var scan = (ushort)MapVirtualKey(vk, 0);
                inputs.Add(new INPUT
                {
                    type = INPUT_KEYBOARD,
                    ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = KEYEVENTF_KEYDOWN }
                });
                inputs.Add(new INPUT
                {
                    type = INPUT_KEYBOARD,
                    ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = KEYEVENTF_KEYUP }
                });

                if (needsShift)
                {
                    inputs.Add(new INPUT
                    {
                        type = INPUT_KEYBOARD,
                        ki = new KEYBDINPUT { wVk = 0x10, dwFlags = KEYEVENTF_KEYUP }
                    });
                }
            }
            else
            {
                inputs.Add(new INPUT
                {
                    type = INPUT_KEYBOARD,
                    ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE }
                });
                inputs.Add(new INPUT
                {
                    type = INPUT_KEYBOARD,
                    ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP }
                });
            }
        }

        if (inputs.Count == 0) return "无有效字符可输入";

        var array = inputs.ToArray();
        var sent = SendInput((uint)array.Length, array, Marshal.SizeOf<INPUT>());
        return sent == array.Length
            ? $"已输入: {text}"
            : $"输入部分失败 (sent {sent}/{array.Length})";
    }

    public static void ReleaseModifiers()
    {
        var mods = new[] { (ushort)0x11, (ushort)0x12, (ushort)0x10, (ushort)0x5B };
        foreach (var vk in mods)
        {
            var inputs = new INPUT[1]
            {
                new INPUT
                {
                    type = INPUT_KEYBOARD,
                    ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP }
                }
            };
            SendInput(1, inputs, Marshal.SizeOf<INPUT>());
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // System info helpers
    // ═══════════════════════════════════════════════════════════════

    public static void AppendMemoryInfo(StringBuilder sb)
    {
        var mem = new MEMORYSTATUSEX();
        mem.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
        if (!GlobalMemoryStatusEx(ref mem))
        {
            sb.AppendLine("Memory: unavailable");
            return;
        }

        sb.AppendLine("=== Memory ===");
        sb.AppendLine($"  Total:     {FormatBytes(mem.ullTotalPhys)}");
        sb.AppendLine($"  Available: {FormatBytes(mem.ullAvailPhys)}");
        sb.AppendLine($"  Used:      {FormatBytes(mem.ullTotalPhys - mem.ullAvailPhys)}");
        sb.AppendLine($"  Usage:     {mem.dwMemoryLoad}%");
        sb.AppendLine();
    }

    public static void AppendDiskInfo(StringBuilder sb)
    {
        sb.AppendLine("=== Disk ===");
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
                sb.AppendLine($"  {drive.Name} ({drive.VolumeLabel})");
                sb.AppendLine($"    Total: {FormatBytes((ulong)drive.TotalSize)}");
                sb.AppendLine($"    Free:  {FormatBytes((ulong)drive.AvailableFreeSpace)}");
                sb.AppendLine($"    Used:  {FormatBytes((ulong)(drive.TotalSize - drive.AvailableFreeSpace))}");
            }
        }
        catch { sb.AppendLine("  (unavailable)"); }
        sb.AppendLine();
    }

    public static void AppendProcessInfo(StringBuilder sb)
    {
        sb.AppendLine("=== Top Processes by Memory ===");
        try
        {
            var procs = Process.GetProcesses()
                .Where(p => p.WorkingSet64 > 0)
                .OrderByDescending(p => p.WorkingSet64)
                .Take(10);

            foreach (var p in procs)
            {
                long ws;
                try { ws = p.WorkingSet64; }
                catch { continue; }
                sb.AppendLine($"  {p.ProcessName,-30} {FormatBytes((ulong)ws)}");
            }
        }
        catch { sb.AppendLine("  (unavailable)"); }
        sb.AppendLine();
    }

    public static void AppendBatteryInfo(StringBuilder sb)
    {
        var status = new SYSTEM_POWER_STATUS();
        if (!GetSystemPowerStatus(ref status) || status.BatteryFlag == 128)
        {
            sb.AppendLine("Battery: no battery (desktop)");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("=== Battery ===");
        var state = status.ACLineStatus switch { 1 => "charging", 0 => "discharging", _ => "unknown" };
        sb.AppendLine($"  State:    {state}");
        sb.AppendLine($"  Level:    {status.BatteryLifePercent}%");

        if (status.BatteryLifeTime >= 0)
            sb.AppendLine($"  Remaining: {TimeSpan.FromSeconds(status.BatteryLifeTime):h\\:mm}");

        if (status.BatteryFlag == 4)
            sb.AppendLine("  WARNING: Critical battery!");
        else if (status.BatteryFlag == 2)
            sb.AppendLine("  WARNING: Low battery!");
        sb.AppendLine();
    }

    public static string FormatBytes(ulong bytes) => bytes switch
    {
        >= 1073741824 => $"{bytes / 1073741824.0:F1} GB",
        >= 1048576 => $"{bytes / 1048576.0:F1} MB",
        >= 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes} B"
    };

    public static void CleanupGDI(IntPtr screenDC, IntPtr memDC, IntPtr bitmap, IntPtr oldBitmap)
    {
        SelectObject(memDC, oldBitmap);
        DeleteObject(bitmap);
        DeleteDC(memDC);
        DeleteDC(screenDC);
    }
}
