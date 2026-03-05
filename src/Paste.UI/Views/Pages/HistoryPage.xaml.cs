using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using Paste.Core.Interfaces;
using Paste.Core.Models;
using Paste.UI.ViewModels;

namespace Paste.UI.Views.Pages;

public partial class HistoryPage : UserControl
{
    private const int WM_MOUSEHWHEEL = 0x020E;
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    private const double DefaultCardWheelStep = 0.8;
    private const double DefaultFilterWheelStep = 0.5;
    private readonly ClipboardHistoryViewModel _viewModel;
    private readonly ISettingsService _settingsService;
    private HwndSource? _hwndSource;
    private double _scrollSpeedMultiplier = 1.0;
    private bool _settingsEventsHooked;

    // Drag-to-scroll state
    private bool _isMouseDown;
    private bool _isDragging;
    private Point _dragStartPoint;
    private double _dragStartOffset;
    private readonly List<(DateTime Time, double Offset)> _dragSamples = new();
    private const double DragThreshold = 5.0;
    private const double MaxVelocity = 60.0;
    private int _currentSelectionIndex = -1;

    // Inertia / smooth scroll animation
    private double _velocity;
    private double _smoothTarget;
    private bool _hasSmoothTarget;
    private DispatcherTimer? _animationTimer;
    private CancellationTokenSource? _folderClickDelayCts;
    private const double InertiaFriction = 0.95;
    private const double MinVelocity = 0.5;
    private const double SmoothLerpFactor = 0.15;

    public HistoryPage(ClipboardHistoryViewModel viewModel, ISettingsService settingsService)
    {
        _viewModel = viewModel;
        _settingsService = settingsService;
        DataContext = _viewModel;
        InitializeComponent();
        UpdateScrollSpeedFromSettings(_settingsService.Load());
        CardList.LostMouseCapture += CardList_LostMouseCapture;
        CardList.SelectionChanged += CardList_SelectionChanged;
        Loaded += HistoryPage_Loaded;
        Unloaded += HistoryPage_Unloaded;
    }

    private void HistoryPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_settingsEventsHooked)
        {
            _settingsService.SettingsChanged += OnSettingsChanged;
            _settingsEventsHooked = true;
        }

        var window = Window.GetWindow(this);
        if (window != null)
        {
            var helper = new WindowInteropHelper(window);
            _hwndSource = HwndSource.FromHwnd(helper.Handle);
            _hwndSource?.AddHook(WndProc);
        }
    }

    private void HistoryPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _hwndSource?.RemoveHook(WndProc);
        if (_settingsEventsHooked)
        {
            _settingsService.SettingsChanged -= OnSettingsChanged;
            _settingsEventsHooked = false;
        }
        CardList.SelectionChanged -= CardList_SelectionChanged;
        StopAnimation();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEHWHEEL)
        {
            var delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
            if (AppFilterScrollViewer.IsMouseOver)
            {
                ScrollAppFilters(delta);
                handled = true;
            }
            else if (CardList.IsMouseOver)
            {
                var scrollViewer = GetScrollViewer(CardList);
                if (scrollViewer != null)
                {
                    if (!_hasSmoothTarget)
                        _smoothTarget = scrollViewer.HorizontalOffset;

                    _smoothTarget += delta * GetCardWheelStep();
                    _smoothTarget = Clamp(_smoothTarget, 0, scrollViewer.ScrollableWidth);
                    _velocity = 0;
                    _hasSmoothTarget = true;
                    StartAnimation();
                    handled = true;
                }
            }
        }
        return IntPtr.Zero;
    }

    // --- Animation timer ---

    private void EnsureAnimationTimer()
    {
        if (_animationTimer != null) return;
        _animationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _animationTimer.Tick += AnimationTimer_Tick;
    }

    private void StartAnimation()
    {
        EnsureAnimationTimer();
        if (!_animationTimer!.IsEnabled)
            _animationTimer.Start();
    }

    private void StopAnimation()
    {
        _animationTimer?.Stop();
        _velocity = 0;
        _hasSmoothTarget = false;
    }

    private void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        var sv = GetScrollViewer(CardList);
        if (sv == null) { StopAnimation(); return; }

        var current = sv.HorizontalOffset;
        var newOffset = current;
        var shouldStop = true;

        if (Math.Abs(_velocity) > MinVelocity)
        {
            newOffset += _velocity;
            _velocity *= InertiaFriction;
            shouldStop = false;
        }
        else
        {
            _velocity = 0;
        }

        if (_hasSmoothTarget)
        {
            var diff = _smoothTarget - newOffset;
            if (Math.Abs(diff) > 0.5)
            {
                newOffset += diff * SmoothLerpFactor;
                shouldStop = false;
            }
            else
            {
                newOffset = _smoothTarget;
                _hasSmoothTarget = false;
            }
        }

        newOffset = Clamp(newOffset, 0, sv.ScrollableWidth);
        sv.ScrollToHorizontalOffset(newOffset);

        if (shouldStop)
            StopAnimation();
    }

    // --- Drag-to-scroll ---

    private void CardList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        StopAnimation();
        _isMouseDown = true;
        _isDragging = false;
        Mouse.OverrideCursor = null;
        _dragStartPoint = e.GetPosition(CardList);
        var sv = GetScrollViewer(CardList);
        _dragStartOffset = sv?.HorizontalOffset ?? 0;
        _dragSamples.Clear();
        _dragSamples.Add((DateTime.Now, _dragStartOffset));
    }

    private void CardList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isMouseDown) return;
        if (_isDragging && e.LeftButton != MouseButtonState.Pressed)
        {
            ResetDragState();
            return;
        }

        var currentPos = e.GetPosition(CardList);
        var deltaX = currentPos.X - _dragStartPoint.X;

        if (!_isDragging)
        {
            if (Math.Abs(deltaX) > DragThreshold)
            {
                _isDragging = true;
                CardList.CaptureMouse();
                Mouse.OverrideCursor = Cursors.Hand;
            }
            else return;
        }

        var sv = GetScrollViewer(CardList);
        if (sv != null)
        {
            var newOffset = _dragStartOffset - deltaX;
            sv.ScrollToHorizontalOffset(Clamp(newOffset, 0, sv.ScrollableWidth));

            _dragSamples.Add((DateTime.Now, sv.HorizontalOffset));
            while (_dragSamples.Count > 5)
                _dragSamples.RemoveAt(0);
        }

        e.Handled = true;
    }

    private void CardList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isMouseDown) return;
        _isMouseDown = false;
        Mouse.OverrideCursor = null;

        if (_isDragging)
        {
            _isDragging = false;
            CardList.ReleaseMouseCapture();

            if (_dragSamples.Count >= 2)
            {
                var first = _dragSamples[0];
                var last = _dragSamples[^1];
                var dt = (last.Time - first.Time).TotalMilliseconds;
                if (dt > 0 && dt < 300)
                {
                    var v = -(last.Offset - first.Offset) / dt * 16;
                    _velocity = Clamp(v, -MaxVelocity, MaxVelocity);
                    if (Math.Abs(_velocity) > MinVelocity)
                        StartAnimation();
                }
            }

            e.Handled = true;
        }
    }

    private void CardList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_isDragging || (_isMouseDown && Mouse.LeftButton != MouseButtonState.Pressed))
        {
            ResetDragState();
        }

        var scrollViewer = GetScrollViewer(CardList);
        if (scrollViewer != null)
        {
            if (!_hasSmoothTarget)
                _smoothTarget = scrollViewer.HorizontalOffset;

            _smoothTarget -= e.Delta * GetCardWheelStep();
            _smoothTarget = Clamp(_smoothTarget, 0, scrollViewer.ScrollableWidth);
            _velocity = 0;
            _hasSmoothTarget = true;
            StartAnimation();
        }
        e.Handled = true;
    }

    // --- Card event handlers ---

    private void CardList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _viewModel.PasteSelectedCommand.Execute(null);
    }

    private async void CardList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _viewModel.PasteSelectedCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            MoveSelectionBy(1);
            e.Handled = true;
        }
        else if (e.Key == Key.Left)
        {
            MoveSelectionBy(-1);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            var entry = ResolveCurrentEntryForKeyboardDelete();
            if (entry != null)
            {
                var indexBeforeDelete = _currentSelectionIndex >= 0 ? _currentSelectionIndex : CardList.SelectedIndex;

                if (entry.FavoriteFolderId is > 0)
                {
                    if (!await ConfirmDeleteFavoriteEntryAsync())
                    {
                        e.Handled = true;
                        return;
                    }
                }

                await _viewModel.DeleteEntryCommand.ExecuteAsync(entry);
                SelectByIndex(indexBeforeDelete);
                e.Handled = true;
            }
        }
    }

    private void HistoryPage_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        if (Keyboard.FocusedElement is TextBox)
        {
            return;
        }

        if (e.Key is Key.Delete or Key.Left or Key.Right or Key.Enter)
        {
            CardList_PreviewKeyDown(CardList, e);
        }
    }

    private void CardList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _currentSelectionIndex = CardList.SelectedIndex;
    }

    private void MoveSelectionBy(int delta)
    {
        if (_viewModel.Entries.Count == 0)
        {
            return;
        }

        var baseIndex = _currentSelectionIndex;
        if (baseIndex < 0)
        {
            baseIndex = CardList.SelectedIndex;
        }
        if (baseIndex < 0)
        {
            baseIndex = 0;
        }

        var targetIndex = Math.Clamp(baseIndex + delta, 0, _viewModel.Entries.Count - 1);
        SelectByIndex(targetIndex);
    }

    private void SelectByIndex(int index)
    {
        if (_viewModel.Entries.Count == 0)
        {
            _currentSelectionIndex = -1;
            _viewModel.SelectedEntry = null;
            return;
        }

        var clamped = Math.Clamp(index, 0, _viewModel.Entries.Count - 1);
        _currentSelectionIndex = clamped;
        var entry = _viewModel.Entries[clamped];
        _viewModel.SelectedEntry = entry;
        CardList.SelectedIndex = clamped;
        CardList.ScrollIntoView(entry);
    }

    private ClipboardEntry? ResolveCurrentEntryForKeyboardDelete()
    {
        if (_viewModel.SelectedEntry != null)
        {
            return _viewModel.SelectedEntry;
        }

        if (_viewModel.Entries.Count == 0)
        {
            return null;
        }

        var index = _currentSelectionIndex >= 0 ? _currentSelectionIndex : CardList.SelectedIndex;
        if (index < 0)
        {
            index = 0;
        }

        index = Math.Clamp(index, 0, _viewModel.Entries.Count - 1);
        return _viewModel.Entries[index];
    }

    // --- Top bar handlers ---

    private void TopMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu != null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void TopMenu_Settings_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenSettingsCommand.Execute(null);
    }

    private void TopMenu_Exit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void AppFilterChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is SourceAppFilter filter)
        {
            _viewModel.ToggleAppFilterCommand.Execute(filter);
        }
    }

    private void AppFilterScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ScrollAppFilters(e.Delta);
        e.Handled = true;
    }

    private void CardList_LostMouseCapture(object sender, MouseEventArgs e)
    {
        ResetDragState();
    }

    private void ResetDragState()
    {
        _isMouseDown = false;
        _isDragging = false;
        _dragSamples.Clear();
        Mouse.OverrideCursor = null;
        if (CardList.IsMouseCaptured)
        {
            CardList.ReleaseMouseCapture();
        }
    }

    private void TopFilterBar_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ScrollAppFilters(e.Delta);
        e.Handled = true;
    }

    private void ScrollAppFilters(int delta)
    {
        AppFilterScrollViewer.ScrollToHorizontalOffset(
            Clamp(
                AppFilterScrollViewer.HorizontalOffset + delta * GetFilterWheelStep(),
                0,
                AppFilterScrollViewer.ScrollableWidth));
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        Dispatcher.Invoke(() => UpdateScrollSpeedFromSettings(settings));
    }

    private void UpdateScrollSpeedFromSettings(AppSettings settings)
    {
        _scrollSpeedMultiplier = Clamp(settings.ScrollSpeedMultiplier, 0.5, 10.0);
    }

    private double GetCardWheelStep() => DefaultCardWheelStep * _scrollSpeedMultiplier;

    private double GetFilterWheelStep() => DefaultFilterWheelStep * _scrollSpeedMultiplier;

    // --- Favorites handlers ---

    private async void FolderChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is FavoriteFolderViewModel folder)
        {
            if (e.ClickCount >= 2)
            {
                _folderClickDelayCts?.Cancel();
                BeginFolderRename(fe, folder);
                e.Handled = true;
                return;
            }

            if (e.ClickCount != 1 || folder.IsEditing)
            {
                return;
            }

            _folderClickDelayCts?.Cancel();
            _folderClickDelayCts = new CancellationTokenSource();
            var token = _folderClickDelayCts.Token;

            try
            {
                const int DoubleClickDelayMs = 300;
                await Task.Delay(DoubleClickDelayMs, token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (!token.IsCancellationRequested && !folder.IsEditing)
            {
                _viewModel.SelectFolderCommand.Execute(folder);
            }
        }
    }

    private void FolderChip_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            _folderClickDelayCts?.Cancel();
        }
    }

    private void FolderEditBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb && tb.DataContext is FavoriteFolderViewModel folder)
        {
            CommitFolderRename(folder);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && sender is TextBox && ((TextBox)sender).DataContext is FavoriteFolderViewModel f)
        {
            f.IsEditing = false;
            e.Handled = true;
        }
    }

    private void FolderEditBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is FavoriteFolderViewModel folder && folder.IsEditing)
        {
            CommitFolderRename(folder);
        }
    }

    private void CommitFolderRename(FavoriteFolderViewModel folder)
    {
        folder.IsEditing = false;
        if (!string.IsNullOrWhiteSpace(folder.Name))
        {
            _viewModel.RenameFolderCommand.Execute((folder.Id, folder.Name.Trim()));
        }
    }

    private void BeginFolderRename(FrameworkElement folderElement, FavoriteFolderViewModel targetFolder)
    {
        foreach (var folder in _viewModel.FavoriteFolders)
        {
            if (!ReferenceEquals(folder, targetFolder) && folder.IsEditing)
            {
                CommitFolderRename(folder);
            }
        }

        targetFolder.IsEditing = true;

        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            var textBox = FindVisualChild<TextBox>(folderElement);
            if (textBox != null)
            {
                textBox.Focus();
                textBox.SelectAll();
            }
        });
    }

    private void FolderChip_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is FavoriteFolderViewModel folder)
        {
            var menu = new ContextMenu();

            var renameItem = new MenuItem { Header = "重命名" };
            renameItem.Click += (_, _) =>
            {
                BeginFolderRename(fe, folder);
            };

            var deleteItem = new MenuItem { Header = "删除" };
            deleteItem.Click += (_, _) =>
            {
                _viewModel.DeleteFolderCommand.Execute(folder.Id);
            };

            menu.Items.Add(renameItem);
            menu.Items.Add(deleteItem);
            menu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void AddFolder_Click(object sender, MouseButtonEventArgs e)
    {
        _viewModel.CreateFolderCommand.Execute(null);
    }

    private void AddToFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        if (menuItem.Tag is not long folderId) return;

        // Walk up: sub-MenuItem → parent "添加到收藏夹" MenuItem → ContextMenu
        ContextMenu? cm = null;
        var parentItem = ItemsControl.ItemsControlFromItemContainer(menuItem);
        if (parentItem is MenuItem parentMenuItem)
            cm = parentMenuItem.Parent as ContextMenu;

        // Fallback: walk logical/visual tree
        if (cm == null)
        {
            DependencyObject? current = menuItem;
            while (current != null)
            {
                if (current is ContextMenu found) { cm = found; break; }
                current = LogicalTreeHelper.GetParent(current)
                          ?? VisualTreeHelper.GetParent(current);
            }
        }

        // Get ClipboardEntry from ContextMenu
        ClipboardEntry? entry = null;
        if (cm?.PlacementTarget is FrameworkElement fe)
            entry = fe.DataContext as ClipboardEntry;
        entry ??= cm?.DataContext as ClipboardEntry;

        if (entry != null)
            _viewModel.AddEntryToFolderCommand.Execute((entry.Id, folderId));
    }

    private void RemoveFromFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;

        ContextMenu? cm = null;
        DependencyObject? current = menuItem;
        while (current != null)
        {
            if (current is ContextMenu found) { cm = found; break; }
            current = LogicalTreeHelper.GetParent(current)
                      ?? VisualTreeHelper.GetParent(current);
        }

        ClipboardEntry? entry = null;
        if (cm?.PlacementTarget is FrameworkElement fe)
            entry = fe.DataContext as ClipboardEntry;
        entry ??= cm?.DataContext as ClipboardEntry;

        if (entry != null)
            _viewModel.RemoveEntryFromFolderCommand.Execute(entry);
    }

    private async void DeleteEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem)
        {
            return;
        }

        var entry = TryGetEntryFromContextMenu(menuItem);
        if (entry == null)
        {
            return;
        }

        if (entry.FavoriteFolderId is > 0)
        {
            if (!await ConfirmDeleteFavoriteEntryAsync())
            {
                return;
            }
        }

        await _viewModel.DeleteEntryCommand.ExecuteAsync(entry);
    }

    private static ClipboardEntry? TryGetEntryFromContextMenu(MenuItem menuItem)
    {
        ContextMenu? cm = null;
        DependencyObject? current = menuItem;
        while (current != null)
        {
            if (current is ContextMenu found)
            {
                cm = found;
                break;
            }

            current = LogicalTreeHelper.GetParent(current)
                      ?? VisualTreeHelper.GetParent(current);
        }

        if (cm?.PlacementTarget is FrameworkElement fe)
        {
            return fe.DataContext as ClipboardEntry;
        }

        return cm?.DataContext as ClipboardEntry;
    }

    private async Task<bool> ConfirmDeleteFavoriteEntryAsync()
    {
        var owner = Window.GetWindow(this);
        var confirmBox = new Wpf.Ui.Controls.MessageBox
        {
            Title = "删除确认",
            Content = "该记录在收藏夹中，确认删除吗？",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.Manual
        };

        if (owner != null)
        {
            confirmBox.Loaded += (_, _) => CenterDialogOnOwnerScreen(confirmBox, owner);
        }

        var result = await confirmBox.ShowDialogAsync();
        return result == Wpf.Ui.Controls.MessageBoxResult.Primary;
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

        // Center on the current monitor's working area (slightly above center),
        // instead of centering on owner window bounds.
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

    // --- Alias editing handlers ---

    private void CardHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ClipboardEntry entry)
        {
            EnsureSelectionForEntry(entry);
            EnterAliasEditMode(fe, entry);
            e.Handled = true;
        }
    }

    private void EntryCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ClipboardEntry entry)
        {
            EnsureSelectionForEntry(entry);
            CardList.Focus();
        }
    }

    private void AliasEditBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb && tb.Tag is ClipboardEntry entry)
        {
            CommitAliasEdit(tb, entry);
            e.Handled = true;
            CardList.Focus();
        }
        else if (e.Key == Key.Escape && sender is TextBox tb2)
        {
            ExitAliasEditMode(tb2, tb2.Tag as ClipboardEntry);
            e.Handled = true;
            CardList.Focus();
        }
    }

    private void AliasEditBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.Tag is ClipboardEntry entry)
        {
            CommitAliasEdit(tb, entry);
        }
    }

    private void EnsureSelectionForEntry(ClipboardEntry entry)
    {
        _viewModel.SelectedEntry = entry;

        var index = _viewModel.Entries.IndexOf(entry);
        if (index < 0)
        {
            return;
        }

        _currentSelectionIndex = index;
        CardList.SelectedIndex = index;
    }

    private void CommitAliasEdit(TextBox textBox, ClipboardEntry entry)
    {
        ExitAliasEditMode(textBox, entry);
        var newAlias = textBox.Text.Trim();
        if (newAlias != entry.Alias)
        {
            _viewModel.UpdateAliasCommand.Execute((entry.Id, string.IsNullOrWhiteSpace(newAlias) ? null : newAlias));
        }
    }

    private void CommitActiveAliasEdits()
    {
        var editBoxes = new List<TextBox>();
        CollectVisualChildrenByName(CardList, "AliasEditBox", editBoxes);

        foreach (var box in editBoxes)
        {
            if (box.Visibility == Visibility.Visible && box.Tag is ClipboardEntry entry)
            {
                CommitAliasEdit(box, entry);
            }
        }
    }

    // --- Helpers ---

    private static ScrollViewer? GetScrollViewer(DependencyObject depObj)
    {
        if (depObj is ScrollViewer sv) return sv;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = VisualTreeHelper.GetChild(depObj, i);
            var result = GetScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var found = FindVisualChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    private static double Clamp(double value, double min, double max)
        => Math.Max(min, Math.Min(value, max));

    private static void EnterAliasEditMode(DependencyObject container, ClipboardEntry entry)
    {
        var textBox = FindVisualChildByName<TextBox>(container, "AliasEditBox");
        var aliasTitle = FindVisualChildByName<TextBlock>(container, "AliasTitleTextBlock");
        var defaultTitle = FindVisualChildByName<TextBlock>(container, "DefaultTitleTextBlock");
        if (textBox == null) return;

        if (aliasTitle != null) aliasTitle.Visibility = Visibility.Collapsed;
        if (defaultTitle != null) defaultTitle.Visibility = Visibility.Collapsed;

        var displayedTitle = aliasTitle?.Text;
        if (string.IsNullOrWhiteSpace(displayedTitle))
            displayedTitle = defaultTitle?.Text;
        textBox.Text = displayedTitle ?? string.Empty;
        textBox.Visibility = Visibility.Visible;
        textBox.Tag = entry;

        textBox.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            textBox.Focus();
            textBox.SelectAll();
        });
    }

    private static void ExitAliasEditMode(TextBox textBox, ClipboardEntry? entry)
    {
        textBox.Visibility = Visibility.Collapsed;
        var container = VisualTreeHelper.GetParent(textBox);
        var aliasTitle = container != null ? FindVisualChildByName<TextBlock>(container, "AliasTitleTextBlock") : null;
        var defaultTitle = container != null ? FindVisualChildByName<TextBlock>(container, "DefaultTitleTextBlock") : null;

        var hasAlias = !string.IsNullOrWhiteSpace(entry?.Alias);
        if (aliasTitle != null) aliasTitle.Visibility = hasAlias ? Visibility.Visible : Visibility.Collapsed;
        if (defaultTitle != null) defaultTitle.Visibility = hasAlias ? Visibility.Collapsed : Visibility.Visible;
    }

    private static T? FindVisualChildByName<T>(DependencyObject parent, string name)
        where T : FrameworkElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t && t.Name == name) return t;
            var found = FindVisualChildByName<T>(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private static void CollectVisualChildrenByName<T>(DependencyObject parent, string name, IList<T> result)
        where T : FrameworkElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t && t.Name == name)
            {
                result.Add(t);
            }

            CollectVisualChildrenByName(child, name, result);
        }
    }

}
