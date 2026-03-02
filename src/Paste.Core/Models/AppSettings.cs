namespace Paste.Core.Models;

public class AppSettings
{
    /// <summary>Win32 modifier flags (e.g. MOD_ALT | MOD_SHIFT).</summary>
    public int HotkeyModifiers { get; set; } = 0x0001 | 0x0004; // ALT + SHIFT

    /// <summary>Win32 virtual-key code.</summary>
    public int HotkeyKey { get; set; } = 0x56; // VK_V

    /// <summary>Auto-cleanup interval in days. 0 = never.</summary>
    public int AutoCleanupDays { get; set; }

    /// <summary>Start app on Windows startup.</summary>
    public bool AutoRunOnStartup { get; set; }

    /// <summary>Minimize to tray on startup instead of showing window.</summary>
    public bool MinimizeToTrayOnStartup { get; set; } = true;

    /// <summary>Show tray icon in system tray.</summary>
    public bool ShowTrayIcon { get; set; } = true;
}
