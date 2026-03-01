using System.Collections.ObjectModel;
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

public partial class ClipboardHistoryViewModel : ObservableObject
{
    private readonly IClipboardHistoryService _historyService;
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

    // The window handle of the app that was active before Paste window was shown
    public IntPtr LastForegroundWindow { get; set; }

    // Action to hide the window after paste
    public Action? HideWindowAction { get; set; }

    // Paste service and action set from outside (DI)
    public IPasteService? PasteService { get; set; }

    public ClipboardHistoryViewModel(IClipboardHistoryService historyService)
    {
        _historyService = historyService;
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilters();
    }

    public async Task LoadEntriesAsync()
    {
        var entries = await _historyService.GetRecentAsync();
        _allEntries = new List<ClipboardEntry>(entries);
        RebuildAppFilters();
        ApplyFilters();
    }

    public async Task HandleClipboardChangedAsync(ClipboardEntry entry)
    {
        // Deduplicate: skip if same hash as last entry
        if (entry.ContentHash == _lastHash)
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
    private void PasteSelected()
    {
        if (SelectedEntry == null || PasteService == null) return;
        var entry = SelectedEntry;
        var targetWindow = LastForegroundWindow;
        // 1. Set clipboard while we still have foreground permission
        PasteService.SetClipboardContent(entry);
        // 2. Hide our window
        HideWindowAction?.Invoke();
        // 3. Activate target and send Ctrl+V
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
        await _historyService.DeleteAsync(entry.Id);
        _allEntries.Remove(entry);
        Entries.Remove(entry);
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

        // If clicking an already-selected filter, deselect it (show all)
        if (filter.IsSelected)
        {
            filter.IsSelected = false;
        }
        else
        {
            // Deselect all others, select this one
            foreach (var f in SourceAppFilters)
                f.IsSelected = false;
            filter.IsSelected = true;
        }

        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var filtered = _allEntries.AsEnumerable();

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
            filtered = filtered.Where(e =>
                (e.Content != null && e.Content.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (e.Preview != null && e.Preview.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        Entries = new ObservableCollection<ClipboardEntry>(filtered);
    }

    private void RebuildAppFilters()
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
