using Paste.Core.Models;

namespace Paste.Core.Interfaces;

public interface IPasteService
{
    void PasteEntry(ClipboardEntry entry, IntPtr targetWindow);
    void SetClipboardContent(ClipboardEntry entry);
    void ActivateAndPaste(IntPtr targetWindow);
}
