using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Media;
using Paste.Core.Models;

namespace Paste.UI.Converters;

/// <summary>
/// MultiValueConverter: takes ContentType (enum) + Content (string) and returns a SolidColorBrush.
/// Text URLs → blue, plain Text → coral/red, Image → green, FilePaths → purple.
/// </summary>
public partial class ContentTypeToColorConverter : IMultiValueConverter
{
    private static readonly SolidColorBrush TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0x6B, 0x6B)));   // coral/red
    private static readonly SolidColorBrush LinkBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x5B, 0x9B, 0xD5)));   // blue
    private static readonly SolidColorBrush ImageBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x6B, 0xB8, 0x7A)));  // green
    private static readonly SolidColorBrush FilesBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x9B, 0x7E, 0xC8)));  // purple
    private static readonly SolidColorBrush FallbackBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)));

    [GeneratedRegex(@"^https?://\S+$", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not ClipboardContentType contentType)
            return FallbackBrush;

        var content = values[1] as string ?? "";

        return contentType switch
        {
            ClipboardContentType.Image => ImageBrush,
            ClipboardContentType.FilePaths => FilesBrush,
            ClipboardContentType.Text when IsUrl(content) => LinkBrush,
            ClipboardContentType.Text => TextBrush,
            _ => FallbackBrush
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static bool IsUrl(string text)
        => !string.IsNullOrWhiteSpace(text) && UrlRegex().IsMatch(text.Trim());

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
