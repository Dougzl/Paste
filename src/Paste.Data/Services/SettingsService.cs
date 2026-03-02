using System.Text.Json;
using Paste.Core.Interfaces;
using Paste.Core.Models;

namespace Paste.Data.Services;

public class SettingsService : ISettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Paste", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public event EventHandler<AppSettings>? SettingsChanged;

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
        SettingsChanged?.Invoke(this, settings);
    }
}
