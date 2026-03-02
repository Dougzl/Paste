using Microsoft.EntityFrameworkCore;
using Paste.Core.Interfaces;
using Paste.Core.Models;
using Paste.Data.Database;

namespace Paste.Data.Services;

public class FavoriteFolderService : IFavoriteFolderService
{
    private readonly IDbContextFactory<PasteDbContext> _contextFactory;

    public FavoriteFolderService(IDbContextFactory<PasteDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<FavoriteFolder>> GetAllAsync()
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.FavoriteFolders
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.CreatedAt)
            .ToListAsync();
    }

    public async Task<FavoriteFolder> CreateAsync(string name, string colorHex)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var maxSort = await db.FavoriteFolders.AnyAsync()
            ? await db.FavoriteFolders.MaxAsync(f => f.SortOrder)
            : 0;

        var folder = new FavoriteFolder
        {
            Name = name,
            ColorHex = colorHex,
            SortOrder = maxSort + 1
        };

        db.FavoriteFolders.Add(folder);
        await db.SaveChangesAsync();
        return folder;
    }

    public async Task RenameAsync(long folderId, string newName)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var folder = await db.FavoriteFolders.FindAsync(folderId);
        if (folder != null)
        {
            folder.Name = newName;
            await db.SaveChangesAsync();
        }
    }

    public async Task DeleteAsync(long folderId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        // Clear folder reference from entries
        var entries = await db.ClipboardEntries
            .Where(e => e.FavoriteFolderId == folderId)
            .ToListAsync();
        foreach (var entry in entries)
            entry.FavoriteFolderId = null;

        var folder = await db.FavoriteFolders.FindAsync(folderId);
        if (folder != null)
            db.FavoriteFolders.Remove(folder);

        await db.SaveChangesAsync();
    }

    public async Task AddEntryToFolderAsync(long entryId, long folderId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var entry = await db.ClipboardEntries.FindAsync(entryId);
        if (entry != null)
        {
            entry.FavoriteFolderId = folderId;
            await db.SaveChangesAsync();
        }
    }

    public async Task RemoveEntryFromFolderAsync(long entryId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var entry = await db.ClipboardEntries.FindAsync(entryId);
        if (entry != null)
        {
            entry.FavoriteFolderId = null;
            await db.SaveChangesAsync();
        }
    }
}
