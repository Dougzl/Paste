namespace Paste.Core.Interfaces;

public interface IGlobalHotkeyService : IDisposable
{
    event EventHandler? HotkeyPressed;
    void Register(IntPtr hwnd);
    void Unregister();
}
