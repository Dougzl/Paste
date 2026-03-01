using Paste.Core.Interfaces;

namespace Paste.Data.Storage;

public class ImageStorageService : IImageStorageService
{
    private readonly string _imageDir;

    public ImageStorageService()
    {
        _imageDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Paste", "images");
        Directory.CreateDirectory(_imageDir);
    }

    public async Task<string> SaveImageAsync(byte[] imageData, string hash)
    {
        var fileName = $"{hash}.png";
        var filePath = Path.Combine(_imageDir, fileName);

        if (!File.Exists(filePath))
        {
            await File.WriteAllBytesAsync(filePath, imageData);
        }

        return fileName;
    }

    public async Task<byte[]?> LoadImageAsync(string relativePath)
    {
        var filePath = Path.Combine(_imageDir, relativePath);
        if (!File.Exists(filePath))
            return null;

        return await File.ReadAllBytesAsync(filePath);
    }

    public void DeleteImage(string relativePath)
    {
        var filePath = Path.Combine(_imageDir, relativePath);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}
