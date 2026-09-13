using System.Collections.ObjectModel;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.ViewModels;

public class CatalogViewModel : ObservableViewModel
{
    private SettingsViewModel? _settings;
    private ICatalogSourcesService? _service;
    private Func<string, string, bool, Task<bool>> _message = (_, _, _) => Task.FromResult(false);
    private Func<Task> _reloadLibrary = () => Task.CompletedTask;
    private Func<Task> _refreshBadges = () => Task.CompletedTask;
    private Func<Task> _promptReview = () => Task.CompletedTask;
    private bool _refreshingList;
    private bool _refreshListRequested;
    private bool _refreshingAll;
    public ObservableCollection<CatalogSourceListItem> Sources { get; } = new();
    public event Action? ListChanged;
    public bool IsRefreshing => _refreshingAll;
    public int PendingReviewCount => Sources.Where(s => s.Enabled).Sum(s => s.PendingReviewCount);
    public bool IsEmpty => Sources.Count == 0;
    public string EmptyMessage => SourceListFilter switch
    {
        CatalogSourceListFilter.Enabled => "No enabled catalog sources.",
        CatalogSourceListFilter.Disabled => "No disabled catalog sources.",
        _ when _settings?.Current.AppCatalogSources.Count == 0 => "No catalog sources yet. Add a source to subscribe to an external app list.",
        _ => "No catalog sources match this filter.",
    };

    public void Configure(SettingsViewModel settings, ICatalogSourcesService service,
        Func<string, string, bool, Task<bool>> message, Func<Task> reloadLibrary,
        Func<Task> refreshBadges, Func<Task> promptReview)
    {
        _settings = settings;
        _service = service;
        _message = message;
        _reloadLibrary = reloadLibrary;
        _refreshBadges = refreshBadges;
        _promptReview = promptReview;
    }

    public async Task RefreshAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_refreshingList) { _refreshListRequested = true; return; }
        _refreshingList = true;
        try
        {
            // Render the last known summaries without waiting for all catalog files to be compared.
            if (Sources.Count == 0)
            {
                RefreshSourceList(Sources, _settings!.Current);
                Notify(null);
                ListChanged?.Invoke();
            }
            while (true)
            {
                token.ThrowIfCancellationRequested();
                _refreshListRequested = false;
                var settings = _settings!.Current;
                var reviewed = settings.AppCatalogSources
                    .Select(s => (s.Id, s.AcknowledgedListVersion, s.UpdateAvailable)).ToArray();
                try { await _service!.RefreshUsageAsync(settings); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Catalog source usage stats refresh failed: {ex.Message}"); }
                token.ThrowIfCancellationRequested();
                // A settings reload can replace every source while file reads are
                // pending. Recompute on the current objects rather than publishing
                // their default zero counts or saving an obsolete review state.
                if (!ReferenceEquals(settings, _settings.Current)) continue;
                if (!reviewed.SequenceEqual(settings.AppCatalogSources
                        .Select(s => (s.Id, s.AcknowledgedListVersion, s.UpdateAvailable))))
                    _settings.SaveCatalogState();
                if (_refreshListRequested) continue;
                break;
            }
            token.ThrowIfCancellationRequested();
            // Read the current filter after the load so a changed filter wins over an older request.
            RefreshSourceList(Sources, _settings!.Current);
            Notify(null);
            ListChanged?.Invoke();
        }
        finally { _refreshingList = false; }
    }

    public void RefreshPresentation()
    {
        RefreshSourceList(Sources, _settings!.Current);
        Notify(null);
        ListChanged?.Invoke();
    }

    public async Task AddAsync(string location, CancellationToken token)
    {
        var (apps, version, error) = await _service!.LoadAsync(location);
        token.ThrowIfCancellationRequested();
        if (error != null) { await _message($"Could not load catalog source:\n{error}", "Invalid Source", false); return; }
        if (apps.Count == 0) { await _message("The catalog source loaded successfully but contains no apps.", "Empty Catalog", false); return; }
        var source = CreateSource(location);
        await _service.RegisterAsync(source);
        token.ThrowIfCancellationRequested();
        _settings!.Current.AppCatalogSources.Add(source);
        _settings.SaveCurrent();
        await RefreshAsync(token);
        token.ThrowIfCancellationRequested();
        var versionLabel = string.IsNullOrWhiteSpace(version) ? "unknown" : version;
        await _message($"Added \"{source.Name}\" ({apps.Count} app(s), v{versionLabel}). Use App Catalog → Review to add apps to your library.", "Source Added", false);
    }

    public async Task RemoveAsync(string id, CancellationToken token)
    {
        var source = _settings!.Current.AppCatalogSources.FirstOrDefault(s => s.Id == id);
        if (source == null) return;
        var confirmed = await _message($"Remove catalog source \"{source.Name}\"?\n\nApps already in your local apps.json will stay in your library.", "Remove Source", true);
        token.ThrowIfCancellationRequested();
        if (!confirmed) return;
        _service!.DeleteCache(id);
        _settings.Current.AppCatalogSources.RemoveAll(s => s.Id == id);
        _settings.SaveCurrent();
        await RefreshAsync(token);
    }

    public async Task SetEnabledAsync(string id, bool enabled, CancellationToken token)
    {
        var source = _settings!.Current.AppCatalogSources.FirstOrDefault(s => s.Id == id);
        if (source == null || source.Enabled == enabled) return;
        source.Enabled = enabled;
        _settings.SaveCurrent();
        // Keep the visible snapshot current while availability checks are pending,
        // so another toggle can reverse this change without waiting for a reload.
        foreach (var row in Sources.Where(row => row.SourceId == id))
            row.Enabled = enabled;
        await _service!.RefreshAvailabilityAsync(source);
        token.ThrowIfCancellationRequested();
        await RefreshAsync(token);
        await _reloadLibrary();
        token.ThrowIfCancellationRequested();
        await _refreshBadges();
    }

    public async Task RefreshAllAsync(CancellationToken token)
    {
        if (_refreshingAll) return;
        _refreshingAll = true;
        Notify(nameof(IsRefreshing));
        try
        {
            await _service!.RefreshAllAsync(_settings!.Current);
            token.ThrowIfCancellationRequested();
            _settings.SaveCurrent();
            _settings.Load().EnsureInitialized();
            await RefreshAsync(token);
            await _refreshBadges();
            token.ThrowIfCancellationRequested();
            if (UpdatePromptPolicy.ShouldPromptCatalogUpdates(_settings.Current)) await _promptReview();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested) await _message($"Failed to refresh catalog sources: {ex.Message}", "Refresh Error", false);
        }
        finally { _refreshingAll = false; if (!token.IsCancellationRequested) Notify(nameof(IsRefreshing)); }
    }
    public CatalogSourceListFilter SourceListFilter { get; set; } = CatalogSourceListFilter.Enabled;

    public IReadOnlyList<CatalogSourceListItem> BuildSourceListItems(
        AppSettings settings,
        CatalogSourceListFilter? filter = null)
    {
        settings.EnsureInitialized();
        var activeFilter = filter ?? SourceListFilter;

        return settings.AppCatalogSources
            .Where(s => activeFilter switch
            {
                CatalogSourceListFilter.Enabled => s.Enabled,
                CatalogSourceListFilter.Disabled => !s.Enabled,
                _ => true,
            })
            .OrderByDescending(s => s.Enabled)
            .ThenByDescending(s => s.PendingReviewCount > 0)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(CatalogSourceListItem.FromSource)
            .ToList();
    }

    public void RefreshSourceList(ObservableCollection<CatalogSourceListItem> target, AppSettings settings)
    {
        var items = BuildSourceListItems(settings);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var existing = target.FirstOrDefault(row => row.SourceId == item.SourceId);
            if (existing != null && existing.HasSamePresentation(item)) item = existing;
            if (i < target.Count && ReferenceEquals(target[i], item)) continue;
            var oldIndex = target.IndexOf(item);
            if (oldIndex >= 0) target.Move(oldIndex, i);
            else if (i < target.Count && target[i].SourceId == item.SourceId) target[i] = item;
            else target.Insert(i, item);
        }
        while (target.Count > items.Count) target.RemoveAt(target.Count - 1);
    }

    public AppCatalogSource CreateSource(string location) =>
        new()
        {
            Name = "",
            Location = location,
            Enabled = true,
            LastFetchedUtc = DateTime.UtcNow,
            LastError = null,
        };
}
