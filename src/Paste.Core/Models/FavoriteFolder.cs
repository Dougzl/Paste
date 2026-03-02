namespace Paste.Core.Models;

public class FavoriteFolder
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#FF6B6B";
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
