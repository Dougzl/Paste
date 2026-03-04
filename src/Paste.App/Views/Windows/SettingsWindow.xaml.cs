using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Paste.App.Services;
using Paste.Core.Interfaces;
using Paste.Core.Models;
using Wpf.Ui.Controls;

namespace Paste.App.Views.Windows;

public partial class SettingsWindow : FluentWindow
{
    private readonly ISettingsService _settingsService;
    private readonly IClipboardHistoryService _historyService;
    private int _capturedModifiers;
    private int _capturedKey;
    private bool _isLoading;

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
        InitializeComponent();
        PreviewKeyDown += SettingsWindow_PreviewKeyDown;
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

        // Wire up toggle events after loading to avoid premature saves
        AutoRunToggle.Checked += ToggleChanged;
        AutoRunToggle.Unchecked += ToggleChanged;
        MinimizeToTrayToggle.Checked += ToggleChanged;
        MinimizeToTrayToggle.Unchecked += ToggleChanged;
        ShowTrayIconToggle.Checked += ToggleChanged;
        ShowTrayIconToggle.Unchecked += ToggleChanged;

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
            ShowTrayIcon = ShowTrayIconToggle.IsChecked == true
        };

        _settingsService.Save(settings);
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

    private void ToggleChanged(object sender, RoutedEventArgs e) => SaveSettings();

    private void CleanupSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => SaveSettings();

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
            Owner = this
        };

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
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        Close();
    }
}
