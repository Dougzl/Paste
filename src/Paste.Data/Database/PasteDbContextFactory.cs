using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Paste.Data.Database;

public class PasteDbContextFactory : IDbContextFactory<PasteDbContext>, IDesignTimeDbContextFactory<PasteDbContext>
{
    private readonly string _connectionString;

    public PasteDbContextFactory()
    {
        var dbPath = GetDefaultDbPath();
        _connectionString = $"Data Source={dbPath}";
    }

    public PasteDbContextFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    public PasteDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PasteDbContext>()
            .UseSqlite(_connectionString)
            .Options;
        return new PasteDbContext(options);
    }

    public PasteDbContext CreateDbContext(string[] args)
    {
        return CreateDbContext();
    }

    public static string GetDefaultDbPath()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Paste");
        Directory.CreateDirectory(appDataDir);
        return Path.Combine(appDataDir, "paste.db");
    }
}
