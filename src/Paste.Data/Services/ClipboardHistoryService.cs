using Microsoft.EntityFrameworkCore;
using Paste.Core.Interfaces;
using Paste.Core.Models;
using Paste.Data.Database;

namespace Paste.Data.Services;

public class ClipboardHistoryService : IClipboardHistoryService
{
    private readonly IDbContextFactory<PasteDbContext> _contextFactory;

    public ClipboardHistoryService(IDbContextFactory<PasteDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<ClipboardEntry>> GetRecentAsync(int count = 50)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.ClipboardEntries
            .OrderByDescending(e => e.IsPinned)
            .ThenByDescending(e => e.CopiedAt)
            .Take(count)
            .ToListAsync();
    }

    public async Task<List<ClipboardEntry>> SearchAsync(string query)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.ClipboardEntries
            .Where(e => e.Content != null && EF.Functions.Like(e.Content, $"%{query}%"))
            .OrderByDescending(e => e.CopiedAt)
            .Take(50)
            .ToListAsync();
    }

    public async Task<ClipboardEntry> AddAsync(ClipboardEntry entry)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        // Move existing duplicate to top by deleting old entry
        if (!string.IsNullOrEmpty(entry.ContentHash))
        {
            var existing = await db.ClipboardEntries
                .FirstOrDefaultAsync(e => e.ContentHash == entry.ContentHash);
            if (existing != null)
            {
                db.ClipboardEntries.Remove(existing);
            }
        }

        db.ClipboardEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry;
    }

    public async Task DeleteAsync(long id)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var entry = await db.ClipboardEntries.FindAsync(id);
        if (entry != null)
        {
            db.ClipboardEntries.Remove(entry);
            await db.SaveChangesAsync();
        }
    }

    public async Task<bool> ExistsByHashAsync(string contentHash)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.ClipboardEntries.AnyAsync(e => e.ContentHash == contentHash);
    }

    public async Task UpdatePinnedAsync(long id, bool isPinned)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var entry = await db.ClipboardEntries.FindAsync(id);
        if (entry != null)
        {
            entry.IsPinned = isPinned;
            await db.SaveChangesAsync();
        }
    }

    public async Task UpdateAliasAsync(long id, string? alias)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var entry = await db.ClipboardEntries.FindAsync(id);
        if (entry != null)
        {
            entry.Alias = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
            await db.SaveChangesAsync();
        }
    }

    public async Task ClearAllAsync()
    {
        await using var db = await _contextFactory.CreateDbContextAsync();

        // Only remove entries that are NOT in any favorite folder
        // This preserves all favorited items
        var entriesToRemove = await db.ClipboardEntries
            .Where(e => e.FavoriteFolderId == null || e.FavoriteFolderId == 0)
            .ToListAsync();

        if (entriesToRemove.Any())
        {
            db.ClipboardEntries.RemoveRange(entriesToRemove);
            await db.SaveChangesAsync();
        }
    }
}
