using Microsoft.EntityFrameworkCore;
using Paste.Core.Models;

namespace Paste.Data.Database;

public class PasteDbContext : DbContext
{
    public DbSet<ClipboardEntry> ClipboardEntries => Set<ClipboardEntry>();
    public DbSet<FavoriteFolder> FavoriteFolders => Set<FavoriteFolder>();

    public PasteDbContext(DbContextOptions<PasteDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ClipboardEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Content).HasMaxLength(1_000_000);
            entity.Property(e => e.Preview).HasMaxLength(500);
            entity.Property(e => e.ContentHash).HasMaxLength(64);
            entity.Property(e => e.SourceAppName).HasMaxLength(256);
            entity.Property(e => e.SourceAppPath).HasMaxLength(1024);
            entity.Property(e => e.Alias).HasMaxLength(256);

            // Ignore computed/UI properties
            entity.Ignore(e => e.ImageFullPath);

            entity.HasIndex(e => e.CopiedAt).IsDescending();
            entity.HasIndex(e => e.ContentHash);
            entity.HasIndex(e => e.ContentType);
            entity.HasIndex(e => e.FavoriteFolderId);
        });

        modelBuilder.Entity<FavoriteFolder>(entity =>
        {
            entity.HasKey(f => f.Id);
            entity.Property(f => f.Id).ValueGeneratedOnAdd();
            entity.Property(f => f.Name).HasMaxLength(100);
            entity.Property(f => f.ColorHex).HasMaxLength(20);
        });
    }
}
