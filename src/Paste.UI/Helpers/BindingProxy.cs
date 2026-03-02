using System.Windows;

namespace Paste.UI.Helpers;

/// <summary>
/// Freezable proxy to allow binding from ContextMenu (which is outside the visual tree)
/// back to the DataContext of the page.
/// </summary>
public class BindingProxy : Freezable
{
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy), new UIPropertyMetadata(null));

    public object Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new BindingProxy();
}
