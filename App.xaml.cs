using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PersonalAssistant.Core.Interfaces;
using PersonalAssistant.Core.Services;
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
            {
                // 日志文件写入 %APPDATA%\PersonalAssistant\logs\
                var logDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PersonalAssistant", "logs");

                config
                    .ReadFrom.Configuration(context.Configuration)
                    .WriteTo.File(
                        System.IO.Path.Combine(logDir, "app-.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 7,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                    .WriteTo.File(
                        System.IO.Path.Combine(logDir, "errors-.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 30,
                        restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Error,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
            })
            .ConfigureServices((context, services) =>
            {
                // ═══════════════════════════════════════════════════════════
                // 1. 自动扫描 IToolPlugin → As<IToolPlugin>() → Singleton
                //    新增插件零 DI 配置 — 实现 IToolPlugin 即自动发现
                // ═══════════════════════════════════════════════════════════
                services.Scan(scan => scan
                    .FromApplicationDependencies(a =>
                        a.FullName!.StartsWith("PersonalAssistant"))
                    .AddClasses(c => c.AssignableTo<IToolPlugin>())
                    .As<IToolPlugin>()
                    .WithSingletonLifetime());

                // ═══════════════════════════════════════════════════════════
                // 2. 自动扫描 *Service（排除 IToolPlugin）→ AsImplementedInterfaces → Singleton
                // ═══════════════════════════════════════════════════════════
                services.Scan(scan => scan
                    .FromApplicationDependencies(a =>
                        a.FullName!.StartsWith("PersonalAssistant"))
                    .AddClasses(c => c
                        .Where(t => t.Name.EndsWith("Service")
                            && !typeof(IToolPlugin).IsAssignableFrom(t)))
                    .AsImplementedInterfaces()
                    .WithSingletonLifetime());

                // ═══════════════════════════════════════════════════════════
                // 3. 自动扫描 *ViewModel / *View → AsSelf → Singleton
                // ═══════════════════════════════════════════════════════════
                services.Scan(scan => scan
                    .FromApplicationDependencies(a =>
                        a.FullName!.StartsWith("PersonalAssistant"))
                    .AddClasses(c => c.Where(t =>
                        t.Name.EndsWith("ViewModel") || t.Name.EndsWith("View")))
                    .AsSelf()
                    .WithSingletonLifetime());

                // ═══════════════════════════════════════════════════════════
                // 4. 手动注册：无接口的具体类 + 平台核心组件
                // ═══════════════════════════════════════════════════════════

                // 用户设置（无接口）
                services.AddSingleton<UserSettingsService>();

                // 插件状态持久化（无接口，必须在 PluginAggregator 之前注册）
                services.AddSingleton<PluginStateService>();

                // 托盘服务（无接口，需提前初始化）
                services.AddSingleton<TrayService>();

                // 本地 LLM 模型服务（无接口，IDisposable）
                services.AddSingleton<Features.Chat.Services.LocalModelService>();

                // 模型路由服务（自动判断本地/远程，无接口）
                services.AddSingleton<Features.Chat.Services.ModelRoutingService>();

                // 工作流 / 学习能力服务（无接口）
                services.AddSingleton<Features.Workflow.Services.WorkflowRecorder>();
                services.AddSingleton<Features.Workflow.Services.PatternDetector>();
                services.AddSingleton<Features.Workflow.Services.WorkflowStorageService>();
                services.AddSingleton<Features.Workflow.Services.WorkflowExecutorService>();

                // 定时任务服务（无接口）
                services.AddSingleton<Features.Scheduler.Services.SchedulerStorageService>();
                services.AddSingleton<Features.Scheduler.Services.SchedulerService>();

                // MAF 聊天代理（无接口）
                services.AddSingleton<Features.Chat.Services.ChatAgentService>();

                // 插件聚合器：实现 IToolPluginHost + IDangerousToolPolicy
                services.AddSingleton<PluginAggregator>();
                services.AddSingleton<IToolPluginHost>(sp => sp.GetRequiredService<PluginAggregator>());
                services.AddSingleton<IDangerousToolPolicy>(sp => sp.GetRequiredService<PluginAggregator>());

                // 外部插件加载器
                services.AddSingleton<Core.Plugins.PluginLoader>();

                // 插件间共享状态
                services.AddSingleton<PluginSharedState>();

                // Window 类
                services.AddSingleton<MainWindow>();
                services.AddSingleton<Features.Mascot.MascotWindow>();
                services.AddTransient<Features.Settings.SettingsWindow>();
                services.AddTransient<Features.Plugins.PluginManagementWindow>();
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

        // 触发 SchedulerService 初始化（启动定时器）
        Services.GetRequiredService<Features.Scheduler.Services.SchedulerService>();

        // 后台预热本地模型（下载/加载，不阻塞 UI）
        _ = Task.Run(async () =>
        {
            try
            {
                var localModel = Services.GetRequiredService<
                    Features.Chat.Services.LocalModelService>();
                var result = await localModel.EnsureModelAvailableAsync();
                if (result is null)
                    Log.Information("[App] 本地模型预热完成");
                else
                    Log.Warning("[App] 本地模型预热未完成: {Msg}", result);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[App] 本地模型预热异常");
            }
        });

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
