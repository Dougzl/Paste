using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;

namespace Paste.UI.Converters;

public partial class UrlDetectorConverter : IValueConverter
{
    [GeneratedRegex(@"^https?://\S+$", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isUrl = value is string text && !string.IsNullOrWhiteSpace(text) && UrlRegex().IsMatch(text.Trim());

        var param = parameter as string ?? "";

        return param switch
        {
            "Visibility" => isUrl ? Visibility.Visible : Visibility.Collapsed,
            "InvertVisibility" => isUrl ? Visibility.Collapsed : Visibility.Visible,
            _ => isUrl
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var invert = parameter is string s && s == "Invert";
        var visible = value is true;
        if (invert) visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
