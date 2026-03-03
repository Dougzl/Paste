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
    private static readonly Color CardBodyColor = Color.FromRgb(0x2A, 0x2A, 0x2E);

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

            // Composite PNG over card body color so transparent areas always match content background.
            var composited = CompositeOnBackground(bitmap, CardBodyColor);
            Cache[relativePath] = composited;
            return composited;
        }
        catch
        {
            return DependencyProperty.UnsetValue;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static BitmapSource CompositeOnBackground(BitmapSource source, Color background)
    {
        var formatted = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        var width = formatted.PixelWidth;
        var height = formatted.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        formatted.CopyPixels(pixels, stride, 0);

        for (var i = 0; i < pixels.Length; i += 4)
        {
            var b = pixels[i];
            var g = pixels[i + 1];
            var r = pixels[i + 2];
            var a = pixels[i + 3];

            if (a == 255)
                continue;

            // Alpha blend source pixel over background color.
            var invA = 255 - a;
            pixels[i] = (byte)((b * a + background.B * invA) / 255);
            pixels[i + 1] = (byte)((g * a + background.G * invA) / 255);
            pixels[i + 2] = (byte)((r * a + background.R * invA) / 255);
            pixels[i + 3] = 255;
        }

        var result = BitmapSource.Create(width, height, formatted.DpiX, formatted.DpiY,
            PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }
}
