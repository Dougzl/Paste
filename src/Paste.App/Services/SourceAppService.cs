using System.Diagnostics;
using Paste.Core.Interfaces;

namespace Paste.App.Services;

public class SourceAppService : ISourceAppService
{
    public (string? appName, string? appPath) GetForegroundAppInfo()
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return (null, null);

            NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
            if (processId == 0)
                return (null, null);

            var process = Process.GetProcessById((int)processId);
            var appName = process.ProcessName;
            string? appPath = null;

            try
            {
                appPath = process.MainModule?.FileName;
            }
            catch
            {
                // Access denied for some system processes
            }

            return (appName, appPath);
        }
        catch
        {
            return (null, null);
        }
    }
}
