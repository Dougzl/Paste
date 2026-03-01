using System.Collections.Concurrent;
using System.Drawing;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Paste.UI.Converters;

public class AppIconConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, ImageSource?> IconCache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string appPath || string.IsNullOrEmpty(appPath))
            return null;

        return IconCache.GetOrAdd(appPath, path =>
        {
            try
            {
                if (!System.IO.File.Exists(path))
                    return null;

                using var icon = Icon.ExtractAssociatedIcon(path);
                if (icon == null) return null;

                var bitmap = icon.ToBitmap();
                var hBitmap = bitmap.GetHbitmap();
                try
                {
                    var source = Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap, IntPtr.Zero, Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    return source;
                }
                finally
                {
                    DeleteObject(hBitmap);
                    bitmap.Dispose();
                }
            }
            catch
            {
                return null;
            }
        });
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
