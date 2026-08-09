using System.Diagnostics;

namespace QuiverLauncher.Services.Mods;

/// <summary>Loads packages across an app's configured mod sources.</summary>
public sealed class ModCatalogLoader
{
    public const int DefaultPageSize = 30;

    private readonly ModProviderRegistry _registry;

    public ModCatalogLoader(ModProviderRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public IReadOnlyList<(GameModSource Source, ModSourceRef Parsed, IModProvider Provider)> ResolveSources(
        IEnumerable<GameModSource>? sources)
    {
        var result = new List<(GameModSource, ModSourceRef, IModProvider)>();
        foreach (var source in GameModsConfig.NormalizeSources(sources))
        {
            if (!_registry.TryGet(source.Provider, out var provider))
            {
                Debug.WriteLine($"Unknown mod provider '{source.Provider}', skipping.");
                continue;
            }

            if (!provider.TryParseSource(source.SourceUrl, out var parsed))
            {
                Debug.WriteLine($"Could not parse mod source URL '{source.SourceUrl}' for {source.Provider}.");
                continue;
            }

            result.Add((source, parsed, provider));
        }

        return result;
    }

    public bool HasPagedSources(IEnumerable<GameModSource>? sources) =>
        ResolveSources(sources).Any(s => s.Provider.SupportsPagedListing);

    public bool HasRemoteSearchSources(IEnumerable<GameModSource>? sources) =>
        ResolveSources(sources).Any(s => s.Provider.SupportsRemoteSearch);

    public async Task<IReadOnlyList<ModPackage>> LoadAllPackagesAsync(
        IEnumerable<GameModSource>? sources,
        bool forceRefresh = false,
        ModListOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var resolved = ResolveSources(sources);
        var packages = new List<ModPackage>();
        options ??= ModListOptions.Default;

        foreach (var (_, parsed, provider) in resolved)
        {
            if (forceRefresh)
                provider.ForceRefreshOnNextList();

            try
            {
                var listed = await provider.ListPackagesAsync(parsed, options, cancellationToken)
                    .ConfigureAwait(false);
                packages.AddRange(listed);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to list mods from {parsed.DisplayLabel}: {ex.Message}");
            }
        }

        return packages;
    }

    public async Task<ModBrowseSession> LoadBrowseSessionAsync(
        IEnumerable<GameModSource>? sources,
        string? sourceFilterKey,
        bool forceRefresh = false,
        int pageSize = DefaultPageSize,
        ModListOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var resolved = FilterResolved(sources, sourceFilterKey);
        options ??= ModListOptions.Default;

        var packages = new List<ModPackage>();
        var pageStates = new List<ModSourcePageState>();
        int? totalHint = null;

        foreach (var (_, parsed, provider) in resolved)
        {
            if (forceRefresh)
                provider.ForceRefreshOnNextList();

            try
            {
                if (!provider.SupportsPagedListing)
                {
                    var listed = await provider.ListPackagesAsync(parsed, options, cancellationToken)
                        .ConfigureAwait(false);
                    packages.AddRange(listed);
                    pageStates.Add(new ModSourcePageState(parsed, provider, NextPageToken: null, IsDone: true));
                    continue;
                }

                var page = await provider
                    .ListPackagesPageAsync(parsed, pageToken: null, pageSize, options, cancellationToken)
                    .ConfigureAwait(false);
                packages.AddRange(page.Packages);
                if (page.TotalCount is int count)
                    totalHint = (totalHint ?? 0) + count;

                pageStates.Add(new ModSourcePageState(
                    parsed,
                    provider,
                    page.NextPageToken,
                    IsDone: page.NextPageToken == null));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to list mods from {parsed.DisplayLabel}: {ex.Message}");
                pageStates.Add(new ModSourcePageState(parsed, provider, NextPageToken: null, IsDone: true));
            }
        }

        return new ModBrowseSession
        {
            Packages = packages,
            SourceStates = pageStates,
            TotalCountHint = totalHint,
            IsSearch = false,
            SearchQuery = null,
        };
    }

    public async Task<ModBrowseSession> LoadMoreBrowseSessionAsync(
        ModBrowseSession session,
        int pageSize = DefaultPageSize,
        ModListOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.CanLoadMore)
            return session;

        options ??= ModListOptions.Default;
        var packages = session.Packages.ToList();
        var states = new List<ModSourcePageState>();
        int? totalHint = session.TotalCountHint;

        foreach (var state in session.SourceStates)
        {
            if (state.IsDone || state.NextPageToken == null)
            {
                states.Add(state);
                continue;
            }

            try
            {
                ModPackagePage page;
                if (session.IsSearch && !string.IsNullOrWhiteSpace(session.SearchQuery) &&
                    state.Provider.SupportsRemoteSearch)
                {
                    page = await state.Provider
                        .SearchPackagesPageAsync(
                            state.Source,
                            session.SearchQuery!,
                            state.NextPageToken,
                            pageSize,
                            options,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    page = await state.Provider
                        .ListPackagesPageAsync(state.Source, state.NextPageToken, pageSize, options, cancellationToken)
                        .ConfigureAwait(false);
                }

                ModCatalogListBuilder.AppendUniquePackages(packages, page.Packages);
                if (page.TotalCount is int count)
                    totalHint = totalHint ?? count;

                states.Add(state with
                {
                    NextPageToken = page.NextPageToken,
                    IsDone = page.NextPageToken == null,
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load more mods from {state.Source.DisplayLabel}: {ex.Message}");
                states.Add(state with { NextPageToken = null, IsDone = true });
            }
        }

        return new ModBrowseSession
        {
            Packages = packages,
            SourceStates = states,
            TotalCountHint = totalHint,
            IsSearch = session.IsSearch,
            SearchQuery = session.SearchQuery,
        };
    }

    public async Task<ModBrowseSession> LoadSearchSessionAsync(
        IEnumerable<GameModSource>? sources,
        string? sourceFilterKey,
        string query,
        int pageSize = DefaultPageSize,
        ModListOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var resolved = FilterResolved(sources, sourceFilterKey);
        options ??= ModListOptions.Default;
        var trimmed = query.Trim();

        var packages = new List<ModPackage>();
        var pageStates = new List<ModSourcePageState>();
        int? totalHint = null;

        foreach (var (_, parsed, provider) in resolved)
        {
            try
            {
                if (!provider.SupportsRemoteSearch)
                {
                    var listed = await provider.ListPackagesAsync(parsed, options, cancellationToken)
                        .ConfigureAwait(false);
                    var filtered = listed.Where(p => PackageMatchesQuery(p, trimmed)).ToList();
                    packages.AddRange(filtered);
                    pageStates.Add(new ModSourcePageState(parsed, provider, NextPageToken: null, IsDone: true));
                    continue;
                }

                var page = await provider
                    .SearchPackagesPageAsync(parsed, trimmed, pageToken: null, pageSize, options, cancellationToken)
                    .ConfigureAwait(false);
                packages.AddRange(page.Packages);
                if (page.TotalCount is int count)
                    totalHint = (totalHint ?? 0) + count;

                pageStates.Add(new ModSourcePageState(
                    parsed,
                    provider,
                    page.NextPageToken,
                    IsDone: page.NextPageToken == null));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to search mods from {parsed.DisplayLabel}: {ex.Message}");
                pageStates.Add(new ModSourcePageState(parsed, provider, NextPageToken: null, IsDone: true));
            }
        }

        return new ModBrowseSession
        {
            Packages = packages,
            SourceStates = pageStates,
            TotalCountHint = totalHint,
            IsSearch = true,
            SearchQuery = trimmed,
        };
    }

    public Task<ModBrowseSession> LoadMoreSearchSessionAsync(
        ModBrowseSession session,
        int pageSize = DefaultPageSize,
        ModListOptions? options = null,
        CancellationToken cancellationToken = default) =>
        LoadMoreBrowseSessionAsync(session, pageSize, options, cancellationToken);

    private IReadOnlyList<(GameModSource Source, ModSourceRef Parsed, IModProvider Provider)> FilterResolved(
        IEnumerable<GameModSource>? sources,
        string? sourceFilterKey)
    {
        var resolved = ResolveSources(sources);
        if (string.IsNullOrEmpty(sourceFilterKey))
            return resolved;

        return resolved
            .Where(r => string.Equals(
                $"{r.Parsed.ProviderId}|{r.Parsed.SourceKey}",
                sourceFilterKey,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static bool PackageMatchesQuery(ModPackage package, string term) =>
        package.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
        package.Owner.Contains(term, StringComparison.OrdinalIgnoreCase) ||
        package.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ||
        package.FullName.Contains(term, StringComparison.OrdinalIgnoreCase);
}

public sealed class ModBrowseSession
{
    public IReadOnlyList<ModPackage> Packages { get; init; } = [];
    public IReadOnlyList<ModSourcePageState> SourceStates { get; init; } = [];
    public int? TotalCountHint { get; init; }
    public bool IsSearch { get; init; }
    public string? SearchQuery { get; init; }

    public bool CanLoadMore => SourceStates.Any(s => !s.IsDone && s.NextPageToken != null);
}

public sealed record ModSourcePageState(
    ModSourceRef Source,
    IModProvider Provider,
    string? NextPageToken,
    bool IsDone);
