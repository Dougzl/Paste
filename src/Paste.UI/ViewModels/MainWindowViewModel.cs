using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using Wpf.Ui.Controls;

namespace Paste.UI.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _applicationTitle = "Paste";

    [ObservableProperty]
    private ObservableCollection<object> _menuItems = new()
    {
        new NavigationViewItem
        {
            Content = "History",
            Icon = new SymbolIcon { Symbol = SymbolRegular.ClipboardPaste24 },
            TargetPageType = null // Will be set in code-behind
        }
    };

    [ObservableProperty]
    private ObservableCollection<MenuItem> _trayMenuItems = new()
    {
        new MenuItem { Header = "Show", Tag = "show" },
        new MenuItem { Header = "Exit", Tag = "exit" }
    };
}
