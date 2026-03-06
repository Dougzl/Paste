using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Paste.App.Services;
using Paste.Core.Interfaces;
using Paste.Core.Models;
using Wpf.Ui.Controls;

namespace Paste.App.Views.Windows;

public partial class SettingsWindow : FluentWindow
{
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    private const double MinScrollSpeed = 0.5;
    private const double MaxScrollSpeed = 10.0;
    private const int DefaultSourceFileCopyMaxSizeMb = 100;
    private const int MinSourceFileCopyMaxSizeMb = 1;
    private const int MaxSourceFileCopyMaxSizeMb = 2048;
    private readonly ISettingsService _settingsService;
    private readonly IClipboardHistoryService _historyService;
    private int _capturedModifiers;
    private int _capturedKey;
    private bool _isLoading;
    public bool ShouldHideOwnerOnClose { get; private set; }

    // Callback to refresh main window after clearing history
    public Action? OnHistoryClearedCallback { get; set; }

    // Slider index → days mapping: 天(1) 周(7) 月(30) 季度(90) 半年(180) 年(365) 无限制(0)
    private static readonly int[] SliderToDays = { 1, 7, 30, 90, 180, 365, 0 };

    private static readonly Dictionary<Key, string> KeyDisplayNames = new()
    {
        { Key.OemTilde, "`" }, { Key.OemMinus, "-" }, { Key.OemPlus, "=" },
        { Key.OemOpenBrackets, "[" }, { Key.OemCloseBrackets, "]" },
        { Key.OemPipe, "\\" }, { Key.OemSemicolon, ";" }, { Key.OemQuotes, "'" },
        { Key.OemComma, "," }, { Key.OemPeriod, "." }, { Key.OemQuestion, "/" },
    };

    public SettingsWindow(ISettingsService settingsService, IClipboardHistoryService historyService)
    {
        _settingsService = settingsService;
        _historyService = historyService;
        _isLoading = true;
        InitializeComponent();
        PreviewKeyDown += SettingsWindow_PreviewKeyDown;
        Deactivated += SettingsWindow_Deactivated;
        Loaded += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.DisableWindowResize(hwnd);
        };
        LoadSettings();
    }

    private void LoadSettings()
    {
        _isLoading = true;
        var s = _settingsService.Load();

        _capturedModifiers = s.HotkeyModifiers;
        _capturedKey = s.HotkeyKey;
        HotkeyBox.Text = FormatHotkey(_capturedModifiers, _capturedKey);

        // Map days to slider index
        var sliderIndex = Array.IndexOf(SliderToDays, s.AutoCleanupDays);
        CleanupSlider.Value = sliderIndex >= 0 ? sliderIndex : 6; // default to "无限制"

        AutoRunToggle.IsChecked = s.AutoRunOnStartup;
        MinimizeToTrayToggle.IsChecked = s.MinimizeToTrayOnStartup;
        ShowTrayIconToggle.IsChecked = s.ShowTrayIcon;
        CopySourceFilesToggle.IsChecked = s.CopySourceFiles;
        SourceFileMaxSizeBox.Text = NormalizeSourceFileCopyMaxSizeMb(s.SourceFileCopyMaxSizeMb).ToString();
        UpdateSourceFileControlsState();
        ThemeModeCombo.SelectedValue = NormalizeThemeMode(s.ThemeMode);
        ScrollSpeedSlider.Value = Clamp(s.ScrollSpeedMultiplier, MinScrollSpeed, MaxScrollSpeed);
        UpdateScrollSpeedText();

        // Wire up toggle events after loading to avoid premature saves
        AutoRunToggle.Checked += ToggleChanged;
        AutoRunToggle.Unchecked += ToggleChanged;
        MinimizeToTrayToggle.Checked += ToggleChanged;
        MinimizeToTrayToggle.Unchecked += ToggleChanged;
        ShowTrayIconToggle.Checked += ToggleChanged;
        ShowTrayIconToggle.Unchecked += ToggleChanged;
        CopySourceFilesToggle.Checked += ToggleChanged;
        CopySourceFilesToggle.Unchecked += ToggleChanged;

        _isLoading = false;
    }

    private void SaveSettings()
    {
        if (_isLoading) return;

        var sliderIndex = (int)CleanupSlider.Value;
        var cleanupDays = sliderIndex >= 0 && sliderIndex < SliderToDays.Length ? SliderToDays[sliderIndex] : 0;

        var settings = new AppSettings
        {
            HotkeyModifiers = _capturedModifiers,
            HotkeyKey = _capturedKey,
            AutoCleanupDays = cleanupDays,
            AutoRunOnStartup = AutoRunToggle.IsChecked == true,
            MinimizeToTrayOnStartup = MinimizeToTrayToggle.IsChecked == true,
            ShowTrayIcon = ShowTrayIconToggle.IsChecked == true,
            CopySourceFiles = CopySourceFilesToggle.IsChecked == true,
            SourceFileCopyMaxSizeMb = GetSourceFileCopyMaxSizeMb(),
            ThemeMode = GetSelectedThemeMode(),
            ScrollSpeedMultiplier = Clamp(ScrollSpeedSlider.Value, MinScrollSpeed, MaxScrollSpeed)
        };

        _settingsService.Save(settings);
        App.ApplyThemeMode(settings.ThemeMode);
        UpdateAutoRun(settings.AutoRunOnStartup);
    }

    private static void UpdateAutoRun(bool enable)
    {
        const string appName = "Paste";
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                    key.SetValue(appName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(appName, false);
            }
        }
        catch
        {
            // Silently ignore registry errors
        }
    }

    private void ToggleChanged(object sender, RoutedEventArgs e)
    {
        UpdateSourceFileControlsState();
        SaveSettings();
    }

    private void SourceFileMaxSizeBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = e.Text.Any(ch => !char.IsDigit(ch));
    }

    private void SourceFileMaxSizeBox_LostFocus(object sender, RoutedEventArgs e)
    {
        SaveSettings();
    }

    private void SourceFileMaxSizeBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        SaveSettings();
    }

    private void CleanupSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => SaveSettings();

    private void ThemeModeCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        SaveSettings();
    }

    private void ScrollSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ScrollSpeedValueText == null)
        {
            return;
        }

        UpdateScrollSpeedText();
        SaveSettings();
    }

    private int GetSourceFileCopyMaxSizeMb()
    {
        var normalized = DefaultSourceFileCopyMaxSizeMb;

        if (int.TryParse(SourceFileMaxSizeBox.Text, out var parsed))
        {
            normalized = NormalizeSourceFileCopyMaxSizeMb(parsed);
        }

        if (!string.Equals(SourceFileMaxSizeBox.Text, normalized.ToString(), StringComparison.Ordinal))
        {
            SourceFileMaxSizeBox.Text = normalized.ToString();
        }

        UpdateSourceFileControlsState();
        return normalized;
    }

    private static int NormalizeSourceFileCopyMaxSizeMb(int value)
    {
        if (value <= 0)
        {
            value = DefaultSourceFileCopyMaxSizeMb;
        }

        return Math.Clamp(value, MinSourceFileCopyMaxSizeMb, MaxSourceFileCopyMaxSizeMb);
    }

    private void UpdateSourceFileControlsState()
    {
        var enabled = CopySourceFilesToggle.IsChecked == true;
        SourceFileMaxSizeRow.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        SourceFileMaxSizeBox.IsEnabled = enabled;
    }

    private void ResetHotkey_Click(object sender, MouseButtonEventArgs e)
    {
        _capturedModifiers = 0x0001 | 0x0004; // ALT + SHIFT
        _capturedKey = 0x56; // VK_V
        HotkeyBox.Text = FormatHotkey(_capturedModifiers, _capturedKey);
        SaveSettings();
    }

    private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            var partialMods = BuildModifiers();
            if (partialMods != 0)
                HotkeyBox.Text = FormatModifiers(partialMods) + " ...";
            return;
        }

        var modifiers = BuildModifiers();
        if (modifiers == 0) return;

        _capturedModifiers = modifiers;
        _capturedKey = KeyInterop.VirtualKeyFromKey(key);
        HotkeyBox.Text = FormatHotkey(_capturedModifiers, _capturedKey);
        SaveSettings();
    }

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e) { }

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        HotkeyBox.Text = FormatHotkey(_capturedModifiers, _capturedKey);
    }

    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var uiMessageBox = new Wpf.Ui.Controls.MessageBox
        {
            Title = "清空历史记录",
            Content = "确定要清空所有历史记录吗？收藏夹中的内容将被保留。\n\n此操作不可撤销。",
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.Manual
        };

        uiMessageBox.Loaded += (_, _) => CenterDialogOnOwnerScreen(uiMessageBox, this);

        var result = await uiMessageBox.ShowDialogAsync();

        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            await _historyService.ClearAllAsync();
            // Refresh the main window
            OnHistoryClearedCallback?.Invoke();
        }
    }

    private static int BuildModifiers()
    {
        int mods = 0;
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) mods |= 0x0002;
        if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt)) mods |= 0x0001;
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) mods |= 0x0004;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) mods |= 0x0008;
        return mods;
    }

    private static string FormatHotkey(int modifiers, int vk)
    {
        var parts = new List<string>();
        if ((modifiers & 0x0002) != 0) parts.Add("Ctrl");
        if ((modifiers & 0x0001) != 0) parts.Add("Alt");
        if ((modifiers & 0x0004) != 0) parts.Add("Shift");
        if ((modifiers & 0x0008) != 0) parts.Add("Win");

        var key = KeyInterop.KeyFromVirtualKey(vk);
        var keyName = KeyDisplayNames.TryGetValue(key, out var dn) ? dn : key.ToString();
        parts.Add(keyName);

        return string.Join(" + ", parts);
    }

    private static string FormatModifiers(int modifiers)
    {
        var parts = new List<string>();
        if ((modifiers & 0x0002) != 0) parts.Add("Ctrl");
        if ((modifiers & 0x0001) != 0) parts.Add("Alt");
        if ((modifiers & 0x0004) != 0) parts.Add("Shift");
        if ((modifiers & 0x0008) != 0) parts.Add("Win");
        return string.Join(" + ", parts);
    }

    private void SettingsWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is Key.LeftAlt or Key.RightAlt || (e.Key == Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt))
        {
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        Close();
    }

    private void SettingsWindow_Deactivated(object? sender, EventArgs e)
    {
        if (!IsVisible || HasVisibleOwnedWindow())
        {
            return;
        }

        ShouldHideOwnerOnClose = ShouldHideOwnerAfterDeactivate();
        Close();
    }

    private bool HasVisibleOwnedWindow()
    {
        foreach (Window window in System.Windows.Application.Current.Windows)
        {
            if (window.Owner == this && window.IsVisible)
            {
                return true;
            }
        }

        return false;
    }

    private bool ShouldHideOwnerAfterDeactivate()
    {
        var foregroundWindow = NativeMethods.GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(foregroundWindow, out var processId);
        if (processId == 0 || processId == Environment.ProcessId)
        {
            return false;
        }

        return true;
    }

    private static void CenterDialogOnOwnerScreen(Window dialog, Window owner)
    {
        var ownerHandle = new WindowInteropHelper(owner).Handle;
        if (ownerHandle == IntPtr.Zero)
        {
            return;
        }

        if (!TryGetOwnerMonitorWorkArea(ownerHandle, out var workLeftPx, out var workTopPx, out var workWidthPx, out var workHeightPx))
        {
            return;
        }

        var source = PresentationSource.FromVisual(owner);
        var scaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var scaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
        if (scaleX <= 0) scaleX = 1.0;
        if (scaleY <= 0) scaleY = 1.0;

        var workLeft = workLeftPx / scaleX;
        var workTop = workTopPx / scaleY;
        var workWidth = workWidthPx / scaleX;
        var workHeight = workHeightPx / scaleY;

        var dialogWidth = dialog.ActualWidth > 0 ? dialog.ActualWidth : (double.IsNaN(dialog.Width) ? 420 : dialog.Width);
        var dialogHeight = dialog.ActualHeight > 0 ? dialog.ActualHeight : (double.IsNaN(dialog.Height) ? 220 : dialog.Height);

        var targetLeft = workLeft + (workWidth - dialogWidth) / 2.0;
        var targetTop = workTop + (workHeight - dialogHeight) / 2.0 - 24.0;

        var minLeft = workLeft;
        var maxLeft = workLeft + workWidth - dialogWidth;
        var minTop = workTop;
        var maxTop = workTop + workHeight - dialogHeight;

        if (maxLeft < minLeft) maxLeft = minLeft;
        if (maxTop < minTop) maxTop = minTop;

        dialog.Left = Math.Max(minLeft, Math.Min(targetLeft, maxLeft));
        dialog.Top = Math.Max(minTop, Math.Min(targetTop, maxTop));
    }

    private static bool TryGetOwnerMonitorWorkArea(IntPtr ownerHandle, out int left, out int top, out int width, out int height)
    {
        left = 0;
        top = 0;
        width = 0;
        height = 0;

        var monitor = MonitorFromWindow(ownerHandle, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        left = info.rcWork.Left;
        top = info.rcWork.Top;
        width = info.rcWork.Right - info.rcWork.Left;
        height = info.rcWork.Bottom - info.rcWork.Top;
        return width > 0 && height > 0;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private string GetSelectedThemeMode()
    {
        var selectedValue = ThemeModeCombo.SelectedValue as string;
        return NormalizeThemeMode(selectedValue);
    }

    private static string NormalizeThemeMode(string? themeMode)
    {
        if (string.Equals(themeMode, "Light", StringComparison.OrdinalIgnoreCase))
        {
            return "Light";
        }

        if (string.Equals(themeMode, "Dark", StringComparison.OrdinalIgnoreCase))
        {
            return "Dark";
        }

        return "System";
    }

    private void UpdateScrollSpeedText()
    {
        ScrollSpeedValueText.Text = $"{ScrollSpeedSlider.Value:F1}x";
    }

    private static double Clamp(double value, double min, double max)
        => Math.Max(min, Math.Min(value, max));
}
