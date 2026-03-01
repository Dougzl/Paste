using Paste.Core.Models;

namespace Paste.Core.Interfaces;

public interface IClipboardMonitor : IDisposable
{
    event EventHandler<ClipboardEntry>? ClipboardChanged;
    void Start();
    void Stop();
}
