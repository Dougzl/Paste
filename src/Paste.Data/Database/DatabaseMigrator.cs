using Microsoft.EntityFrameworkCore;
using Paste.Data.Database;

namespace Paste.Data.Database;

public static class DatabaseMigrator
{
    public static async Task MigrateAsync(IDbContextFactory<PasteDbContext> contextFactory)
    {
        await using var db = await contextFactory.CreateDbContextAsync();

        // Ensure the database and base tables exist
        await db.Database.EnsureCreatedAsync();

        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();

        try
        {
            // Create FavoriteFolders table if missing
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT COUNT(*) FROM sqlite_master
                    WHERE type='table' AND name='FavoriteFolders'";
                var exists = Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;

                if (!exists)
                {
                    cmd.CommandText = @"
                        CREATE TABLE FavoriteFolders (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            Name TEXT NOT NULL DEFAULT '',
                            ColorHex TEXT NOT NULL DEFAULT '#FF6B6B',
                            SortOrder INTEGER NOT NULL DEFAULT 0,
                            CreatedAt TEXT NOT NULL DEFAULT '0001-01-01T00:00:00'
                        )";
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            // Add FavoriteFolderId column to ClipboardEntries if missing
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(ClipboardEntries)";
                var hasFavCol = false;
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        if (reader.GetString(1) == "FavoriteFolderId")
                        {
                            hasFavCol = true;
                            break;
                        }
                    }
                }

                if (!hasFavCol)
                {
                    cmd.CommandText = @"
                        ALTER TABLE ClipboardEntries
                        ADD COLUMN FavoriteFolderId INTEGER NULL
                        REFERENCES FavoriteFolders(Id)";
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }
        finally
        {
            await conn.CloseAsync();
        }
    }
}
