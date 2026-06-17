using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PersonalAssistant.Infrastructure.Common.Services;
using Serilog;

namespace PersonalAssistant;

/// <summary>
/// WPF 应用程序入口，负责 DI 容器初始化、Serilog 配置和全局异常处理
/// </summary>
public partial class App : Application
{
    /// <summary>全局 DI 服务提供器，供 View 无参构造函数使用</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    private readonly IHost _host;

    /// <summary>
    /// 初始化应用程序，构建 DI 容器并注册所有服务
    /// </summary>
    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .UseSerilog((context, config) =>
                config.ReadFrom.Configuration(context.Configuration))
            .ConfigureServices((context, services) =>
            {
                // Configuration
                // ChatSettings 不再从 appsettings.json 读取，改用 UserSettingsService
                // Serilog 仍从 appsettings.json 读取

                // Scrutor auto-scan: *Service → AsImplementedInterfaces, Singleton
                services.Scan(scan => scan
                    .FromApplicationDependencies(a =>
                        a.FullName!.StartsWith("PersonalAssistant"))
                    .AddClasses(c => c.Where(t => t.Name.EndsWith("Service")))
                    .AsImplementedInterfaces()
                    .WithSingletonLifetime());

                // Scrutor auto-scan: *ViewModel / *View → AsSelf, Singleton
                services.Scan(scan => scan
                    .FromApplicationDependencies(a =>
                        a.FullName!.StartsWith("PersonalAssistant"))
                    .AddClasses(c => c.Where(t =>
                        t.Name.EndsWith("ViewModel") || t.Name.EndsWith("View")))
                    .AsSelf()
                    .WithSingletonLifetime());

                // Manual registrations
                services.AddSingleton<UserSettingsService>();
                services.AddSingleton<TrayService>();
                services.AddSingleton<MainWindow>();
                services.AddSingleton<PersonalAssistant.Features.Mascot.MascotWindow>();
                services.AddSingleton<PersonalAssistant.Features.Settings.SettingsWindow>();
            })
            .Build();

        Services = _host.Services;

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <summary>
    /// 启动 Host 并显示主窗口
    /// </summary>
    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        // 触发 TrayService 初始化（托盘图标 + 注册表读取）
        Services.GetRequiredService<TrayService>();

        base.OnStartup(e);
    }

    /// <summary>
    /// 停止 Host 并释放资源
    /// </summary>
    protected override async void OnExit(ExitEventArgs e)
    {
        Services.GetRequiredService<TrayService>().Dispose();
        await _host.StopAsync(TimeSpan.FromSeconds(5));
        _host.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// 全局未处理异常兜底：记录日志并弹窗提示用户
    /// </summary>
    private void OnDispatcherUnhandledException(object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "未处理的调度器异常");
        MessageBox.Show($"发生未知错误:\n\n{e.Exception.Message}",
            "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    /// <summary>
    /// 未观察到的 Task 异常兜底：记日志防止静默进程崩溃
    /// </summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "未观察到的任务异常");
        e.SetObserved();
    }
}
