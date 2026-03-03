using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Paste.UI.Converters;

public class FavoriteFolderIdToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var inFavorite = value switch
        {
            long id => id > 0,
            int id => id > 0,
            short id => id > 0,
            ulong id => id > 0,
            uint id => id > 0,
            ushort id => id > 0,
            string s when long.TryParse(s, out var parsed) => parsed > 0,
            _ => false
        };

        var invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
        var visible = invert ? !inFavorite : inFavorite;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
