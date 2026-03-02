using Paste.Core.Models;

namespace Paste.Core.Interfaces;

public interface IFavoriteFolderService
{
    Task<List<FavoriteFolder>> GetAllAsync();
    Task<FavoriteFolder> CreateAsync(string name, string colorHex);
    Task RenameAsync(long folderId, string newName);
    Task DeleteAsync(long folderId);
    Task AddEntryToFolderAsync(long entryId, long folderId);
    Task RemoveEntryFromFolderAsync(long entryId);
}
