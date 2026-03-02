using Paste.Core.Models;

namespace Paste.Core.Interfaces;

public interface ISettingsService
{
    AppSettings Load();
    void Save(AppSettings settings);
    event EventHandler<AppSettings>? SettingsChanged;
}
