namespace Paste.Core.Interfaces;

public interface ISourceAppService
{
    (string? appName, string? appPath) GetForegroundAppInfo();
}
