using Microsoft.EntityFrameworkCore;
using Paste.Core.Interfaces;
using Paste.Core.Models;
using Paste.Data.Database;

namespace Paste.Data.Services;

public class ClipboardHistoryService : IClipboardHistoryService
{
    private static readonly string ManagedRootDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Paste");
    private static readonly string ManagedImagesDir = Path.Combine(ManagedRootDir, "images");
    private static readonly string ManagedFilesDir = Path.Combine(ManagedRootDir, "files");

    private readonly IDbContextFactory<PasteDbContext> _contextFactory;
    private readonly IImageStorageService _imageStorageService;

    public ClipboardHistoryService(
        IDbContextFactory<PasteDbContext> contextFactory,
        IImageStorageService imageStorageService)
    {
        _contextFactory = contextFactory;
        _imageStorageService = imageStorageService;
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
        var imageCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Move existing duplicate to top by deleting old entry
        if (!string.IsNullOrEmpty(entry.ContentHash))
        {
            var existing = await db.ClipboardEntries
                .FirstOrDefaultAsync(e => e.ContentHash == entry.ContentHash);
            if (existing != null)
            {
                CollectPayloadCandidates(existing, imageCandidates, fileCandidates);
                db.ClipboardEntries.Remove(existing);
            }
        }

        db.ClipboardEntries.Add(entry);
        await db.SaveChangesAsync();
        await CleanupUnreferencedPayloadsAsync(db, imageCandidates, fileCandidates);
        return entry;
    }

    public async Task DeleteAsync(long id)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var entry = await db.ClipboardEntries.FindAsync(id);
        if (entry != null)
        {
            var imageCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fileCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectPayloadCandidates(entry, imageCandidates, fileCandidates);
            db.ClipboardEntries.Remove(entry);
            await db.SaveChangesAsync();
            await CleanupUnreferencedPayloadsAsync(db, imageCandidates, fileCandidates);
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

    public async Task UpdateContentAsync(long id, string content)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var entry = await db.ClipboardEntries.FindAsync(id);
        if (entry != null)
        {
            entry.Content = content ?? string.Empty;
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
            var imageCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fileCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entriesToRemove)
            {
                CollectPayloadCandidates(entry, imageCandidates, fileCandidates);
            }

            db.ClipboardEntries.RemoveRange(entriesToRemove);
            await db.SaveChangesAsync();
            await CleanupUnreferencedPayloadsAsync(db, imageCandidates, fileCandidates);
        }
    }

    public async Task CleanupExpiredAsync(int autoCleanupDays)
    {
        if (autoCleanupDays <= 0)
        {
            return;
        }

        var threshold = DateTime.UtcNow.AddDays(-autoCleanupDays);

        await using var db = await _contextFactory.CreateDbContextAsync();
        var entriesToRemove = await db.ClipboardEntries
            .Where(e => !e.IsPinned && e.FavoriteFolderId == null && e.CopiedAt < threshold)
            .ToListAsync();

        var imageCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entriesToRemove)
        {
            CollectPayloadCandidates(entry, imageCandidates, fileCandidates);
        }

        if (entriesToRemove.Count > 0)
        {
            db.ClipboardEntries.RemoveRange(entriesToRemove);
            await db.SaveChangesAsync();
        }

        await CleanupUnreferencedPayloadsAsync(db, imageCandidates, fileCandidates);
        await CleanupExpiredOrphanedManagedFilesAsync(db, threshold);
    }

    private async Task CleanupUnreferencedPayloadsAsync(
        PasteDbContext db,
        HashSet<string> imageCandidates,
        HashSet<string> fileCandidates)
    {
        if (imageCandidates.Count > 0)
        {
            var referencedImages = await db.ClipboardEntries
                .Where(e => e.ContentType == ClipboardContentType.Image && e.Content != null && e.Content != "")
                .Select(e => e.Content!)
                .ToListAsync();

            var referencedImageSet = new HashSet<string>(
                referencedImages
                    .Select(NormalizeImageContent)
                    .Where(static value => !string.IsNullOrWhiteSpace(value))!
                    .Select(static value => value!),
                StringComparer.OrdinalIgnoreCase);
            foreach (var imageContent in imageCandidates)
            {
                if (!referencedImageSet.Contains(imageContent))
                {
                    _imageStorageService.DeleteImage(imageContent);
                }
            }
        }

        if (fileCandidates.Count > 0)
        {
            var referencedFileEntries = await db.ClipboardEntries
                .Where(e => e.ContentType == ClipboardContentType.FilePaths && e.Content != null && e.Content != "")
                .Select(e => e.Content!)
                .ToListAsync();

            var referencedManagedFiles = BuildReferencedManagedFileSet(referencedFileEntries);
            foreach (var filePath in fileCandidates)
            {
                if (!referencedManagedFiles.Contains(filePath))
                {
                    TryDeleteFile(filePath);
                }
            }
        }
    }

    private async Task CleanupExpiredOrphanedManagedFilesAsync(PasteDbContext db, DateTime thresholdUtc)
    {
        var referencedImages = await db.ClipboardEntries
            .Where(e => e.ContentType == ClipboardContentType.Image && e.Content != null && e.Content != "")
            .Select(e => e.Content!)
            .ToListAsync();
        var referencedImageNames = new HashSet<string>(
            referencedImages
                .Select(NormalizeImageContent)
                .Where(static value => !string.IsNullOrWhiteSpace(value))!
                .Select(static value => value!),
            StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(ManagedImagesDir))
        {
            foreach (var file in Directory.EnumerateFiles(ManagedImagesDir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var fileName = Path.GetFileName(file);
                    if (referencedImageNames.Contains(fileName))
                    {
                        continue;
                    }

                    if (File.GetLastWriteTimeUtc(file) < thresholdUtc)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // Ignore single-file failures
                }
            }
        }

        var referencedFileEntries = await db.ClipboardEntries
            .Where(e => e.ContentType == ClipboardContentType.FilePaths && e.Content != null && e.Content != "")
            .Select(e => e.Content!)
            .ToListAsync();
        var referencedManagedFiles = BuildReferencedManagedFileSet(referencedFileEntries);

        if (Directory.Exists(ManagedFilesDir))
        {
            foreach (var file in Directory.EnumerateFiles(ManagedFilesDir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var fullPath = NormalizeFullPath(file);
                    if (fullPath == null)
                    {
                        continue;
                    }

                    if (referencedManagedFiles.Contains(fullPath))
                    {
                        continue;
                    }

                    if (File.GetLastWriteTimeUtc(file) < thresholdUtc)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // Ignore single-file failures
                }
            }
        }
    }

    private static void CollectPayloadCandidates(
        ClipboardEntry entry,
        HashSet<string> imageCandidates,
        HashSet<string> fileCandidates)
    {
        if (entry.ContentType == ClipboardContentType.Image && !string.IsNullOrWhiteSpace(entry.Content))
        {
            var imageContent = NormalizeImageContent(entry.Content);
            if (!string.IsNullOrWhiteSpace(imageContent))
            {
                imageCandidates.Add(imageContent);
            }
            return;
        }

        if (entry.ContentType != ClipboardContentType.FilePaths || string.IsNullOrWhiteSpace(entry.Content))
        {
            return;
        }

        foreach (var line in SplitLines(entry.Content))
        {
            var normalized = NormalizeFullPath(line);
            if (normalized != null && IsUnderDirectory(normalized, ManagedFilesDir))
            {
                fileCandidates.Add(normalized);
            }
        }
    }

    private static HashSet<string> BuildReferencedManagedFileSet(IEnumerable<string> fileDropContents)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var content in fileDropContents)
        {
            foreach (var line in SplitLines(content))
            {
                var normalized = NormalizeFullPath(line);
                if (normalized != null && IsUnderDirectory(normalized, ManagedFilesDir))
                {
                    set.Add(normalized);
                }
            }
        }

        return set;
    }

    private static IEnumerable<string> SplitLines(string content)
    {
        return content
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0);
    }

    private static string? NormalizeImageContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var value = content.Trim();

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            value = uri.LocalPath;
        }

        if (Path.IsPathRooted(value))
        {
            var fullPath = NormalizeFullPath(value);
            if (fullPath == null || !IsUnderDirectory(fullPath, ManagedImagesDir))
            {
                return null;
            }

            return Path.GetFileName(fullPath);
        }

        value = value.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var imagePrefix = $"images{Path.DirectorySeparatorChar}";
        if (value.StartsWith(imagePrefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value[imagePrefix.Length..];
        }

        value = value.TrimStart(Path.DirectorySeparatorChar);
        var fileName = Path.GetFileName(value);
        return string.IsNullOrWhiteSpace(fileName) ? null : fileName;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            var normalized = NormalizeFullPath(path);
            if (normalized == null || !IsUnderDirectory(normalized, ManagedFilesDir))
            {
                return;
            }

            if (File.Exists(normalized))
            {
                File.Delete(normalized);
            }
        }
        catch
        {
            // Ignore delete failures
        }
    }

    private static bool IsUnderDirectory(string fullPath, string directory)
    {
        var dirPath = NormalizeFullPath(directory);
        if (dirPath == null)
        {
            return false;
        }

        if (!dirPath.EndsWith(Path.DirectorySeparatorChar))
        {
            dirPath += Path.DirectorySeparatorChar;
        }

        return fullPath.StartsWith(dirPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }
}
