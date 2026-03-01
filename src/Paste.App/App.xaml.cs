using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Paste.App.Services;
using Paste.App.Views.Windows;
using Paste.Core.Interfaces;
using Paste.Data.Database;
using Paste.Data.Services;
using Paste.Data.Storage;
using Paste.UI.ViewModels;
using Paste.UI.Views.Pages;
using Wpf.Ui.Appearance;
using Application = System.Windows.Application;

namespace Paste.App;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                // Database
                services.AddDbContextFactory<PasteDbContext>(options =>
                {
                    var dbPath = PasteDbContextFactory.GetDefaultDbPath();
                    options.UseSqlite($"Data Source={dbPath}");
                });

                // Services
                services.AddSingleton<ISourceAppService, SourceAppService>();
                services.AddSingleton<IImageStorageService, ImageStorageService>();
                services.AddSingleton<IClipboardMonitor, ClipboardMonitor>();
                services.AddSingleton<IGlobalHotkeyService, GlobalHotkeyService>();
                services.AddSingleton<IPasteService, PasteService>();
                services.AddSingleton<IClipboardHistoryService, ClipboardHistoryService>();

                // ViewModels
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<ClipboardHistoryViewModel>();

                // Views
                services.AddSingleton<MainWindow>();
                services.AddTransient<HistoryPage>();

                // Hosted service
                services.AddHostedService<ApplicationHostService>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Apply system theme and watch for changes
        ApplicationThemeManager.ApplySystemTheme();

        // Ensure database is created
        var contextFactory = _host.Services.GetRequiredService<IDbContextFactory<PasteDbContext>>();
        await using var db = await contextFactory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();

        await _host.StartAsync();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        var monitor = _host.Services.GetRequiredService<IClipboardMonitor>();
        monitor.Stop();

        var hotkey = _host.Services.GetRequiredService<IGlobalHotkeyService>();
        hotkey.Unregister();

        await _host.StopAsync();
        _host.Dispose();

        base.OnExit(e);
    }

    private void App_OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Console.WriteLine($"[UNHANDLED EXCEPTION] {e.Exception}");
        e.Handled = true;
    }
}
