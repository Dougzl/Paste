using System.IO;
using System.Collections.Concurrent;
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
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".ico"
    };

    private static readonly string ImageDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Paste", "images");
    private static readonly string SourceFilesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Paste", "files");
    private const int DefaultSourceFileCopyMaxSizeMb = 100;
    private const int MinSourceFileCopyMaxSizeMb = 1;
    private const int MaxSourceFileCopyMaxSizeMb = 2048;
    private const int MaxStoredTextLength = 1_000_000;
    private const int TextHashSampleLength = 16_384;

    private HwndSource? _hwndSource;
    private readonly ISourceAppService _sourceAppService;
    private readonly IImageStorageService _imageStorageService;
    private readonly ISettingsService _settingsService;
    private readonly ConcurrentDictionary<string, string> _managedFileCopyCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _isMonitoring;
    private bool _isSuppressed;
    private AppSettings _settings;

    public event EventHandler<ClipboardEntry>? ClipboardChanged;

    public void Suppress() => _isSuppressed = true;
    public void Resume() => _isSuppressed = false;

    public ClipboardMonitor(
        ISourceAppService sourceAppService,
        IImageStorageService imageStorageService,
        ISettingsService settingsService)
    {
        _sourceAppService = sourceAppService;
        _imageStorageService = imageStorageService;
        _settingsService = settingsService;
        _settings = _settingsService.Load();
        _settingsService.SettingsChanged += OnSettingsChanged;
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
                var originalPaths = NormalizeClipboardFileDropPaths(files.Cast<string>());
                if (originalPaths.Count == 0)
                {
                    return;
                }

                entry = TryCreateImageEntryFromFileDrop(originalPaths, appName, appPath);
                if (entry == null)
                {
                    var normalizedPaths = NormalizeFileDropPaths(originalPaths);
                    var content = string.Join(Environment.NewLine, normalizedPaths);
                    var fileCount = originalPaths.Count;
                    var firstFile = fileCount > 0 ? Path.GetFileName(originalPaths[0]) : "";
                    var preview = fileCount > 1 ? $"{firstFile} +{fileCount - 1}" : firstFile;
                    entry = new ClipboardEntry
                    {
                        Content = content,
                        ContentType = ClipboardContentType.FilePaths,
                        Preview = preview,
                        ContentHash = ComputeFileDropContentHash(originalPaths),
                        SourceAppName = appName,
                        SourceAppPath = appPath,
                        CopiedAt = DateTime.UtcNow
                    };
                }
            }
            else if (Clipboard.ContainsImage())
            {
                var image = Clipboard.GetImage();
                if (image != null)
                {
                    var imageForStorage = NormalizeTransparentImageIfNeeded(image);

                    var imageData = BitmapSourceToBytes(imageForStorage);
                    var hash = ComputeHash(imageData);
                    // Use Task.Run to avoid deadlock — SaveImageAsync posts back
                    // to SynchronizationContext which is blocked by .GetResult()
                    var relativePath = Task.Run(() => _imageStorageService.SaveImageAsync(imageData, hash)).GetAwaiter().GetResult();

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
                    if (text.Length > MaxStoredTextLength)
                    {
                        return;
                    }

                    var hashInput = text.Length > TextHashSampleLength
                        ? $"{text.Length}:{text[..TextHashSampleLength]}"
                        : text;

                    entry = new ClipboardEntry
                    {
                        Content = text,
                        ContentType = ClipboardContentType.Text,
                        Preview = text.Length > 200 ? text[..200] + "..." : text,
                        ContentHash = ComputeHash(hashInput),
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

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        _settings = settings ?? new AppSettings();
    }

    private static List<string> NormalizeClipboardFileDropPaths(IEnumerable<string> originalPaths)
    {
        var normalized = new List<string>();
        foreach (var originalPath in originalPaths)
        {
            if (string.IsNullOrWhiteSpace(originalPath))
            {
                continue;
            }

            normalized.Add(NormalizeFullPath(originalPath) ?? originalPath.Trim());
        }

        return normalized;
    }

    private List<string> NormalizeFileDropPaths(IEnumerable<string> originalPaths)
    {
        var settings = _settings;
        if (!settings.CopySourceFiles)
        {
            return originalPaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        }

        var maxBytes = NormalizeSourceFileCopyMaxSizeMb(settings.SourceFileCopyMaxSizeMb) * 1024L * 1024L;
        var normalized = new List<string>();
        foreach (var originalPath in originalPaths)
        {
            if (string.IsNullOrWhiteSpace(originalPath))
            {
                continue;
            }

            normalized.Add(TryCopySourceFile(originalPath, maxBytes));
        }

        return normalized;
    }

    private string TryCopySourceFile(string path, long maxBytes)
    {
        try
        {
            if (!File.Exists(path))
            {
                return path;
            }

            var normalizedPath = NormalizeFullPath(path);
            if (normalizedPath != null && IsUnderDirectory(normalizedPath, SourceFilesDir))
            {
                return normalizedPath;
            }

            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > maxBytes)
            {
                return path;
            }

            var cacheKey = BuildManagedFileCopyCacheKey(info);
            if (_managedFileCopyCache.TryGetValue(cacheKey, out var cachedPath) && File.Exists(cachedPath))
            {
                return cachedPath;
            }

            Directory.CreateDirectory(SourceFilesDir);
            var destinationPath = GetAvailableDestinationPath(SourceFilesDir, info.Name);
            File.Copy(path, destinationPath, overwrite: false);
            _managedFileCopyCache[cacheKey] = destinationPath;
            return destinationPath;
        }
        catch
        {
            return path;
        }
    }

    private static string BuildManagedFileCopyCacheKey(FileInfo info)
    {
        var fullPath = NormalizeFullPath(info.FullName) ?? info.FullName;
        return $"{fullPath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
    }

    private static string GetAvailableDestinationPath(string directory, string originalFileName)
    {
        var safeName = string.IsNullOrWhiteSpace(originalFileName) ? "copied-file" : originalFileName;
        var candidate = Path.Combine(directory, safeName);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var stem = Path.GetFileNameWithoutExtension(safeName);
        var ext = Path.GetExtension(safeName);
        for (var i = 1; i < 10_000; i++)
        {
            candidate = Path.Combine(directory, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{ext}");
    }

    private static int NormalizeSourceFileCopyMaxSizeMb(int value)
    {
        if (value <= 0)
        {
            value = DefaultSourceFileCopyMaxSizeMb;
        }

        return Math.Clamp(value, MinSourceFileCopyMaxSizeMb, MaxSourceFileCopyMaxSizeMb);
    }

    private ClipboardEntry? TryCreateImageEntryFromFileDrop(
        IReadOnlyList<string> normalizedPaths,
        string? appName,
        string? appPath)
    {
        if (normalizedPaths.Count != 1)
        {
            return null;
        }

        var path = normalizedPaths[0];
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !IsSupportedImageFile(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            var frame = decoder.Frames.FirstOrDefault();
            if (frame == null)
            {
                return null;
            }

            frame.Freeze();
            var imageForStorage = NormalizeTransparentImageIfNeeded(frame);
            var imageData = BitmapSourceToBytes(imageForStorage);
            var hash = ComputeHash(imageData);
            var relativePath = Task.Run(() => _imageStorageService.SaveImageAsync(imageData, hash)).GetAwaiter().GetResult();

            return new ClipboardEntry
            {
                Content = relativePath,
                ContentType = ClipboardContentType.Image,
                Preview = $"{Path.GetFileName(path)} {(int)frame.PixelWidth}x{(int)frame.PixelHeight}",
                ContentHash = hash,
                SourceAppName = appName,
                SourceAppPath = appPath,
                CopiedAt = DateTime.UtcNow
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSupportedImageFile(string path)
    {
        var extension = Path.GetExtension(path);
        return !string.IsNullOrWhiteSpace(extension) && SupportedImageExtensions.Contains(extension);
    }

    private static string ComputeFileDropContentHash(IEnumerable<string> paths)
    {
        var signature = string.Join(Environment.NewLine, paths.Select(BuildFileDropHashComponent));
        return ComputeHash(signature);
    }

    private static string BuildFileDropHashComponent(string path)
    {
        var normalizedPath = NormalizeFullPath(path) ?? path.Trim();

        if (File.Exists(normalizedPath))
        {
            var info = new FileInfo(normalizedPath);
            return $"F|{normalizedPath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }

        if (Directory.Exists(normalizedPath))
        {
            return $"D|{normalizedPath}|{Directory.GetLastWriteTimeUtc(normalizedPath).Ticks}";
        }

        return $"M|{normalizedPath}";
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

    private static string? NormalizeFullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch
        {
            return null;
        }
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        var fullPath = NormalizeFullPath(path);
        var fullDirectory = NormalizeFullPath(directory);
        if (fullPath == null || fullDirectory == null)
        {
            return false;
        }

        var directoryPrefix = fullDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        Stop();
        _settingsService.SettingsChanged -= OnSettingsChanged;
        GC.SuppressFinalize(this);
    }
}
