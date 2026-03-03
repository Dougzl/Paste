using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        mainWindow.Opacity = 0;
        _ = new WindowInteropHelper(mainWindow).EnsureHandle();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
