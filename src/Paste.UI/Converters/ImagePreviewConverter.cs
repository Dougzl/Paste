using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
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

        if (Cache.TryGetValue(relativePath, out var cached))
            return cached;

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

            // Some clipboard sources (Snipaste, chat apps) save images with alpha=0,
            // producing fully transparent PNGs. Force all pixels opaque for preview.
            BitmapSource result = EnsureOpaque(bitmap);

            Cache.TryAdd(relativePath, result);
            return result;
        }
        catch
        {
            return DependencyProperty.UnsetValue;
        }
    }

    /// <summary>
    /// If the bitmap has an alpha channel, set all alpha values to 255 (fully opaque).
    /// </summary>
    private static BitmapSource EnsureOpaque(BitmapSource source)
    {
        if (source.Format != PixelFormats.Bgra32 && source.Format != PixelFormats.Pbgra32)
            return source;

        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        source.CopyPixels(pixels, stride, 0);

        for (var i = 3; i < pixels.Length; i += 4)
            pixels[i] = 255;

        var opaque = BitmapSource.Create(width, height, source.DpiX, source.DpiY,
            PixelFormats.Bgra32, null, pixels, stride);
        opaque.Freeze();
        return opaque;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
