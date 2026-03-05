using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace Paste.UI.Converters;

public class FilePathsDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string content || string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var lines = content
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0)
            .Select(static line =>
            {
                try
                {
                    var name = Path.GetFileName(line);
                    return string.IsNullOrWhiteSpace(name) ? line : name;
                }
                catch
                {
                    return line;
                }
            });

        return string.Join(Environment.NewLine, lines);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
