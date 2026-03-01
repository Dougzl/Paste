namespace Paste.Core.Interfaces;

public interface IImageStorageService
{
    Task<string> SaveImageAsync(byte[] imageData, string hash);
    Task<byte[]?> LoadImageAsync(string relativePath);
    void DeleteImage(string relativePath);
}
