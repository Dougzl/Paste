using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Paste.Core.Interfaces;
using Paste.Core.Models;
using Wpf.Ui.Controls;

namespace Paste.App.Views.Windows;

public partial class SettingsWindow : FluentWindow
{
    private readonly ISettingsService _settingsService;
    private int _capturedModifiers;
    private int _capturedKey;

    private static readonly Dictionary<Key, string> KeyDisplayNames = new()
    {
        { Key.OemTilde, "`" }, { Key.OemMinus, "-" }, { Key.OemPlus, "=" },
        { Key.OemOpenBrackets, "[" }, { Key.OemCloseBrackets, "]" },
        { Key.OemPipe, "\\" }, { Key.OemSemicolon, ";" }, { Key.OemQuotes, "'" },
        { Key.OemComma, "," }, { Key.OemPeriod, "." }, { Key.OemQuestion, "/" },
    };

    public SettingsWindow(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        var s = _settingsService.Load();
        _capturedModifiers = s.HotkeyModifiers;
        _capturedKey = s.HotkeyKey;
        HotkeyBox.Text = FormatHotkey(_capturedModifiers, _capturedKey);

        // Select the matching cleanup item
        foreach (ComboBoxItem item in CleanupCombo.Items)
        {
            if (item.Tag is string tag && int.TryParse(tag, out var days) && days == s.AutoCleanupDays)
            {
                CleanupCombo.SelectedItem = item;
                break;
            }
        }
    }

    private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Ignore standalone modifier presses
        if (key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            // Show partial capture feedback
            var partialMods = BuildModifiers();
            if (partialMods != 0)
                HotkeyBox.Text = FormatModifiers(partialMods) + " ...";
            return;
        }

        var modifiers = BuildModifiers();
        if (modifiers == 0) return; // Require at least one modifier

        _capturedModifiers = modifiers;
        _capturedKey = KeyInterop.VirtualKeyFromKey(key);
        HotkeyBox.Text = FormatHotkey(_capturedModifiers, _capturedKey);
    }

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
    }

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        HotkeyBox.Text = FormatHotkey(_capturedModifiers, _capturedKey);
    }

    private static int BuildModifiers()
    {
        int mods = 0;
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) mods |= 0x0002; // MOD_CONTROL
        if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt)) mods |= 0x0001;   // MOD_ALT
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) mods |= 0x0004; // MOD_SHIFT
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) mods |= 0x0008;           // MOD_WIN
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

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var cleanupDays = 0;
        if (CleanupCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            int.TryParse(tag, out cleanupDays);

        var settings = new AppSettings
        {
            HotkeyModifiers = _capturedModifiers,
            HotkeyKey = _capturedKey,
            AutoCleanupDays = cleanupDays
        };

        _settingsService.Save(settings);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
