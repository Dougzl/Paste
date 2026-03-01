using Paste.Core.Models;

namespace Paste.Core.Interfaces;

public interface IClipboardHistoryService
{
    Task<List<ClipboardEntry>> GetRecentAsync(int count = 50);
    Task<List<ClipboardEntry>> SearchAsync(string query);
    Task<ClipboardEntry> AddAsync(ClipboardEntry entry);
    Task DeleteAsync(long id);
    Task<bool> ExistsByHashAsync(string contentHash);
    Task UpdatePinnedAsync(long id, bool isPinned);
}
