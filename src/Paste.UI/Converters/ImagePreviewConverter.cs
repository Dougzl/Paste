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
        {
            Console.WriteLine($"[ImagePreview] SKIP: value is null/empty (type={value?.GetType().Name ?? "null"})");
            return DependencyProperty.UnsetValue;
        }

        if (!relativePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[ImagePreview] SKIP: not .png → {relativePath[..Math.Min(40, relativePath.Length)]}...");
            return DependencyProperty.UnsetValue;
        }

        if (Cache.TryGetValue(relativePath, out var cached))
        {
            Console.WriteLine($"[ImagePreview] CACHE HIT: {relativePath} → {cached.PixelWidth}x{cached.PixelHeight}");
            return cached;
        }

        try
        {
            var fullPath = Path.Combine(ImageDir, relativePath);
            if (!File.Exists(fullPath))
            {
                Console.WriteLine($"[ImagePreview] FILE NOT FOUND: {fullPath}");
                return DependencyProperty.UnsetValue;
            }

            // Use MemoryStream for guaranteed synchronous loading
            var data = File.ReadAllBytes(fullPath);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(data);
            bitmap.DecodePixelWidth = 300;
            bitmap.EndInit();
            bitmap.Freeze();

            Console.WriteLine($"[ImagePreview] LOADED: {relativePath} → {bitmap.PixelWidth}x{bitmap.PixelHeight}");

            Cache.TryAdd(relativePath, bitmap);
            return bitmap;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ImagePreview] ERROR: {relativePath} → {ex.Message}");
            return DependencyProperty.UnsetValue;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
