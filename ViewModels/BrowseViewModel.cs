using System.Collections.ObjectModel;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.ViewModels;

/// <summary>
/// Browse: the quiverlauncher.com catalog, searched and filtered on the site and read a page at
/// a time, or the player's own app list (a JSON file or URL), filtered here.
/// </summary>
public sealed class BrowseViewModel(QuiverCatalogClient client, Func<IReadOnlyList<GameInfo>> library,
    Func<string> customListLocation, Func<string, Task<(List<GameInfo> Apps, string? Error)>> loadCustomList) : ObservableViewModel
{
    private string? _cursor;
    private int _generation;
    private bool _isLoading;
    private bool _isLoadingMore;
    private string _status = "";
    private bool _showingCustomList;
    private List<GameInfo>? _customApps;
    private string? _customAppsLocation;
    private Dictionary<string, string> _consoleNames = [];

    public ObservableCollection<BrowseItem> Items { get; } = [];
    public IReadOnlyList<QuiverCatalogConsole> Consoles { get; private set; } = [];
    public string Search { get; set; } = "";
    public string Sort { get; set; } = "added";
    public string? Platform { get; set; } = BrowseText.CurrentPlatform;
    /// <summary>A console id, or "maker:Brand" for every console a maker made.</summary>
    public string? Console { get; set; }
    public string? ProjectType { get; set; }
    public bool HasCustomList => !string.IsNullOrWhiteSpace(customListLocation());
    public bool ShowingCustomList { get => _showingCustomList && HasCustomList; set => Set(ref _showingCustomList, value); }
    public bool HasFilters => Platform != BrowseText.CurrentPlatform || Console != null || ProjectType != null;
    public bool IsLoading { get => _isLoading; private set => Set(ref _isLoading, value); }
    public bool IsLoadingMore { get => _isLoadingMore; private set => Set(ref _isLoadingMore, value); }
    public bool CanLoadMore => _cursor != null && !IsLoading && !IsLoadingMore && !ShowingCustomList;
    /// <summary>Why nothing shows: an error, or an empty result.</summary>
    public string Status { get => _status; private set => Set(ref _status, value); }

    public void ClearFilters()
    {
        Platform = BrowseText.CurrentPlatform;
        Console = null;
        ProjectType = null;
    }

    public string ConsoleName(string? id) => id == null ? "All consoles"
        : id.StartsWith("maker:", StringComparison.Ordinal) ? $"All {BrandName(id["maker:".Length..])}"
        : _consoleNames.GetValueOrDefault(id, id);

    public static string BrandName(string brand) => brand == "OtherPlatforms" ? "other platforms" : brand;

    /// <summary>Loads the first page for the current search and filters, replacing the cards.</summary>
    public async Task ReloadAsync(CancellationToken token)
    {
        var generation = ++_generation;
        _cursor = null;
        IsLoading = true;
        IsLoadingMore = false;
        Status = "";
        Notify(nameof(CanLoadMore));
        Items.Clear();
        try
        {
            if (ShowingCustomList)
            {
                await LoadCustomListAsync(generation, token);
                return;
            }
            if (Consoles.Count == 0) await LoadFacetsAsync(token);
            var query = new QuiverCatalogQuery(Search, Platform, Console, ProjectType, Sort);
            var page = await client.GetAppsAsync(query, null, token);
            if (generation != _generation) return;
            Show(page);
            if (Items.Count == 0) Status = "Nothing matches that search and those filters.";
        }
        catch (Exception ex) when (generation == _generation && !token.IsCancellationRequested)
        {
            Status = $"Couldn't reach quiverlauncher.com. {ex.Message}";
        }
        finally
        {
            if (generation == _generation)
            {
                IsLoading = false;
                Notify(nameof(CanLoadMore));
            }
        }
    }

    /// <summary>Appends the next page of the catalog, if there is one.</summary>
    public async Task<bool> LoadMoreAsync(CancellationToken token)
    {
        if (!CanLoadMore) return false;
        var generation = _generation;
        IsLoadingMore = true;
        Notify(nameof(CanLoadMore));
        try
        {
            var query = new QuiverCatalogQuery(Search, Platform, Console, ProjectType, Sort);
            var page = await client.GetAppsAsync(query, _cursor, token);
            if (generation != _generation) return false;
            Show(page);
            Status = "";
            return page.Items.Count > 0;
        }
        catch (Exception ex) when (generation == _generation && !token.IsCancellationRequested)
        {
            Status = $"Couldn't load more apps. {ex.Message}";
            return false;
        }
        finally
        {
            if (generation == _generation)
            {
                IsLoadingMore = false;
                Notify(nameof(CanLoadMore));
            }
        }
    }

    /// <summary>Marks the cards of apps already in the library.</summary>
    public void RefreshLibraryState()
    {
        var apps = library();
        var folders = apps.Select(a => a.FolderName?.Trim()).Where(f => !string.IsNullOrEmpty(f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Items)
            item.InLibrary = item.ListApp != null
                ? LibraryAddService.FindExisting(apps, item.ListApp) != null
                : folders.Contains(item.FolderName.Trim());
    }

    /// <summary>The library app a card stands for, once its repository is known.</summary>
    public GameInfo? FindInLibrary(GameInfo app) => LibraryAddService.FindExisting(library(), app);

    /// <summary>Forgets the player's list, so a changed location or file is read again.</summary>
    public void ForgetCustomList()
    {
        // A load still reading the old list is dropped, so it can't show after the change.
        ++_generation;
        _cursor = null;
        _customApps = null;
        _customAppsLocation = null;
        IsLoading = false;
        IsLoadingMore = false;
        Status = "";
        Items.Clear();
        Notify(nameof(CanLoadMore));
        Notify(nameof(HasCustomList));
        Notify(nameof(ShowingCustomList));
    }

    private void Show(QuiverCatalogPage<QuiverCatalogApp> page)
    {
        foreach (var app in page.Items)
            Items.Add(BrowseItem.FromCatalog(app, _consoleNames));
        _cursor = page.NextCursor;
        RefreshLibraryState();
    }

    private async Task LoadFacetsAsync(CancellationToken token)
    {
        try
        {
            var facets = await client.GetFacetsAsync(token);
            Consoles = facets.Consoles;
            _consoleNames = facets.Consoles.ToDictionary(c => c.Id, c => c.Name, StringComparer.OrdinalIgnoreCase);
        }
        // Without console names the cards show console ids; the catalog itself still loads.
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            System.Diagnostics.Debug.WriteLine($"Catalog facets unavailable: {ex.Message}");
        }
    }

    private async Task LoadCustomListAsync(int generation, CancellationToken token)
    {
        var location = customListLocation().Trim();
        if (_customApps == null || _customAppsLocation != location)
        {
            var (apps, error) = await loadCustomList(location);
            token.ThrowIfCancellationRequested();
            if (generation != _generation) return;
            if (error != null)
            {
                Status = $"Couldn't read your app list. {error}";
                return;
            }
            _customApps = apps;
            _customAppsLocation = location;
        }
        var search = Search.Trim();
        foreach (var app in _customApps.Where(a => search.Length == 0 || AppSearch.Matches(a, search))
                     .OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase))
            Items.Add(BrowseItem.FromList(app));
        RefreshLibraryState();
        if (Items.Count == 0)
            Status = _customApps.Count == 0 ? "Your app list has no apps." : "Nothing in your app list matches that search.";
    }
}
