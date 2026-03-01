using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Paste.Core.Interfaces;
using Paste.Core.Models;
using Clipboard = System.Windows.Clipboard;

namespace Paste.App.Services;

public class ClipboardMonitor : IClipboardMonitor
{
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
                    var imageData = BitmapSourceToBytes(image);
                    var hash = ComputeHash(imageData);
                    // Use Task.Run to avoid deadlock — SaveImageAsync posts back
                    // to SynchronizationContext which is blocked by .GetResult()
                    var relativePath = Task.Run(() => _imageStorageService.SaveImageAsync(imageData, hash)).GetAwaiter().GetResult();

                    entry = new ClipboardEntry
                    {
                        Content = relativePath,
                        ContentType = ClipboardContentType.Image,
                        Preview = $"Image {(int)image.Width}x{(int)image.Height}",
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

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
