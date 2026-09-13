using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.ViewModels;

public enum CatalogReviewAction { AddAll, MergeAll, Acknowledge, Ignore, Hide, Unhide, Add, Replace, Merge, Remove }

/// <summary>Owns review requests and serializes mutations against a captured source and row.</summary>
public sealed class CatalogReviewWorkspace : ObservableViewModel, IDisposable
{
    private readonly CatalogSyncViewModel _model;
    private readonly SettingsViewModel _settings;
    private readonly ICatalogReviewService _service;
    private readonly Func<string, string, bool, Task<bool>> _message;
    private readonly Func<Task> _refreshSourcesAndBadges;
    private readonly CatalogMutationQueue _mutations;
    private readonly Func<Task> _refreshAdditionBadges;
    private readonly HashSet<(string Source, string App)> _pendingAdds = [];
    private List<GameInfo>? _committedLibrary;
    private CancellationTokenSource? _load;
    private int _generation;
    private int _presentedGeneration = -1;
    private bool _busy;
    private bool _loading;
    private string? _error;
    public AppCatalogSource? ActiveSource { get; private set; }
    public bool IsLoading { get => _loading; private set => Set(ref _loading, value); }
    public bool IsBusy { get => _busy; private set => Set(ref _busy, value); }
    public string? Error { get => _error; private set => Set(ref _error, value); }
    public event Action? RowsChanged;
    public event Action? AdditionRowsChanged;
    public event Action<AppCatalogSource>? SourceOpened;
    public int Generation => _generation;

    public CatalogReviewWorkspace(CatalogSyncViewModel model, SettingsViewModel settings, ICatalogReviewService service,
        Func<string, string, bool, Task<bool>> message, Func<Task> refreshSourcesAndBadges, CatalogMutationQueue? mutations = null,
        Func<Task>? refreshAdditionBadges = null)
    {
        _model = model; _settings = settings; _service = service; _message = message; _refreshSourcesAndBadges = refreshSourcesAndBadges;
        _mutations = mutations ?? new CatalogMutationQueue();
        _refreshAdditionBadges = refreshAdditionBadges ?? refreshSourcesAndBadges;
    }

    public async Task OpenAsync(AppCatalogSource source, CatalogReviewFilter filter, CancellationToken lifetime)
    {
        Close();
        ActiveSource = source;
        _model.RevealPendingPlatforms = false;
        _model.ResetPlatformPresentation();
        var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        _load = request;
        var generation = _generation;
        _model.ReviewFilter = filter == CatalogReviewFilter.New ? CatalogReviewFilter.NotInLibrary : filter;
        _model.SearchText = "";
        IsLoading = true;
        Error = null;
        SourceOpened?.Invoke(source);
        try
        {
            var local = await _service.LoadLocalAsync();
            var cached = await _service.LoadCachedAsync(source.Id);
            if (!Current(source, generation, request.Token)) return;
            // Cached catalog + cached public metadata are already available together.
            if (cached.Count > 0) await PublishAsync(source, local, cached, generation, request.Token);
            var metadataRefresh = _service.RefreshPlatformMetadataAsync(source, false, request.Token);
            try
            {
                await _service.FetchAsync(source);
                if (!Current(source, generation, request.Token)) return;
                _settings.SaveCurrent();
                local = await _service.LoadLocalAsync();
                var fetched = await _service.LoadCachedAsync(source.Id);
                // A slow shared index must not delay the catalog payload. Unknown
                // compatibility is visible until evidence arrives independently.
                if (Current(source, generation, request.Token))
                    await PublishAsync(source, local, fetched, generation, request.Token);
            }
            finally { await metadataRefresh; }
            if (Current(source, generation, request.Token)) _model.DeferPlatformDiscoveries();
        }
        catch (Exception) when (!Current(source, generation, request.Token)) { }
        catch (Exception ex) { Error = $"Failed to load catalog: {ex.Message}"; }
        finally
        {
            if (Current(source, generation, request.Token)) IsLoading = false;
            if (ReferenceEquals(_load, request)) _load = null;
            request.Dispose();
        }
    }

    public bool Current(AppCatalogSource source, int generation, CancellationToken token) =>
        !token.IsCancellationRequested && generation == _generation && ReferenceEquals(source, ActiveSource);

    private async Task PublishAsync(AppCatalogSource source, List<GameInfo> local, List<GameInfo> external, int generation, CancellationToken token)
    {
        using var timing = QuiverLauncher.Core.Services.CatalogPerformance.Measure("publish", external.Count);
        var prepared = await Task.Run(() => CatalogSyncViewModel.PrepareComparison(local, external), token);
        if (!Current(source, generation, token)) return;
        using var uiTiming = CatalogPerformance.Measure("publish-ui", external.Count);
        _model.PlatformFilters = _settings.Current.CatalogPlatformFilters;
        _model.IgnoreArticlesWhenSorting = _settings.Current.IgnoreArticlesWhenSorting;
        _model.Refresh(source, local, external, _settings.Current, prepared);
        CatalogReviewEligibility.Reconcile(source, _model.AllRows, _settings.Current);
        foreach (var row in _model.AllRows)
            if (_pendingAdds.Contains((source.Id, row.External?.InstanceKey ?? row.IdentityKey))) row.SetAddPending(true);
        // Opening the view can apply its default filters before rows arrive. That
        // empty presentation must not turn the first real payload into deferred discoveries.
        if (_presentedGeneration != _generation)
        {
            _model.AcceptPlatformDiscoveries();
            _presentedGeneration = _generation;
        }
        else _model.EnsurePlatformPresentation();
        RowsChanged?.Invoke();
    }

    public async Task RefreshAsync(CancellationToken token)
    {
        var source = ActiveSource;
        var generation = _generation;
        if (source == null) return;
        var local = await _service.LoadLocalAsync();
        var external = await _service.LoadCachedAsync(source.Id);
        if (!Current(source, generation, token)) return;
        _service.RefreshAvailability(source, local, external);
        await PublishAsync(source, local, external, generation, token);
    }

    public CatalogSyncRowItem? FindRow(string key) => _model.AllRows.FirstOrDefault(r =>
        string.Equals(r.IdentityKey, key, StringComparison.OrdinalIgnoreCase) ||
        r.ReviewKey.Equals(key, StringComparison.OrdinalIgnoreCase) || r.Repository.Equals(key, StringComparison.OrdinalIgnoreCase));

    public Task ExecuteAsync(CatalogReviewAction action, string? key, CancellationToken token)
    {
        if (token.IsCancellationRequested || ActiveSource is not { } source) return Task.CompletedTask;
        var generation = _generation;
        var row = key == null ? null : FindRow(key);
        if (key != null && row == null) return Task.CompletedTask;
        if (action == CatalogReviewAction.Add)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            if (row?.CanAdd != true || row.External == null) return Task.CompletedTask;
            var identity = (source.Id, row.External.InstanceKey);
            if (!_pendingAdds.Add(identity)) return Task.CompletedTask;
            // Change only the existing button, synchronously with input. Do not filter,
            // acknowledge, refresh the list, or move focus before a successful save.
            row.SetAddPending(true);
            var feedback = watch.Elapsed.TotalMilliseconds;
            var external = CatalogCompareService.CloneForLocal(row.External, _settings.Current.AutoUpdateNewlyAddedApps);
            return _mutations.Enqueue(() => AddAsync(source, row, external, identity, token, watch, feedback));
        }
        // Capture filtered bulk targets before waiting so later navigation cannot broaden a bulk action.
        var add = action == CatalogReviewAction.AddAll ? _model.GetFilteredBulkAddRows() : [];
        var blocked = action == CatalogReviewAction.AddAll ? _model.GetFilteredBlockedAddRows() : [];
        var merge = action == CatalogReviewAction.MergeAll ? _model.GetFilteredBulkReplaceRows() : [];
        return _mutations.Enqueue(() => ExecuteQueuedAsync(action, source, row, generation, add, blocked, merge, token));
    }

    private async Task AddAsync(AppCatalogSource source, CatalogSyncRowItem row, GameInfo external,
        (string Source, string App) identity, CancellationToken lifetime, System.Diagnostics.Stopwatch watch, double feedback)
    {
        var saved = false;
        double persistence = 0, presentation = 0, reconciliation = 0;
        try
        {
            var phase = watch.Elapsed.TotalMilliseconds;
            var result = await _service.CommitAddAsync(source, external, external.AutoUpdate);
            persistence = watch.Elapsed.TotalMilliseconds - phase;
            phase = watch.Elapsed.TotalMilliseconds;
            _committedLibrary = result.Library;
            saved = result.Outcome != CatalogAddOutcome.FolderConflict;
            if (saved) CatalogCompareService.ClearIgnoredChange(source, row.ReviewKey);
            _pendingAdds.Remove(identity);
            row.SetAddPending(false, saved);
            if (_model.Source != null)
            {
                foreach (var current in _model.AllRows.Where(r => _model.Source.Id == source.Id && r.External?.InstanceKey == identity.App))
                    current.SetAddPending(false, saved);
                _model.ReconcileAddition(result.Library);
                if (ActiveSource != null && !lifetime.IsCancellationRequested)
                {
                    if (AdditionRowsChanged != null) AdditionRowsChanged.Invoke();
                    else RowsChanged?.Invoke();
                }
            }
            reconciliation = watch.Elapsed.TotalMilliseconds - phase;
            if (!saved)
                await _message($"Could not add '{row.DisplayName}': {result.Error}", "Could not add app", false);
            else
            {
                if (result.App != null)
                {
                    try
                    {
                        phase = watch.Elapsed.TotalMilliseconds;
                        await _service.PresentAddedAsync(result.App);
                        presentation = watch.Elapsed.TotalMilliseconds - phase;
                    }
                    catch (Exception ex)
                    {
                        if (!lifetime.IsCancellationRequested)
                            await _message($"'{row.DisplayName}' was added, but preparation failed: {ex.Message}", "App preparation", false);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (!lifetime.IsCancellationRequested)
                await _message(saved ? $"'{row.DisplayName}' was added, but catalog bookkeeping failed: {ex.Message}" :
                    $"Could not add '{row.DisplayName}': {ex.Message}", "Catalog Error", false);
        }
        finally
        {
            _pendingAdds.Remove(identity);
            row.SetAddPending(false, saved);
            if (_model.Source?.Id == source.Id)
                foreach (var current in _model.AllRows.Where(r => r.External?.InstanceKey == identity.App))
                    current.SetAddPending(false, saved);
        }
        // Also flush earlier successful additions when the final queued save failed.
        if (_mutations.PendingCount == 1 && _committedLibrary is { } library)
        {
            var phase = watch.Elapsed.TotalMilliseconds;
            _committedLibrary = null;
            try
            {
                await _service.ReconcileAddedAsync(library, _model.Source, _model.AllRows);
                _settings.SaveCatalogState();
                if (!lifetime.IsCancellationRequested) await _refreshAdditionBadges();
            }
            catch (Exception ex)
            {
                if (!lifetime.IsCancellationRequested)
                    await _message($"Saved apps are retained, but catalog notifications could not refresh: {ex.Message}", "Catalog Error", false);
            }
            reconciliation += watch.Elapsed.TotalMilliseconds - phase;
        }
        CatalogAddDiagnostics.Report(new(feedback, persistence, presentation, reconciliation, watch.Elapsed.TotalMilliseconds));
    }

    private async Task ExecuteQueuedAsync(CatalogReviewAction action, AppCatalogSource source, CatalogSyncRowItem? row,
        int generation, IReadOnlyList<CatalogSyncRowItem> add, IReadOnlyList<CatalogSyncRowItem> blocked,
        IReadOnlyList<CatalogSyncRowItem> merge, CancellationToken token)
    {
        if (!Current(source, generation, token)) return;
        IsBusy = true;
        try
        {
            if (action == CatalogReviewAction.Remove)
            {
                if (row?.CanRemoveFromLibrary != true) return;
                var confirm = await _message($"Remove '{row.DisplayName}' from your Library?\n\nYour files will not be deleted.", "Remove from Library", true);
                if (!confirm || !Current(source, generation, token)) return;
            }
            switch (action)
            {
                case CatalogReviewAction.Acknowledge: _service.Acknowledge(source); break;
                case CatalogReviewAction.Ignore:
                    if (row?.CanIgnore != true) return;
                    CatalogCompareService.IgnoreChangesForCurrentVersion(source, row.ReviewKey); break;
                case CatalogReviewAction.Hide: CatalogCompareService.HideFromReview(source, row!.ReviewKey); break;
                case CatalogReviewAction.Unhide: CatalogCompareService.UnhideFromReview(source, row!.ReviewKey); break;
                default:
                    if (action == CatalogReviewAction.AddAll && add.Count == 0)
                    {
                        if (blocked.Count > 0) await ShowBlockedAsync(blocked);
                        return;
                    }
                    if (action == CatalogReviewAction.MergeAll && merge.Count == 0) return;
                    var local = await _service.LoadLocalAsync();
                    if (!Current(source, generation, token)) return;
                    var updated = action switch
                    {
                        CatalogReviewAction.AddAll => CatalogCompareService.ApplyAddAllExternalOnly(local, add, _settings.Current.AutoUpdateNewlyAddedApps),
                        CatalogReviewAction.MergeAll => CatalogCompareService.ApplyMergeAllChanged(local, merge),
                        CatalogReviewAction.Replace => CatalogCompareService.ApplyRowReplace(local, row!),
                        CatalogReviewAction.Merge => CatalogCompareService.ApplyRowMerge(local, row!),
                        CatalogReviewAction.Remove => CatalogCompareService.ApplyRowRemove(local, row!),
                        _ => local,
                    };
                    if (row != null)
                    {
                        if (action == CatalogReviewAction.Remove) CatalogCompareService.IgnoreChangesForCurrentVersion(source, row.ReviewKey);
                        else CatalogCompareService.ClearIgnoredChange(source, row.ReviewKey);
                    }
                    await _service.SaveLocalAsync(updated, source, token);
                    _committedLibrary = null; // This full mutation reconciles its own authoritative state.
                    if (!Current(source, generation, token)) return;
                    if (action == CatalogReviewAction.AddAll && blocked.Count > 0) await ShowBlockedAsync(blocked);
                    break;
            }
            if (!Current(source, generation, token)) return;
            await RefreshAsync(token);
            if (!Current(source, generation, token)) return;
            _settings.SaveCurrent();
            await _refreshSourcesAndBadges();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (Current(source, generation, token)) await _message($"Failed to update catalog: {ex.Message}", "Catalog Error", false);
        }
        finally { _busy = false; if (!token.IsCancellationRequested) Notify(nameof(IsBusy)); }
    }

    private Task<bool> ShowBlockedAsync(IReadOnlyList<CatalogSyncRowItem> rows) =>
        _message(CatalogCompareService.FormatAddBlockedMessage(rows), rows.Count == 1 ? "Could not add app" : "Could not add some apps", false);

    public void Close()
    {
        ++_generation;
        _load?.Cancel();
        _load = null;
        ActiveSource = null;
        IsLoading = false;
    }
    public void Dispose() => Close();
}
