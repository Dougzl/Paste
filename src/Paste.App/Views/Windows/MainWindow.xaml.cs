using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Paste.App.Services;
using Paste.Core.Interfaces;
using Paste.Core.Models;
using Paste.UI.ViewModels;
using Paste.UI.Views.Pages;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Application = System.Windows.Application;

namespace Paste.App.Views.Windows;

public partial class MainWindow : FluentWindow
{
    private readonly IClipboardMonitor _clipboardMonitor;
    private readonly IGlobalHotkeyService _hotkeyService;
    private readonly ISettingsService _settingsService;
    private readonly IClipboardHistoryService _clipboardHistoryService;
    private readonly ClipboardHistoryViewModel _historyViewModel;
    private readonly IPasteService _pasteService;
    private readonly IServiceProvider _serviceProvider;
    private IntPtr _lastForegroundWindow;
    private bool _isHidingProgrammatically;

    public MainWindow(
        MainWindowViewModel mainViewModel,
        ClipboardHistoryViewModel historyViewModel,
        IClipboardMonitor clipboardMonitor,
        IGlobalHotkeyService hotkeyService,
        ISettingsService settingsService,
        IClipboardHistoryService clipboardHistoryService,
        IPasteService pasteService,
        IServiceProvider serviceProvider)
    {
        DataContext = mainViewModel;
        _historyViewModel = historyViewModel;
        _clipboardMonitor = clipboardMonitor;
        _hotkeyService = hotkeyService;
        _settingsService = settingsService;
        _clipboardHistoryService = clipboardHistoryService;
        _pasteService = pasteService;
        _serviceProvider = serviceProvider;

        InitializeComponent();

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        StateChanged += MainWindow_StateChanged;
        Deactivated += MainWindow_Deactivated;
        PreviewKeyDown += MainWindow_PreviewKeyDown;

        _settingsService.SettingsChanged += OnSettingsChanged;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.DisableWindowResize(hwnd);

        // Register global hotkey from settings
        var settings = _settingsService.Load();
        _hotkeyService.Register(hwnd, settings.HotkeyModifiers, settings.HotkeyKey);
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;

        // Start clipboard monitoring
        _clipboardMonitor.ClipboardChanged += OnClipboardChanged;
        _clipboardMonitor.Start();

        // Setup ViewModel
        _historyViewModel.PasteService = _pasteService;
        _historyViewModel.HideWindowAction = () =>
        {
            _isHidingProgrammatically = true;
            Hide();
            _isHidingProgrammatically = false;
        };
        _historyViewModel.ShowSettingsAction = async () =>
        {
            var settingsWindow = new SettingsWindow(_settingsService, _clipboardHistoryService)
            {
                Owner = this,
                OnHistoryClearedCallback = async () => await _historyViewModel.LoadEntriesAsync()
            };
            settingsWindow.ShowDialog();
        };

        // Watch for system theme changes
        SystemThemeWatcher.Watch(this);

        // Host the history page directly
        var historyPage = _serviceProvider.GetService(typeof(HistoryPage)) as HistoryPage;
        if (historyPage != null)
        {
            ContentHost.Content = historyPage;
        }

        // Position window at bottom of screen, full width
        PositionAtBottom();

        // Load initial data
        await _historyViewModel.LoadEntriesAsync();
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        Dispatcher.Invoke(() =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            _hotkeyService.Register(hwnd, settings.HotkeyModifiers, settings.HotkeyKey);
        });
    }

    private void PositionAtBottom()
    {
        // Get the working area of the screen where the mouse cursor is
        var mousePos = System.Windows.Forms.Cursor.Position;
        var screen = System.Windows.Forms.Screen.FromPoint(mousePos);
        var workArea = screen.WorkingArea;

        var (scaleX, scaleY) = GetDpiScaleAtPoint(mousePos);

        var screenLeft = workArea.Left / scaleX;
        var screenWidth = workArea.Width / scaleX;
        var screenBottom = (workArea.Top + workArea.Height) / scaleY;

        // Fallback guard for any unexpected DPI API result.
        if (screenWidth < 300)
        {
            screenLeft = SystemParameters.WorkArea.Left;
            screenWidth = SystemParameters.WorkArea.Width;
            screenBottom = SystemParameters.WorkArea.Bottom;
        }

        Width = screenWidth;
        MinWidth = screenWidth;
        MaxWidth = screenWidth;
        MinHeight = Height;
        MaxHeight = Height;
        Left = screenLeft;
        Top = screenBottom - Height;
    }

    private (double ScaleX, double ScaleY) GetDpiScaleAtPoint(System.Drawing.Point point)
    {
        var monitor = NativeMethods.MonitorFromPoint(
            new NativeMethods.POINT(point.X, point.Y),
            NativeMethods.MONITOR_DEFAULTTONEAREST);

        if (monitor != IntPtr.Zero &&
            NativeMethods.TryGetMonitorDpi(monitor, out var dpiX, out var dpiY) &&
            dpiX > 0 && dpiY > 0)
        {
            return (dpiX / 96.0, dpiY / 96.0);
        }

        // Fallback to WPF transform if monitor DPI is unavailable.
        var source = PresentationSource.FromVisual(this);
        var scaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var scaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        if (scaleX <= 0) scaleX = 1.0;
        if (scaleY <= 0) scaleY = 1.0;

        return (scaleX, scaleY);
    }

    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Hide();
        }
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        // Hide when window loses focus, unless we're already hiding programmatically (e.g. paste action)
        if (!_isHidingProgrammatically && IsVisible)
        {
            Hide();
        }
    }

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (IsVisible && WindowState != WindowState.Minimized)
            {
                Hide();
            }
            else
            {
                // Capture foreground window before showing
                _lastForegroundWindow = NativeMethods.GetForegroundWindow();
                _historyViewModel.LastForegroundWindow = _lastForegroundWindow;

                // Re-position before showing (mouse may have moved to different monitor)
                PositionAtBottom();

                Show();
                WindowState = WindowState.Normal;
                Activate();
            }
        });
    }

    private async void OnClipboardChanged(object? sender, Core.Models.ClipboardEntry entry)
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            await _historyViewModel.HandleClipboardChangedAsync(entry);
        });
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Minimize to tray instead of closing
        e.Cancel = true;
        Hide();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    private void TrayIcon_OnLeftDoubleClick(object sender, RoutedEventArgs e)
    {
        ShowAndActivate();
    }

    private void TrayMenu_Show_Click(object sender, RoutedEventArgs e)
    {
        ShowAndActivate();
    }

    private void TrayMenu_Exit_Click(object sender, RoutedEventArgs e)
    {
        _clipboardMonitor.Stop();
        _hotkeyService.Unregister();
        Closing -= MainWindow_Closing;
        Application.Current.Shutdown();
    }

    private void ShowAndActivate()
    {
        _lastForegroundWindow = NativeMethods.GetForegroundWindow();
        _historyViewModel.LastForegroundWindow = _lastForegroundWindow;

        PositionAtBottom();
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }
}
