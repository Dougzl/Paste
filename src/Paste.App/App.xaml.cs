using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Paste.App.Services;
using Paste.App.Views.Windows;
using Paste.Core.Interfaces;
using Paste.Core.Models;
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
                services.AddSingleton<ISettingsService, SettingsService>();
                services.AddSingleton<IFavoriteFolderService, FavoriteFolderService>();

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

        var settingsService = _host.Services.GetRequiredService<ISettingsService>();
        var settings = settingsService.Load();
        ApplyThemeMode(settings.ThemeMode);

        // Run database migration (creates tables if missing, alters schema as needed)
        var contextFactory = _host.Services.GetRequiredService<IDbContextFactory<PasteDbContext>>();
        await DatabaseMigrator.MigrateAsync(contextFactory);

        // Auto-cleanup based on settings
        await RunAutoCleanupAsync();

        await _host.StartAsync();
    }

    public static void ApplyThemeMode(string? themeMode)
    {
        if (string.Equals(themeMode, "Light", StringComparison.OrdinalIgnoreCase))
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Light);
            return;
        }

        if (string.Equals(themeMode, "Dark", StringComparison.OrdinalIgnoreCase))
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Dark);
            return;
        }

        ApplicationThemeManager.ApplySystemTheme();
    }

    private async Task RunAutoCleanupAsync()
    {
        var settingsService = _host.Services.GetRequiredService<ISettingsService>();
        var settings = settingsService.Load();
        if (settings.AutoCleanupDays <= 0) return;

        var historyService = _host.Services.GetRequiredService<IClipboardHistoryService>();
        await historyService.CleanupExpiredAsync(settings.AutoCleanupDays);
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
