using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Data;
using Paste.Core.Models;

namespace Paste.UI.Converters;

/// <summary>
/// MultiValueConverter: takes ContentType (enum) + Content (string) and returns a display label.
/// Text URLs → "Link", plain Text → "Text", Image → "Image", FilePaths → "Files".
/// </summary>
public partial class ContentTypeToLabelConverter : IMultiValueConverter
{
    [GeneratedRegex(@"^https?://\S+$", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not ClipboardContentType contentType)
            return "Unknown";

        var content = values[1] as string ?? "";

        return contentType switch
        {
            ClipboardContentType.Image => "图片",
            ClipboardContentType.FilePaths => "文件",
            ClipboardContentType.Text when IsUrl(content) => "链接",
            ClipboardContentType.Text => "文本",
            _ => "未知"
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static bool IsUrl(string text)
        => !string.IsNullOrWhiteSpace(text) && UrlRegex().IsMatch(text.Trim());
}
