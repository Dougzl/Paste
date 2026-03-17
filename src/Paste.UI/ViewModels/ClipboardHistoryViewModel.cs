using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paste.Core.Interfaces;
using Paste.Core.Models;

namespace Paste.UI.ViewModels;

public partial class SourceAppFilter : ObservableObject
{
    public string AppName { get; set; } = string.Empty;
    public string? AppPath { get; set; }

    [ObservableProperty]
    private bool _isSelected;
}

public partial class TimeFilterOption : ObservableObject
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}

public partial class FavoriteFolderViewModel : ObservableObject
{
    public long Id { get; set; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _colorHex = "#FF6B6B";

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isCompact;

    [ObservableProperty]
    private bool _isEditing;

    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();
}

public partial class ClipboardHistoryViewModel : ObservableObject
{
    private readonly IClipboardHistoryService _historyService;
    private readonly IFavoriteFolderService? _favoriteFolderService;
    private bool _isSyncingCustomDateFromPreset;
    private string? _lastHash;
    private List<ClipboardEntry> _allEntries = new();

    [ObservableProperty]
    private ObservableCollection<ClipboardEntry> _entries = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ClipboardEntry? _selectedEntry;

    [ObservableProperty]
    private ObservableCollection<SourceAppFilter> _sourceAppFilters = new();

    [ObservableProperty]
    private ObservableCollection<FavoriteFolderViewModel> _favoriteFolders = new();

    [ObservableProperty]
    private ObservableCollection<TimeFilterOption> _timeFilters = new();

    [ObservableProperty]
    private FavoriteFolderViewModel? _selectedFolder;

    [ObservableProperty]
    private DateTime? _selectedCustomDate;

    [ObservableProperty]
    private bool _isCustomDateActive;

    [ObservableProperty]
    private bool _isSearchVisible;

    private static readonly string[] ColorPalette =
    {
        "#FF6B6B", "#4ECDC4", "#45B7D1", "#96CEB4",
        "#FFEAA7", "#DDA0DD", "#98D8C8", "#F7DC6F"
    };

    // The window handle of the app that was active before Paste window was shown
    public IntPtr LastForegroundWindow { get; set; }

    // Action to hide the window after paste
    public Action? HideWindowAction { get; set; }

    // Paste service and action set from outside (DI)
    public IPasteService? PasteService { get; set; }

    // Callback to open settings window. Set by the host window.
    public Action? ShowSettingsAction { get; set; }

    public ClipboardHistoryViewModel(IClipboardHistoryService historyService)
    {
        _historyService = historyService;
        InitializeTimeFilters();
    }

    public ClipboardHistoryViewModel(IClipboardHistoryService historyService, IFavoriteFolderService favoriteFolderService)
    {
        _historyService = historyService;
        _favoriteFolderService = favoriteFolderService;
        InitializeTimeFilters();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilters();
    }

    partial void OnSelectedCustomDateChanged(DateTime? value)
    {
        if (_isSyncingCustomDateFromPreset)
            return;

        if (!value.HasValue)
            return;

        IsCustomDateActive = true;
        foreach (var f in TimeFilters)
            f.IsSelected = false;
        ApplyFilters();
    }

    [RelayCommand]
    private void ToggleCustomDateFilter()
    {
        if (IsCustomDateActive)
        {
            IsCustomDateActive = false;
            ApplyFilters();
            return;
        }

        foreach (var f in TimeFilters)
            f.IsSelected = false;

        if (!SelectedCustomDate.HasValue)
            SelectedCustomDate = DateTime.Today;

        IsCustomDateActive = true;
        ApplyFilters();
    }

    public async Task LoadEntriesAsync()
    {
        var entries = await _historyService.GetRecentAsync();
        _allEntries = new List<ClipboardEntry>(entries);
        RebuildAppFilters();
        ApplyFilters();

        if (_favoriteFolderService != null)
            await LoadFoldersAsync();
    }

    private async Task LoadFoldersAsync()
    {
        if (_favoriteFolderService == null) return;
        var folders = await _favoriteFolderService.GetAllAsync();
        var isCompact = folders.Count > 5;
        var currentSelectedId = SelectedFolder?.Id;

        FavoriteFolders = new ObservableCollection<FavoriteFolderViewModel>(
            folders.Select(f => new FavoriteFolderViewModel
            {
                Id = f.Id,
                Name = f.Name,
                ColorHex = f.ColorHex,
                IsSelected = f.Id == currentSelectedId,
                IsCompact = isCompact
            }));

        SelectedFolder = FavoriteFolders.FirstOrDefault(f => f.Id == currentSelectedId);
    }

    public async Task HandleClipboardChangedAsync(ClipboardEntry entry)
    {
        // Deduplicate non-image clipboard content only.
        // Image captures should always be kept as separate entries.
        if (entry.ContentType != ClipboardContentType.Image && entry.ContentHash == _lastHash)
            return;

        _lastHash = entry.ContentHash;
        await _historyService.AddAsync(entry);

        // Insert at top of local cache (after pinned items)
        var insertIndex = 0;
        foreach (var e in _allEntries)
        {
            if (e.IsPinned)
                insertIndex++;
            else
                break;
        }
        _allEntries.Insert(insertIndex, entry);

        RebuildAppFilters();
        ApplyFilters();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            await LoadEntriesAsync();
            return;
        }

        var results = await _historyService.SearchAsync(SearchText);
        _allEntries = new List<ClipboardEntry>(results);
        RebuildAppFilters();
        ApplyFilters();
    }

    [RelayCommand]
    private void ToggleSearch()
    {
        IsSearchVisible = !IsSearchVisible;
        if (!IsSearchVisible)
            SearchText = string.Empty;
    }

    [RelayCommand]
    private void OpenSettings()
    {
        ShowSettingsAction?.Invoke();
    }

    [RelayCommand]
    private void PasteSelected()
    {
        if (SelectedEntry == null || PasteService == null) return;
        var entry = SelectedEntry;
        var targetWindow = LastForegroundWindow;
        PasteService.SetClipboardContent(entry);
        HideWindowAction?.Invoke();
        PasteService.ActivateAndPaste(targetWindow);
    }

    [RelayCommand]
    private void PasteEntry(ClipboardEntry? entry)
    {
        if (entry == null || PasteService == null) return;
        var targetWindow = LastForegroundWindow;
        PasteService.SetClipboardContent(entry);
        HideWindowAction?.Invoke();
        PasteService.ActivateAndPaste(targetWindow);
    }

    [RelayCommand]
    private async Task DeleteEntry(ClipboardEntry? entry)
    {
        if (entry == null) return;

        // Find index in current Entries for auto-selecting next.
        // Use Id instead of object reference to avoid stale selection references.
        var currentIndex = Entries
            .Select((item, index) => new { item.Id, index })
            .FirstOrDefault(x => x.Id == entry.Id)?.index ?? -1;

        // If viewing a folder, only remove from folder (don't delete the entry)
        if (SelectedFolder != null && _favoriteFolderService != null)
        {
            await _favoriteFolderService.RemoveEntryFromFolderAsync(entry.Id);
            entry.FavoriteFolderId = null;
        }
        else
        {
            await _historyService.DeleteAsync(entry.Id);
            _allEntries.RemoveAll(e => e.Id == entry.Id);
        }

        RebuildAppFilters();
        ApplyFilters();

        // Auto-select next (or previous if deleted last) for continuous deletion
        if (Entries.Count > 0)
        {
            var selectIndex = Math.Min(currentIndex, Entries.Count - 1);
            if (selectIndex < 0) selectIndex = 0;
            SelectedEntry = Entries[selectIndex];
        }
        else
        {
            SelectedEntry = null;
        }
    }

    [RelayCommand]
    private async Task TogglePin(ClipboardEntry? entry)
    {
        if (entry == null) return;
        entry.IsPinned = !entry.IsPinned;
        await _historyService.UpdatePinnedAsync(entry.Id, entry.IsPinned);
        await LoadEntriesAsync();
    }

    [RelayCommand]
    private void ToggleAppFilter(SourceAppFilter? filter)
    {
        if (filter == null) return;

        if (filter.IsSelected)
        {
            filter.IsSelected = false;
        }
        else
        {
            foreach (var f in SourceAppFilters)
                f.IsSelected = false;
            filter.IsSelected = true;

            // Mutual exclusion: deselect any folder
            foreach (var f in FavoriteFolders)
                f.IsSelected = false;
            SelectedFolder = null;
        }

        ApplyFilters();
    }

    [RelayCommand]
    private void SelectFolder(FavoriteFolderViewModel? folder)
    {
        if (folder == null) return;

        if (folder.IsSelected)
        {
            folder.IsSelected = false;
            SelectedFolder = null;
        }
        else
        {
            foreach (var f in FavoriteFolders)
                f.IsSelected = false;
            folder.IsSelected = true;
            SelectedFolder = folder;

            // Mutual exclusion: deselect any app filter
            foreach (var f in SourceAppFilters)
                f.IsSelected = false;
        }

        ApplyFilters();
    }

    [RelayCommand]
    private void SelectTimeFilter(TimeFilterOption? filter)
    {
        if (filter == null)
            return;

        if (filter.IsSelected)
        {
            filter.IsSelected = false;
            IsCustomDateActive = false;
            ApplyFilters();
            return;
        }

        foreach (var f in TimeFilters)
            f.IsSelected = false;

        filter.IsSelected = true;
        _isSyncingCustomDateFromPreset = true;
        try
        {
            SelectedCustomDate = filter.Key switch
            {
                "today" => DateTime.Today,
                "yesterday" => DateTime.Today.AddDays(-1),
                "threeDaysAgo" => DateTime.Today.AddDays(-2),
                _ => SelectedCustomDate
            };
        }
        finally
        {
            _isSyncingCustomDateFromPreset = false;
        }
        IsCustomDateActive = false;
        ApplyFilters();
    }

    [RelayCommand]
    private async Task CreateFolder()
    {
        if (_favoriteFolderService == null) return;
        var colorIndex = FavoriteFolders.Count % ColorPalette.Length;
        await _favoriteFolderService.CreateAsync("New Folder", ColorPalette[colorIndex]);
        await LoadFoldersAsync();
    }

    [RelayCommand]
    private async Task RenameFolder((long folderId, string newName) args)
    {
        if (_favoriteFolderService == null) return;
        await _favoriteFolderService.RenameAsync(args.folderId, args.newName);
        await LoadFoldersAsync();
    }

    [RelayCommand]
    private async Task DeleteFolder(long folderId)
    {
        if (_favoriteFolderService == null) return;

        if (SelectedFolder?.Id == folderId)
            SelectedFolder = null;

        await _favoriteFolderService.DeleteAsync(folderId);
        await LoadFoldersAsync();
        await LoadEntriesAsync();
    }

    [RelayCommand]
    private async Task AddEntryToFolder((long entryId, long folderId) args)
    {
        if (_favoriteFolderService == null) return;
        await _favoriteFolderService.AddEntryToFolderAsync(args.entryId, args.folderId);

        var entry = _allEntries.FirstOrDefault(e => e.Id == args.entryId);
        if (entry != null)
            entry.FavoriteFolderId = args.folderId;

        ApplyFilters();
    }

    [RelayCommand]
    private async Task RemoveEntryFromFolder(ClipboardEntry? entry)
    {
        if (entry == null || _favoriteFolderService == null) return;
        await _favoriteFolderService.RemoveEntryFromFolderAsync(entry.Id);
        entry.FavoriteFolderId = null;
        ApplyFilters();
    }

    [RelayCommand]
    private async Task UpdateAlias((long entryId, string? alias) args)
    {
        await _historyService.UpdateAliasAsync(args.entryId, args.alias);
        var entry = _allEntries.FirstOrDefault(e => e.Id == args.entryId);
        if (entry != null)
            entry.Alias = string.IsNullOrWhiteSpace(args.alias) ? null : args.alias.Trim();
        ApplyFilters();
    }

    [RelayCommand]
    private async Task UpdateContent((long entryId, string content) args)
    {
        await _historyService.UpdateContentAsync(args.entryId, args.content);
        var entry = _allEntries.FirstOrDefault(e => e.Id == args.entryId);
        if (entry != null)
            entry.Content = args.content;
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var filtered = _allEntries.AsEnumerable();

        var selectedTimeFilter = GetSelectedTimeFilterKey();
        if (!string.IsNullOrEmpty(selectedTimeFilter))
        {
            var today = DateTime.Today;
            DateTime? targetDate = selectedTimeFilter switch
            {
                "today" => today,
                "yesterday" => today.AddDays(-1),
                "threeDaysAgo" => today.AddDays(-2),
                _ => null
            };

            if (targetDate.HasValue)
            {
                var date = targetDate.Value;
                filtered = filtered.Where(e => ToLocalDate(e.CopiedAt) == date);
            }
        }
        else if (IsCustomDateActive && SelectedCustomDate.HasValue)
        {
            var date = SelectedCustomDate.Value.Date;
            filtered = filtered.Where(e => ToLocalDate(e.CopiedAt) == date);
        }

        // Filter by selected folder
        if (SelectedFolder != null)
        {
            var folderId = SelectedFolder.Id;
            filtered = filtered.Where(e => e.FavoriteFolderId == folderId);
        }

        // Filter by selected app
        var selectedApp = SourceAppFilters.FirstOrDefault(f => f.IsSelected);
        if (selectedApp != null)
        {
            filtered = filtered.Where(e =>
                string.Equals(e.SourceAppName, selectedApp.AppName, StringComparison.OrdinalIgnoreCase));
        }

        // Filter by search text
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var query = SearchText.Trim();
            filtered = filtered.Where(e => MatchesSearch(e, query));
        }

        Entries = new ObservableCollection<ClipboardEntry>(filtered);
    }

    private void InitializeTimeFilters()
    {
        TimeFilters = new ObservableCollection<TimeFilterOption>
        {
            new() { Key = "today", Label = "今天" },
            new() { Key = "yesterday", Label = "昨天" },
            new() { Key = "threeDaysAgo", Label = "前天" }
        };
        SelectedCustomDate = DateTime.Today;
        IsCustomDateActive = false;
    }

    private string? GetSelectedTimeFilterKey()
    {
        return TimeFilters.FirstOrDefault(f => f.IsSelected)?.Key;
    }

    private static DateTime ToLocalDate(DateTime dateTime)
    {
        if (dateTime.Kind == DateTimeKind.Unspecified)
            dateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);

        return dateTime.ToLocalTime().Date;
    }

    private static bool MatchesSearch(ClipboardEntry entry, string query)
    {
        if (entry.ContentType == ClipboardContentType.FilePaths)
        {
            return GetFileNames(entry.Content)
                .Any(fileName => fileName.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        return (entry.Content != null && entry.Content.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
               (entry.Preview != null && entry.Preview.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> GetFileNames(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            yield break;
        }

        foreach (var line in content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var fileName = Path.GetFileName(line.Trim());
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                yield return fileName;
            }
        }
    }

    public void RebuildAppFilters()
    {
        var currentSelected = SourceAppFilters.FirstOrDefault(f => f.IsSelected)?.AppName;

        var apps = _allEntries
            .Where(e => !string.IsNullOrWhiteSpace(e.SourceAppName))
            .GroupBy(e => e.SourceAppName!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g =>
            {
                var representative = g.First();
                return new SourceAppFilter
                {
                    AppName = representative.SourceAppName!,
                    AppPath = representative.SourceAppPath,
                    IsSelected = string.Equals(representative.SourceAppName, currentSelected, StringComparison.OrdinalIgnoreCase)
                };
            })
            .ToList();

        SourceAppFilters = new ObservableCollection<SourceAppFilter>(apps);
    }
}
