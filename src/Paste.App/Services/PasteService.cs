using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Paste.Core.Interfaces;
using Paste.Core.Models;
using Clipboard = System.Windows.Clipboard;

namespace Paste.App.Services;

public class PasteService : IPasteService
{
    private readonly IImageStorageService _imageStorageService;
    private readonly IClipboardMonitor _clipboardMonitor;

    public PasteService(IImageStorageService imageStorageService, IClipboardMonitor clipboardMonitor)
    {
        _imageStorageService = imageStorageService;
        _clipboardMonitor = clipboardMonitor;
    }

    public void PasteEntry(ClipboardEntry entry, IntPtr targetWindow)
    {
        SetClipboardContent(entry);
        ActivateAndPaste(targetWindow);
    }

    public void SetClipboardContent(ClipboardEntry entry)
    {
        try
        {
            // Suppress and keep suppressed — ResumeMonitor after paste completes
            if (_clipboardMonitor is ClipboardMonitor monitor)
                monitor.Suppress();

            switch (entry.ContentType)
            {
                case ClipboardContentType.Text:
                    if (!string.IsNullOrEmpty(entry.Content))
                        Clipboard.SetText(entry.Content);
                    break;

                case ClipboardContentType.Image:
                    if (!string.IsNullOrEmpty(entry.Content))
                    {
                        var imageData = Task.Run(() => _imageStorageService.LoadImageAsync(entry.Content)).GetAwaiter().GetResult();
                        if (imageData != null)
                        {
                            using var stream = new MemoryStream(imageData);
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.StreamSource = stream;
                            bitmap.EndInit();
                            bitmap.Freeze();
                            Clipboard.SetImage(bitmap);
                        }
                    }
                    break;

                case ClipboardContentType.FilePaths:
                    if (!string.IsNullOrEmpty(entry.Content))
                    {
                        var files = new System.Collections.Specialized.StringCollection();
                        files.AddRange(entry.Content.Split(Environment.NewLine));
                        Clipboard.SetFileDropList(files);
                    }
                    break;
            }
        }
        catch
        {
            // Clipboard operation failed silently
        }
    }

    public void ActivateAndPaste(IntPtr targetWindow)
    {
        try
        {
            if (targetWindow == IntPtr.Zero) return;

            // Only restore if the window is minimized; don't touch maximized/fullscreen
            if (NativeMethods.IsIconic(targetWindow))
                NativeMethods.ShowWindow(targetWindow, NativeMethods.SW_RESTORE);

            NativeMethods.SetForegroundWindow(targetWindow);
            Thread.Sleep(150);
            System.Windows.Forms.SendKeys.SendWait("^v");
        }
        catch
        {
            // Paste operation failed silently
        }
        finally
        {
            // Resume clipboard monitor after paste is fully complete, with delay
            // to let WM_CLIPBOARDUPDATE drain before re-enabling
            Thread.Sleep(200);
            if (_clipboardMonitor is ClipboardMonitor m)
                m.Resume();
        }
    }
}
