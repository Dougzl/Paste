using System.Windows.Interop;
using Paste.Core.Interfaces;

namespace Paste.App.Services;

public class GlobalHotkeyService : IGlobalHotkeyService
{
    private IntPtr _hwnd;
    private HwndSource? _hwndSource;

    public event EventHandler? HotkeyPressed;

    public void Register(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);

        NativeMethods.RegisterHotKey(
            _hwnd,
            NativeMethods.HOTKEY_ID,
            NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT,
            NativeMethods.VK_V);
    }

    public void Unregister()
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_hwnd, NativeMethods.HOTKEY_ID);
            _hwndSource?.RemoveHook(WndProc);
            _hwnd = IntPtr.Zero;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == NativeMethods.HOTKEY_ID)
        {
            handled = true;
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        GC.SuppressFinalize(this);
    }
}
