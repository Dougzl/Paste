using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Paste.Core.Interfaces;
using Paste.Core.Models;
using Clipboard = System.Windows.Clipboard;

namespace Paste.App.Services;

public class ClipboardMonitor : IClipboardMonitor
{
    private static readonly string ImageDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Paste", "images");
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Paste", "logs");
    private static readonly string CaptureLogPath = Path.Combine(LogDir, "capture-diagnostics.log");
    private static readonly string CaptureDebugDir = Path.Combine(LogDir, "capture-debug");

    private HwndSource? _hwndSource;
    private readonly ISourceAppService _sourceAppService;
    private readonly IImageStorageService _imageStorageService;
    private bool _isMonitoring;
    private bool _isSuppressed;

    public event EventHandler<ClipboardEntry>? ClipboardChanged;

    public void Suppress() => _isSuppressed = true;
    public void Resume() => _isSuppressed = false;

    public ClipboardMonitor(ISourceAppService sourceAppService, IImageStorageService imageStorageService)
    {
        _sourceAppService = sourceAppService;
        _imageStorageService = imageStorageService;
    }

    public void Start()
    {
        if (_isMonitoring) return;

        var parameters = new HwndSourceParameters("PasteClipboardMonitor")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3) // HWND_MESSAGE
        };

        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);

        NativeMethods.AddClipboardFormatListener(_hwndSource.Handle);
        _isMonitoring = true;
    }

    public void Stop()
    {
        if (!_isMonitoring || _hwndSource == null) return;

        NativeMethods.RemoveClipboardFormatListener(_hwndSource.Handle);
        _hwndSource.RemoveHook(WndProc);
        _hwndSource.Dispose();
        _hwndSource = null;
        _isMonitoring = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
        {
            handled = true;
            OnClipboardChanged();
        }
        return IntPtr.Zero;
    }

    private void OnClipboardChanged()
    {
        if (_isSuppressed) return;

        try
        {
            var (appName, appPath) = _sourceAppService.GetForegroundAppInfo();
            ClipboardEntry? entry = null;

            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList();
                var content = string.Join(Environment.NewLine, files.Cast<string>());
                var fileCount = files.Count;
                var firstFile = fileCount > 0 ? Path.GetFileName(files[0]!) : "";
                var preview = fileCount > 1 ? $"{firstFile} +{fileCount - 1}" : firstFile;
                entry = new ClipboardEntry
                {
                    Content = content,
                    ContentType = ClipboardContentType.FilePaths,
                    Preview = preview,
                    ContentHash = ComputeHash(content),
                    SourceAppName = appName,
                    SourceAppPath = appPath,
                    CopiedAt = DateTime.UtcNow
                };
            }
            else if (Clipboard.ContainsImage())
            {
                var image = Clipboard.GetImage();
                if (image != null)
                {
                    LogImageStats("source", image, null, null);
                    var imageForStorage = NormalizeTransparentImageIfNeeded(image);
                    LogImageStats("normalized", imageForStorage, null, null);

                    var imageData = BitmapSourceToBytes(imageForStorage);
                    var hash = ComputeHash(imageData);
                    var encoded = TryDecodeBitmap(imageData);
                    LogImageStats("encoded", encoded, hash, null);
                    WriteDebugImage("source", image, hash, null);
                    WriteDebugImage("normalized", imageForStorage, hash, null);
                    WriteDebugImage("encoded", encoded, hash, null);
                    // Use Task.Run to avoid deadlock — SaveImageAsync posts back
                    // to SynchronizationContext which is blocked by .GetResult()
                    var relativePath = Task.Run(() => _imageStorageService.SaveImageAsync(imageData, hash)).GetAwaiter().GetResult();
                    var saved = TryLoadSavedBitmap(relativePath);
                    LogImageStats("saved", saved, hash, relativePath);
                    WriteDebugImage("saved", saved, hash, relativePath);

                    entry = new ClipboardEntry
                    {
                        Content = relativePath,
                        ContentType = ClipboardContentType.Image,
                        Preview = $"图片 {(int)image.Width}x{(int)image.Height}",
                        ContentHash = hash,
                        SourceAppName = appName,
                        SourceAppPath = appPath,
                        CopiedAt = DateTime.UtcNow
                    };
                }
            }
            else if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                if (!string.IsNullOrEmpty(text))
                {
                    entry = new ClipboardEntry
                    {
                        Content = text,
                        ContentType = ClipboardContentType.Text,
                        Preview = text.Length > 200 ? text[..200] + "..." : text,
                        ContentHash = ComputeHash(text),
                        SourceAppName = appName,
                        SourceAppPath = appPath,
                        CopiedAt = DateTime.UtcNow
                    };
                }
            }

            if (entry != null)
            {
                ClipboardChanged?.Invoke(this, entry);
            }
        }
        catch
        {
            // Clipboard access can fail if another app has it locked
        }
    }

    private static string ComputeHash(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return ComputeHash(bytes);
    }

    private static string ComputeHash(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static byte[] BitmapSourceToBytes(BitmapSource source)
    {
        using var stream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static BitmapSource NormalizeTransparentImageIfNeeded(BitmapSource source)
    {
        var formatted = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        var width = formatted.PixelWidth;
        var height = formatted.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        formatted.CopyPixels(pixels, stride, 0);

        long samples = 0;
        long alphaZero = 0;
        long visibleColor = 0;

        for (var i = 0; i < pixels.Length; i += 4)
        {
            samples++;
            var b = pixels[i];
            var g = pixels[i + 1];
            var r = pixels[i + 2];
            var a = pixels[i + 3];

            if (a == 0)
            {
                alphaZero++;
            }

            if ((r + g + b) > 12)
            {
                visibleColor++;
            }
        }

        if (samples == 0)
        {
            return source;
        }

        var alphaZeroRatio = alphaZero / (double)samples;
        var hasColorPayload = visibleColor > samples / 500; // >0.2%
        if (alphaZeroRatio < 0.95 || !hasColorPayload)
        {
            return source;
        }

        for (var i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
        }

        var normalized = BitmapSource.Create(
            width,
            height,
            formatted.DpiX,
            formatted.DpiY,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        normalized.Freeze();
        return normalized;
    }

    private static BitmapSource? TryDecodeBitmap(byte[] data)
    {
        try
        {
            using var ms = new MemoryStream(data);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? TryLoadSavedBitmap(string relativePath)
    {
        try
        {
            var path = Path.Combine(ImageDir, relativePath);
            if (!File.Exists(path))
            {
                return null;
            }

            var data = File.ReadAllBytes(path);
            return TryDecodeBitmap(data);
        }
        catch
        {
            return null;
        }
    }

    private static void LogImageStats(string stage, BitmapSource? source, string? hash, string? relativePath)
    {
        try
        {
            Directory.CreateDirectory(LogDir);

            if (source == null)
            {
                AppendCaptureLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} stage={stage} hash={hash ?? "-"} file={relativePath ?? "-"} decode=null");
                return;
            }

            var formatted = source.Format == PixelFormats.Bgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            if (formatted is Freezable freezable && freezable.CanFreeze)
            {
                freezable.Freeze();
            }

            var width = formatted.PixelWidth;
            var height = formatted.PixelHeight;
            var stride = width * 4;
            var pixels = new byte[stride * height];
            formatted.CopyPixels(pixels, stride, 0);

            var stepX = Math.Max(1, width / 200);
            var stepY = Math.Max(1, height / 200);
            long samples = 0;
            long black = 0;
            long alphaZero = 0;
            long rTotal = 0;
            long gTotal = 0;
            long bTotal = 0;

            for (var y = 0; y < height; y += stepY)
            {
                var rowBase = y * stride;
                for (var x = 0; x < width; x += stepX)
                {
                    var i = rowBase + x * 4;
                    var b = pixels[i];
                    var g = pixels[i + 1];
                    var r = pixels[i + 2];
                    var a = pixels[i + 3];
                    samples++;

                    if (a == 0)
                    {
                        alphaZero++;
                    }

                    if (a > 8 && r < 8 && g < 8 && b < 8)
                    {
                        black++;
                    }

                    rTotal += r;
                    gTotal += g;
                    bTotal += b;
                }
            }

            var avgR = samples > 0 ? rTotal / (double)samples : 0;
            var avgG = samples > 0 ? gTotal / (double)samples : 0;
            var avgB = samples > 0 ? bTotal / (double)samples : 0;
            var blackPct = samples > 0 ? black * 100.0 / samples : 0;
            var alphaZeroPct = samples > 0 ? alphaZero * 100.0 / samples : 0;

            AppendCaptureLog(
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} stage={stage} hash={hash ?? "-"} file={relativePath ?? "-"} " +
                $"size={width}x{height} format={source.Format} samples={samples} black%={blackPct:F2} alpha0%={alphaZeroPct:F2} " +
                $"avgRGB=({avgR:F1},{avgG:F1},{avgB:F1})");
        }
        catch
        {
            // Ignore diagnostics failure.
        }
    }

    private static void AppendCaptureLog(string line)
    {
        File.AppendAllText(CaptureLogPath, line + Environment.NewLine);
    }

    private static void WriteDebugImage(string stage, BitmapSource? source, string hash, string? relativePath)
    {
        try
        {
            if (source == null)
            {
                return;
            }

            Directory.CreateDirectory(CaptureDebugDir);
            var safeFile = string.IsNullOrWhiteSpace(relativePath)
                ? "-"
                : relativePath.Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_');
            var fileName = $"{DateTime.Now:yyyyMMdd-HHmmss-fff}_{stage}_{hash}_{safeFile}.png";
            var path = Path.Combine(CaptureDebugDir, fileName);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }
        catch
        {
            // Ignore diagnostics failure.
        }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
