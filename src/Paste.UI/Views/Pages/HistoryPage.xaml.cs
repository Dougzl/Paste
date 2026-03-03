using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Paste.Core.Models;
using Paste.UI.ViewModels;

namespace Paste.UI.Views.Pages;

public partial class HistoryPage : UserControl
{
    private const int WM_MOUSEHWHEEL = 0x020E;
    private readonly ClipboardHistoryViewModel _viewModel;
    private HwndSource? _hwndSource;

    // Drag-to-scroll state
    private bool _isMouseDown;
    private bool _isDragging;
    private Point _dragStartPoint;
    private double _dragStartOffset;
    private readonly List<(DateTime Time, double Offset)> _dragSamples = new();
    private const double DragThreshold = 5.0;
    private const double MaxVelocity = 60.0;

    // Inertia / smooth scroll animation
    private double _velocity;
    private double _smoothTarget;
    private bool _hasSmoothTarget;
    private DispatcherTimer? _animationTimer;
    private const double InertiaFriction = 0.95;
    private const double MinVelocity = 0.5;
    private const double SmoothLerpFactor = 0.15;

    public HistoryPage(ClipboardHistoryViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;
        InitializeComponent();
        Loaded += HistoryPage_Loaded;
        Unloaded += HistoryPage_Unloaded;
    }

    private void HistoryPage_Loaded(object sender, RoutedEventArgs e)
    {
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
        StopAnimation();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEHWHEEL)
        {
            var delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
            var scrollViewer = GetScrollViewer(CardList);
            if (scrollViewer != null)
            {
                if (!_hasSmoothTarget)
                    _smoothTarget = scrollViewer.HorizontalOffset;

                _smoothTarget += delta * 0.8;
                _smoothTarget = Clamp(_smoothTarget, 0, scrollViewer.ScrollableWidth);
                _velocity = 0;
                _hasSmoothTarget = true;
                StartAnimation();
                handled = true;
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
        _dragStartPoint = e.GetPosition(CardList);
        var sv = GetScrollViewer(CardList);
        _dragStartOffset = sv?.HorizontalOffset ?? 0;
        _dragSamples.Clear();
        _dragSamples.Add((DateTime.Now, _dragStartOffset));
    }

    private void CardList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isMouseDown) return;

        var currentPos = e.GetPosition(CardList);
        var deltaX = currentPos.X - _dragStartPoint.X;

        if (!_isDragging)
        {
            if (Math.Abs(deltaX) > DragThreshold)
            {
                _isDragging = true;
                CardList.CaptureMouse();
                Cursor = Cursors.Hand;
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
        Cursor = Cursors.Arrow;

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
        var scrollViewer = GetScrollViewer(CardList);
        if (scrollViewer != null)
        {
            if (!_hasSmoothTarget)
                _smoothTarget = scrollViewer.HorizontalOffset;

            _smoothTarget -= e.Delta * 0.8;
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

    private void CardList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _viewModel.PasteSelectedCommand.Execute(null);
        }
        else if (e.Key == Key.Delete)
        {
            if (_viewModel.SelectedEntry != null)
            {
                _viewModel.DeleteEntryCommand.ExecuteAsync(_viewModel.SelectedEntry);
            }
        }
    }

    // --- Top bar handlers ---

    private void SettingsButton_Click(object sender, MouseButtonEventArgs e)
    {
        _viewModel.OpenSettingsCommand.Execute(null);
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
        if (sender is ScrollViewer sv)
        {
            sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta * 0.5);
            e.Handled = true;
        }
    }

    // --- Favorites handlers ---

    private void FolderChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is FavoriteFolderViewModel folder)
        {
            // Only handle single clicks (double-click is handled in PreviewMouseDown)
            if (!folder.IsEditing)
                _viewModel.SelectFolderCommand.Execute(folder);
        }
    }

    private void FolderChip_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2 && sender is FrameworkElement fe && fe.Tag is FavoriteFolderViewModel folder)
        {
            // Double-click: enter inline edit mode
            folder.IsEditing = true;
            e.Handled = true;

            // Focus the TextBox after it becomes visible
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                var textBox = FindVisualChild<TextBox>(fe);
                if (textBox != null)
                {
                    textBox.Focus();
                    textBox.SelectAll();
                }
            });
        }
    }

    private void FolderEditBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb && tb.DataContext is FavoriteFolderViewModel folder)
        {
            CommitFolderRename(folder);
            e.Handled = true;
            // Return focus to card list
            CardList.Focus();
        }
        else if (e.Key == Key.Escape && sender is TextBox && ((TextBox)sender).DataContext is FavoriteFolderViewModel f)
        {
            f.IsEditing = false;
            e.Handled = true;
            CardList.Focus();
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

    private void FolderChip_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is FavoriteFolderViewModel folder)
        {
            var menu = new ContextMenu();

            var renameItem = new MenuItem { Header = "重命名" };
            renameItem.Click += (_, _) =>
            {
                folder.IsEditing = true;
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
                {
                    var textBox = FindVisualChild<TextBox>(fe);
                    if (textBox != null)
                    {
                        textBox.Focus();
                        textBox.SelectAll();
                    }
                });
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

    // --- Alias editing handlers ---

    private void CardHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2 && sender is FrameworkElement fe && fe.Tag is ClipboardEntry entry)
        {
            // Double-click: enter inline alias edit mode
            var textBox = FindVisualChild<TextBox>(fe);
            if (textBox != null)
            {
                textBox.Text = entry.Alias ?? string.Empty;
                textBox.Visibility = Visibility.Visible;
                textBox.Tag = entry;

                Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
                {
                    textBox.Focus();
                    textBox.SelectAll();
                });
            }
            e.Handled = true;
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
            tb2.Visibility = Visibility.Collapsed;
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

    private void CommitAliasEdit(TextBox textBox, ClipboardEntry entry)
    {
        textBox.Visibility = Visibility.Collapsed;
        var newAlias = textBox.Text.Trim();
        if (newAlias != entry.Alias)
        {
            _viewModel.UpdateAliasCommand.Execute((entry.Id, string.IsNullOrWhiteSpace(newAlias) ? null : newAlias));
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
}
