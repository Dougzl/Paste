using Paste.Core.Interfaces;

namespace Paste.Data.Storage;

public class ImageStorageService : IImageStorageService
{
    private static readonly string ManagedRootDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Paste");

    private readonly string _imageDir;

    public ImageStorageService()
    {
        _imageDir = Path.Combine(ManagedRootDir, "images");
        Directory.CreateDirectory(_imageDir);
    }

    public async Task<string> SaveImageAsync(byte[] imageData, string hash)
    {
        // Keep a unique file per clipboard capture, even when image bytes are identical.
        var fileName = $"{hash}_{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}.png";
        var filePath = Path.Combine(_imageDir, fileName);
        await File.WriteAllBytesAsync(filePath, imageData);

        return fileName;
    }

    public async Task<byte[]?> LoadImageAsync(string relativePath)
    {
        var filePath = ResolveManagedImagePath(relativePath);
        if (filePath == null || !File.Exists(filePath))
            return null;

        return await File.ReadAllBytesAsync(filePath);
    }

    public void DeleteImage(string relativePath)
    {
        var filePath = ResolveManagedImagePath(relativePath);
        if (filePath == null || !File.Exists(filePath))
        {
            return;
        }

        try
        {
            File.Delete(filePath);
        }
        catch
        {
            // Ignore single-file failures.
        }
    }

    private string? ResolveManagedImagePath(string pathOrName)
    {
        if (string.IsNullOrWhiteSpace(pathOrName))
        {
            return null;
        }

        var input = pathOrName.Trim();

        if (Uri.TryCreate(input, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            input = uri.LocalPath;
        }

        if (Path.IsPathRooted(input))
        {
            var fullPath = NormalizeFullPath(input);
            return fullPath != null && IsUnderDirectory(fullPath, _imageDir) ? fullPath : null;
        }

        var normalized = input.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var imagePrefix = $"images{Path.DirectorySeparatorChar}";
        if (normalized.StartsWith(imagePrefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[imagePrefix.Length..];
        }

        normalized = normalized.TrimStart(Path.DirectorySeparatorChar);
        var fileName = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        return Path.Combine(_imageDir, fileName);
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
