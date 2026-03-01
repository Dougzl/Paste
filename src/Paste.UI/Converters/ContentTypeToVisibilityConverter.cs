using System.Globalization;
using System.Windows.Data;
using Paste.Core.Models;

namespace Paste.UI.Converters;

public class ContentTypeToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ClipboardContentType contentType && parameter is string target)
        {
            return contentType.ToString() == target
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
        }
        return System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
