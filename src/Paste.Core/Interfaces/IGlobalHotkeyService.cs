namespace Paste.Core.Interfaces;

public interface IGlobalHotkeyService : IDisposable
{
    event EventHandler? HotkeyPressed;
    void Register(IntPtr hwnd);
    void Register(IntPtr hwnd, int modifiers, int key);
    void Unregister();
}
