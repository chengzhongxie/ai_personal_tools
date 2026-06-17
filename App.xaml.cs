using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PersonalAssistant.Features.Chat.Models;
using Serilog;

namespace PersonalAssistant;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private readonly IHost _host;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .UseSerilog((context, config) =>
                config.ReadFrom.Configuration(context.Configuration))
            .ConfigureServices((context, services) =>
            {
                // Configuration
                services.Configure<ChatSettings>(
                    context.Configuration.GetSection("ChatSettings"));

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
                services.AddSingleton<MainWindow>();
            })
            .Build();

        Services = _host.Services;

        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync(TimeSpan.FromSeconds(5));
        _host.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled dispatcher exception");
        MessageBox.Show($"An unexpected error occurred:\n\n{e.Exception.Message}",
            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
