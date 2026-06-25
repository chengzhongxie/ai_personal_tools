using System.Diagnostics;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using Timer = System.Timers.Timer;

namespace PersonalAssistant.Features.Widgets;

public partial class SystemStatusWidget : UserControl
{
    private readonly PerformanceCounter _cpuCounter;
    private readonly Timer _timer;

    public SystemStatusWidget()
    {
        InitializeComponent();
        _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        _timer = new Timer(5000) { AutoReset = true };
        _timer.Elapsed += (_, _) => Dispatcher.Invoke(Refresh);
        Loaded += (_, _) =>
        {
            Refresh();
            _timer.Start();
        };
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            _timer.Dispose();
            _cpuCounter.Dispose();
        };
    }

    private void Refresh()
    {
        try
        {
            var cpu = _cpuCounter.NextValue();
            CpuBar.Value = cpu;
            CpuText.Text = $"{cpu:F0}%";

            var totalMem = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            var usedMem = GC.GetTotalMemory(false);
            var percent = totalMem > 0 ? (double)usedMem / totalMem * 100 : 0;
            MemBar.Value = percent;
            MemText.Text = $"{usedMem / 1024 / 1024:N0} MB";
        }
        catch
        {
            CpuText.Text = "--";
            MemText.Text = "--";
        }
    }
}
