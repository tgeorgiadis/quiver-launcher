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
    private int _total;
    private int _catalogTotal;
    private int _hiddenInLibrary;
    // With library apps hidden, pages are read until at least this many cards show.
    private const int MinCards = 24;
    private readonly Dictionary<string, QuiverCatalogGameDetail?> _games = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<BrowseItem> Items { get; } = [];
    /// <summary>Original games a search matched, shown above the apps as on the website (at most four).</summary>
    public ObservableCollection<BrowseGame> Games { get; } = [];
    public bool HasGames => Games.Count > 0;
    public IReadOnlyList<QuiverCatalogConsole> Consoles { get; private set; } = [];
    public string Search { get; set; } = "";
    public string Sort { get; set; } = "added";
    public string? Platform { get; set; } = BrowseText.CurrentPlatform;
    /// <summary>A console id, or "maker:Brand" for every console a maker made.</summary>
    public string? Console { get; set; }
    public string? ProjectType { get; set; }
    /// <summary>"no-generated" or "no-ai" hides apps by AI use; null shows every app.</summary>
    public string? Ai { get; set; }
    /// <summary>Leaves apps already in the library out of the cards, so new ones are easier to find.</summary>
    public bool HideLibraryApps { get; set; } = true;
    /// <summary>How many of the apps read so far were left out for being in the library.</summary>
    public int HiddenInLibrary { get => _hiddenInLibrary; private set => Set(ref _hiddenInLibrary, value); }
    /// <summary>How many apps the catalog has, or the player's list when it shows.</summary>
    public int Total { get => _total; private set => Set(ref _total, value); }
    public bool HasCustomList => !string.IsNullOrWhiteSpace(customListLocation());
    public bool ShowingCustomList { get => _showingCustomList && HasCustomList; set => Set(ref _showingCustomList, value); }
    public bool HasFilters => Platform != BrowseText.CurrentPlatform || Console != null || ProjectType != null;
    public bool IsLoading { get => _isLoading; private set => Set(ref _isLoading, value); }
    public bool IsLoadingMore { get => _isLoadingMore; private set => Set(ref _isLoadingMore, value); }
    public bool CanLoadMore => _cursor != null && !IsLoading && !IsLoadingMore && !ShowingCustomList;
    /// <summary>Why nothing shows: an error, or an empty result.</summary>
    public string Status { get => _status; private set => Set(ref _status, value); }

    /// <summary>Clears the search and filters, like the website's Clear filters; the AI filter is a preference and stays.</summary>
    public void ClearFilters()
    {
        Search = "";
        Platform = BrowseText.CurrentPlatform;
        Console = null;
        ProjectType = null;
    }

    /// <summary>A maker's heading in the console list, as the website words it.</summary>
    public static string BrandName(string brand) => brand == "OtherPlatforms" ? "Other platforms" : brand;
    public static string AllOfBrand(string brand) => brand == "OtherPlatforms" ? "All other platforms" : $"All {brand}";

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
        HiddenInLibrary = 0;
        if (Games.Count > 0)
        {
            Games.Clear();
            Notify(nameof(HasGames));
        }
        try
        {
            if (ShowingCustomList)
            {
                await LoadCustomListAsync(generation, token);
                return;
            }
            if (Consoles.Count == 0) await LoadFacetsAsync(token);
            Total = _catalogTotal;
            var query = new QuiverCatalogQuery(Search, Platform, Console, ProjectType, Sort, Ai);
            var page = await client.GetAppsAsync(query, null, token);
            if (generation != _generation) return;
            Show(page);
            if (!await FillAsync(query, generation, MinCards, token)) return;
            if (Items.Count == 0 && HiddenInLibrary > 0)
                Status = "Everything that matches is already in your library.";
            else if (Items.Count == 0)
            {
                Status = "Nothing matches that search and those filters.";
                // Usage data: what players look for and don't find, by length only (never the words typed).
                Telemetry.Current.Track("search_no_results", new Dictionary<string, object?>
                {
                    ["query_length"] = Search.Trim().Length,
                    ["filtered"] = query.Os != BrowseText.CurrentPlatform || query.Console != null || query.ProjectType != null || query.Ai != null,
                });
            }
            _ = FindGamesAsync(generation, page.Items, token);
        }
        catch (Exception ex) when (generation == _generation && !token.IsCancellationRequested)
        {
            Telemetry.Current.Track("catalog_load_failed", new Dictionary<string, object?> { ["reason"] = Telemetry.ReasonOf(ex) });
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

    /// <summary>A game and every app that plays it, read once per session.</summary>
    public async Task<QuiverCatalogGameDetail?> GetGameAsync(string slug, CancellationToken token)
    {
        if (_games.TryGetValue(slug, out var known)) return known;
        var game = await client.GetGameAsync(slug, token);
        if (game != null) game.Entries.Sort(BrowseText.ByPlayerFeedback);
        return _games[slug] = game;
    }

    // The games a search matched: from the apps it found, the games whose title has every word searched for.
    private async Task FindGamesAsync(int generation, IReadOnlyList<QuiverCatalogApp> apps, CancellationToken token)
    {
        var search = Search.Trim();
        if (search.Length < 2) return;
        var matched = apps.SelectMany(a => a.Games).Where(g => !string.IsNullOrWhiteSpace(g.Slug) && BrowseText.TitleMatches(g.Title, search))
            .DistinctBy(g => g.Slug, StringComparer.OrdinalIgnoreCase).Take(4).ToList();
        if (matched.Count == 0) return;
        try
        {
            var details = await Task.WhenAll(matched.Select(g => GetGameAsync(g.Slug, token)));
            if (generation != _generation) return;
            foreach (var detail in details)
                if (detail is { Entries.Count: > 0 } found)
                    Games.Add(new BrowseGame(found.Game.Slug, found.Game.Title, BrowseGame.ArtFor(found.Game), found.Entries.Count));
            Notify(nameof(HasGames));
        }
        // The apps still show; the games row is a shortcut to them.
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            System.Diagnostics.Debug.WriteLine($"Matching games unavailable: {ex.Message}");
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
            var query = new QuiverCatalogQuery(Search, Platform, Console, ProjectType, Sort, Ai);
            var page = await client.GetAppsAsync(query, _cursor, token);
            if (generation != _generation) return false;
            var shown = Items.Count;
            Show(page);
            if (!await FillAsync(query, generation, shown + MinCards / 2, token)) return false;
            Status = "";
            return Items.Count > shown;
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
        MarkLibraryState(Items);
        Notify(nameof(JustAdded));
    }

    /// <summary>
    /// Apps added from these cards while library apps are hidden: they stay, showing they were added, until the player
    /// comes back to the catalog (<see cref="HideAddedApps"/>) or the list loads again.
    /// </summary>
    public int JustAdded => HideLibraryApps ? Items.Count(i => i.InLibrary) : 0;

    /// <summary>Coming back to the catalog: apps added last time are hidden now, like the rest of the library.</summary>
    public void HideAddedApps()
    {
        if (!HideLibraryApps) return;
        var added = Items.Where(i => i.InLibrary).ToList();
        if (added.Count == 0) return;
        foreach (var item in added) Items.Remove(item);
        HiddenInLibrary += added.Count;
        Notify(nameof(JustAdded));
    }

    /// <summary>Marks cards whose app is already in the library: these, or a game page's.</summary>
    public void MarkLibraryState(IEnumerable<BrowseItem> items)
    {
        var apps = library();
        var folders = apps.Select(a => a.FolderName?.Trim()).Where(f => !string.IsNullOrEmpty(f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // A library app is linked to its catalog entry by the entry's id, else by repository (CatalogReleases), so one in a
        // folder of its own (added before the catalog renamed its folder, or given another name) still counts. The folder is
        // the fallback.
        var slugs = apps.Select(a => a.CatalogSlug).Where(s => !string.IsNullOrEmpty(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ids = apps.Select(a => a.CatalogEntryId).Where(id => !string.IsNullOrEmpty(id)).ToHashSet(StringComparer.Ordinal);
        foreach (var item in items)
            item.InLibrary = item.ListApp != null
                ? LibraryAddService.FindExisting(apps, item.ListApp) != null
                : (item.App?.Id is { Length: > 0 } id && ids.Contains(id)) || (item.App?.Slug is { } slug && slugs.Contains(slug)) ||
                  folders.Contains(item.FolderName.Trim());
    }

    /// <summary>A game's apps as cards, best first, as on its page.</summary>
    public List<BrowseItem> CardsFor(QuiverCatalogGameDetail game)
    {
        var cards = game.Entries.Select(app => BrowseItem.FromCatalog(app, _consoleNames)).ToList();
        MarkLibraryState(cards);
        return cards;
    }

    /// <summary>An app's card, for opening its page from elsewhere (the Library).</summary>
    public BrowseItem CardFor(QuiverCatalogApp app)
    {
        var card = BrowseItem.FromCatalog(app, _consoleNames);
        MarkLibraryState([card]);
        return card;
    }

    /// <summary>A console's name, as the catalog lists it.</summary>
    public string ConsoleName(string id) => _consoleNames.GetValueOrDefault(id, id.ToUpperInvariant());

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
        HiddenInLibrary = 0;
        Notify(nameof(CanLoadMore));
        Notify(nameof(HasCustomList));
        Notify(nameof(ShowingCustomList));
    }

    private void Show(QuiverCatalogPage<QuiverCatalogApp> page)
    {
        _cursor = page.NextCursor;
        AddCards(page.Items.Select(app => BrowseItem.FromCatalog(app, _consoleNames)).ToList());
    }

    private void AddCards(List<BrowseItem> cards)
    {
        MarkLibraryState(cards);
        var hidden = 0;
        foreach (var card in cards)
        {
            if (HideLibraryApps && card.InLibrary) hidden++;
            else Items.Add(card);
        }
        if (hidden > 0) HiddenInLibrary += hidden;
    }

    // With library apps hidden a page can come back nearly empty: read on until enough cards show. False once a newer load took over.
    private async Task<bool> FillAsync(QuiverCatalogQuery query, int generation, int wanted, CancellationToken token)
    {
        while (HideLibraryApps && Items.Count < wanted && _cursor != null)
        {
            var page = await client.GetAppsAsync(query, _cursor, token);
            if (generation != _generation) return false;
            Show(page);
        }
        return generation == _generation;
    }

    private async Task LoadFacetsAsync(CancellationToken token)
    {
        try
        {
            var facets = await client.GetFacetsAsync(token);
            Consoles = facets.Consoles;
            _catalogTotal = facets.Total;
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
        Total = _customApps.Count;
        var search = Search.Trim();
        AddCards(_customApps.Where(a => search.Length == 0 || AppSearch.Matches(a, search))
            .OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase).Select(BrowseItem.FromList).ToList());
        if (Items.Count == 0)
            Status = _customApps.Count == 0 ? "Your app list has no apps."
                : HiddenInLibrary > 0 ? "Everything that matches is already in your library."
                : "Nothing in your app list matches that search.";
    }
}
