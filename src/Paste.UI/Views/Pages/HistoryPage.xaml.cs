using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Paste.Core.Models;
using Paste.UI.ViewModels;

namespace Paste.UI.Views.Pages;

public partial class HistoryPage : UserControl
{
    private const int WM_MOUSEHWHEEL = 0x020E;
    private readonly ClipboardHistoryViewModel _viewModel;
    private HwndSource? _hwndSource;

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
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEHWHEEL)
        {
            var delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
            var scrollViewer = GetScrollViewer(CardList);
            if (scrollViewer != null)
            {
                scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + delta / 30.0);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

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
    }

    private void CardList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
    }

    private void AppFilterChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is SourceAppFilter filter)
        {
            _viewModel.ToggleAppFilterCommand.Execute(filter);
        }
    }

    private void CardImage_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Image img)
        {
            var dc = img.DataContext;
            var src = img.Source;
            var entry = dc as ClipboardEntry;
            Console.WriteLine($"[CardImage_Loaded] DataContext={dc?.GetType().Name ?? "null"}, " +
                              $"Content={entry?.Content?[..Math.Min(40, entry.Content?.Length ?? 0)]}, " +
                              $"Source={src?.GetType().Name ?? "NULL"}, " +
                              $"ActualSize={img.ActualWidth}x{img.ActualHeight}, " +
                              $"ParentVisible={((FrameworkElement)img.Parent).Visibility}");
        }
    }

    private static ScrollViewer? GetScrollViewer(DependencyObject depObj)
    {
        if (depObj is ScrollViewer sv) return sv;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = VisualTreeHelper.GetChild(depObj, i);
            var result = GetScrollViewer(child);/
            if (result != null) return result;
        }
        return null;
    }
}
