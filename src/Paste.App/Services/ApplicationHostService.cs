using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Paste.Core.Interfaces;
using Paste.App.Views.Windows;
using System.Windows.Interop;

namespace Paste.App.Services;

public class ApplicationHostService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;

    public ApplicationHostService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
        var settings = settingsService.Load();

        if (settings.MinimizeToTrayOnStartup)
        {
            // Silent startup: initialize handle/background services without showing window.
            mainWindow.Opacity = 0;
            _ = new WindowInteropHelper(mainWindow).EnsureHandle();
        }
        else
        {
            mainWindow.Opacity = 1;
            mainWindow.ShowAtBottomAndActivate();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
