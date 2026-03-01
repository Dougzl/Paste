using System.Globalization;
using System.Windows.Data;
using Paste.Core.Models;
using Wpf.Ui.Controls;

namespace Paste.UI.Converters;

public class ContentTypeToIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ClipboardContentType contentType)
        {
            return contentType switch
            {
                ClipboardContentType.Text => SymbolRegular.Document24,
                ClipboardContentType.Image => SymbolRegular.Image24,
                ClipboardContentType.FilePaths => SymbolRegular.Folder24,
                _ => SymbolRegular.Document24
            };
        }
        return SymbolRegular.Document24;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
