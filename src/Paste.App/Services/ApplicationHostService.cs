using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Paste.Core.Interfaces;
using Paste.App.Views.Windows;
using System.Windows;

namespace Paste.App.Services;

public class ApplicationHostService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ISettingsService _settingsService;

    public ApplicationHostService(IServiceProvider serviceProvider, ISettingsService settingsService)
    {
        _serviceProvider = serviceProvider;
        _settingsService = settingsService;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        var settings = _settingsService.Load();

        if (settings.MinimizeToTrayOnStartup)
        {
            // Silent startup: initialize the window while kept invisible and minimized.
            mainWindow.Opacity = 0;
            mainWindow.ShowActivated = false;
            mainWindow.WindowState = WindowState.Minimized;
            mainWindow.Left = -32000;
            mainWindow.Top = -32000;
            mainWindow.Show();
            mainWindow.Hide();
            mainWindow.WindowState = WindowState.Normal;
            mainWindow.Opacity = 1;
            mainWindow.ShowActivated = true;
        }
        else
        {
            mainWindow.Show();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
