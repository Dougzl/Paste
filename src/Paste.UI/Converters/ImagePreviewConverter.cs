using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Paste.UI.Converters;

public class ImagePreviewConverter : IValueConverter
{
    private static readonly string ImageDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Paste", "images");

    private static readonly ConcurrentDictionary<string, BitmapSource> Cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string relativePath || string.IsNullOrEmpty(relativePath))
            return DependencyProperty.UnsetValue;

        if (!relativePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return DependencyProperty.UnsetValue;

        try
        {
            var fullPath = Path.Combine(ImageDir, relativePath);
            if (!File.Exists(fullPath))
                return DependencyProperty.UnsetValue;

            var data = File.ReadAllBytes(fullPath);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(data);
            bitmap.DecodePixelWidth = 300;
            bitmap.EndInit();
            bitmap.Freeze();

            Cache[relativePath] = bitmap;
            return bitmap;
        }
        catch
        {
            return DependencyProperty.UnsetValue;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

}
