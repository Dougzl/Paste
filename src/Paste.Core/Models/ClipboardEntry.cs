namespace Paste.Core.Models;

public class ClipboardEntry
{
    private static readonly string ImageDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Paste", "images");

    public long Id { get; set; }
    public string? Content { get; set; }
    public ClipboardContentType ContentType { get; set; }
    public string? Preview { get; set; }
    public string? SourceAppName { get; set; }
    public string? SourceAppPath { get; set; }
    public string? ContentHash { get; set; }
    public DateTime CopiedAt { get; set; } = DateTime.UtcNow;
    public bool IsPinned { get; set; }

    /// <summary>
    /// Full filesystem path to the image file, for Image entries only.
    /// </summary>
    public string? ImageFullPath =>
        ContentType == ClipboardContentType.Image && !string.IsNullOrEmpty(Content)
            ? Path.Combine(ImageDir, Content)
            : null;
}
