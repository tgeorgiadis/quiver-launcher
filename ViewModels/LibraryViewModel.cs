using System.Collections.ObjectModel;
using System.ComponentModel;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.ViewModels;

public sealed class LibraryViewModel : ObservableViewModel, IDisposable
{
    private readonly GameManager _manager;
    private readonly GameGridViewModel _sorting = new();
    private CancellationTokenSource? _search;
    private GameInfo? _continue;
    private string _sortBy = "Name";
    private bool _closed;
    public SettingsViewModel Settings { get; }
    public ObservableCollection<GameInfo> Games => _manager.Games;
    public ObservableCollection<TagDisplayFilterListItem> TagDisplayFilters { get; } = new();
    public string SortBy { get => _sortBy; set => Set(ref _sortBy, value); }
    public GameInfo? ContinueGameInfo { get => _continue; private set => Set(ref _continue, value); }
    public bool IsContinueVisible => ContinueGameInfo != null;
    public bool IsLibraryEmpty => _manager.IsLibraryEmpty;
    public bool HasNoSearchMatches => _manager.HasNoLibrarySearchMatches;
    public string SearchText => _manager.LibrarySearchText;
    public double CardPixelSize => PlatformCapabilities.IsMobile ? double.NaN : Settings.SlotSize;

    public LibraryViewModel(GameManager manager, SettingsViewModel settings)
    {
        _manager = manager; Settings = settings;
        manager.PropertyChanged += ManagerChanged;
        settings.PropertyChanged += SettingsChanged;
    }
    private void ManagerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GameManager.IsLibraryEmpty) or nameof(GameManager.HasNoLibrarySearchMatches) or nameof(GameManager.LibrarySearchText)) Notify(null);
    }
    private void SettingsChanged(object? sender, PropertyChangedEventArgs e) => Notify(nameof(CardPixelSize));
    public void RefreshContinue()
    {
        if (_closed) return;
        ContinueGameInfo = _manager.GetLatestPlayedInstalledGame();
        Notify(nameof(IsContinueVisible));
    }
    public void ApplySorting() => _sorting.ApplySort(Games, SortBy, _manager.GamesFolder, Settings.Current.IgnoreArticlesWhenSorting);
    public async Task RefreshManualStatusesAsync(CancellationToken token)
    {
        foreach (var game in Games.Where(g => g.IsManuallyManaged).ToList())
        {
            if (_closed || token.IsCancellationRequested) return;
            try { await game.CheckStatusAsync(_manager.HttpClient, _manager.GamesFolder); }
            catch { /* Returning from the file explorer is a best-effort refresh. */ }
        }
        if (_closed || token.IsCancellationRequested) return;
        ApplySorting();
        RefreshContinue();
    }
    public void RefreshFilters()
    {
        var settings = Settings.Current;
        settings.EnsureInitialized();
        if (TagDisplayFilterListItem.TryUpdateSelection(TagDisplayFilters, settings.TagDisplayFilters, settings.ActiveTagDisplayFilterId)) return;
        TagDisplayFilters.Clear();
        foreach (var filter in settings.TagDisplayFilters)
            TagDisplayFilters.Add(TagDisplayFilterListItem.FromFilter(filter, string.Equals(filter.Id, settings.ActiveTagDisplayFilterId, StringComparison.OrdinalIgnoreCase)));
    }
    public void ApplyDisplaySettings()
    {
        var settings = Settings.Current;
        foreach (var game in Games)
        {
            game.LibraryNameStyle = settings.LibraryNameStyle;
            game.ShowLibraryUpdateBadges = settings.ShowLibraryAppUpdateBadges;
            game.LibraryCardTagMaxLines = settings.LibraryCardTagMaxLines;
            game.TruncateLibraryCardTitles = settings.TruncateLibraryCardTitles;
        }
        AppCatalogService.RefreshLibraryCardTags(Games, settings);
    }
    public async Task<bool> SearchAsync(string query, CancellationToken lifetime, bool debounce = true)
    {
        if (_closed) return false;
        CancelSearch();
        var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        _search = request;
        try
        {
            if (debounce && !string.IsNullOrWhiteSpace(query)) await Task.Delay(250, request.Token);
            if (request.IsCancellationRequested || !ReferenceEquals(_search, request)) return false;
            _manager.LibrarySearchText = query;
            _manager.ApplyTagDisplayFilter(Settings.Current);
            ApplySorting();
            Notify(null);
            return true;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { return false; }
        finally
        {
            if (ReferenceEquals(_search, request)) _search = null;
            request.Dispose();
        }
    }
    public void CancelSearch() { _search?.Cancel(); _search = null; }
    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        CancelSearch();
        _manager.PropertyChanged -= ManagerChanged;
        Settings.PropertyChanged -= SettingsChanged;
    }
}
