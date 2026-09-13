using QuiverLauncher.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Services;

/// <summary>Session-owned fallback queue. Navigation detaches presentation, never cancels useful work.</summary>
public sealed class CatalogReleasePrefetch : IDisposable
{
    private sealed class Job(AppCatalogSource source)
    {
        public AppCatalogSource Source = source;
        public List<CatalogSyncRowItem> Rows = [];
        public bool Enabled;
        public bool Pending;
        public bool Force;
        public int Revision;
        public HashSet<string> Attempted = [];
        public DateTimeOffset Due;
        public string Context = "";
        public Dictionary<string, int> Retries = [];
        public CatalogReleaseWarmupProgress? Progress;
    }
    private readonly LauncherSession _session;
    private readonly GameManager _manager;
    private readonly SettingsViewModel _settings;
    private readonly CatalogSyncViewModel _model;
    private readonly CatalogReviewWorkspace _workspace;
    private readonly Func<bool> _isActive;
    private readonly Func<Action, Task> _dispatch;
    private readonly Action _refresh;
    private readonly Action _reviewStatusChanged;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, Job> _jobs = [];
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly CancellationTokenSource _lifetime;
    private Task? _work;
    private bool _disposed;
    private CancellationTokenSource? _activeRequest;
    private string? _activeProvider;
    private int _viewRevision;
    internal Task Completion => _work ?? Task.CompletedTask;

    public CatalogReleasePrefetch(LauncherSession session, GameManager manager, SettingsViewModel settings,
        CatalogSyncViewModel model, CatalogReviewWorkspace workspace, Func<bool> isActive,
        Func<Action, Task> dispatch, Action refresh, TimeProvider? clock = null, Action? reviewStatusChanged = null)
    {
        _session = session; _manager = manager; _settings = settings; _model = model; _workspace = workspace;
        _isActive = isActive; _dispatch = dispatch; _refresh = refresh; _clock = clock ?? TimeProvider.System;
        _reviewStatusChanged = reviewStatusChanged ?? (() => { });
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        settings.CredentialsChanged += CredentialsChanged;
        workspace.PropertyChanged += WorkspaceChanged;
    }
    private void WorkspaceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CatalogReviewWorkspace.IsLoading) && !_workspace.IsLoading && _isActive()) Start();
    }
    private string Context => ReleaseRequestCoordinator.CredentialKey(_settings.Current.GitHubApiToken) + ":" +
        ReleaseRequestCoordinator.CredentialKey(_settings.Current.GitLabApiToken);

    private void RefreshReviewStatus(Job job)
    {
        CatalogReviewEligibility.Reconcile(job.Source, job.Rows, _settings.Current);
        _reviewStatusChanged();
    }

    private Job? Remember()
    {
        if (_disposed || _session.IsClosed || _workspace.ActiveSource is not { } source || _model.AllRows.Count == 0) return null;
        if (!_jobs.TryGetValue(source.Id, out var job))
        {
            job = new(source) { Enabled = true, Pending = true, Context = Context };
            _jobs[source.Id] = job;
        }
        job.Source = source;
        // This comparison is only about repository/release identities. Resolving
        // credentials here would reload settings from disk once for every row.
        var oldKeys = CatalogReleaseIndexWarmup.CollectTargets(job.Rows, _ => "").Select(t => CatalogPlatformIndex.Key(t.RepositorySource, t.Repository, t.PreferredVersion)).ToHashSet();
        job.Rows = _model.AllRows.ToList();
        if (job.Enabled && CatalogReleaseIndexWarmup.CollectTargets(job.Rows, _ => "").Any(t => !oldKeys.Contains(CatalogPlatformIndex.Key(t.RepositorySource, t.Repository, t.PreferredVersion))))
            job.Pending = true;
        if (job.Enabled && !source.IsCommunityManaged && job.Progress?.Outcome == CatalogReleaseWarmupOutcome.Completed &&
            CatalogReleaseIndexWarmup.CollectTargets(job.Rows, game => game.GetReleaseApiToken(_settings.Current))
                .Any(t => !CatalogPlatformIndex.IsFresh(t.RepositorySource, t.Repository, t.PreferredVersion, t.GetApiToken())))
        {
            job.Attempted.Clear(); job.Pending = true; job.Due = default;
        }
        return job;
    }
    public void Start()
    {
        // Give shared metadata the first opportunity to cover newly published apps.
        // Cached cards are already visible while the workspace finishes this request.
        if (_workspace.IsLoading) return;
        var job = Remember();
        if (job == null) return;
        _model.SetPlatformCheck(job.Progress);
        _model.DeferPlatformDiscoveries();
        _refresh();
        StartWorker();
    }
    public void Restart()
    {
        var job = Remember();
        if (job == null) return;
        job.Enabled = true; job.Pending = true; job.Due = default; job.Retries.Remove(Context); job.Attempted.Clear(); job.Revision++;
        StartWorker();
    }
    public void RefreshAll()
    {
        var job = Remember();
        if (job == null) return;
        _ = _session.RunAsync(async () =>
        {
            await PublishedPlatformCache.RefreshAsync(_manager.HttpClient, job.Source.PlatformMetadataUrl, true, _lifetime.Token);
            await _dispatch(() =>
            {
                if (_disposed) return;
                RefreshReviewStatus(job);
                if (ReferenceEquals(job.Source, _workspace.ActiveSource)) { _model.DeferPlatformDiscoveries(); _refresh(); }
                if (!job.Source.IsCommunityManaged) job.Force = true;
                Restart();
            });
        });
    }
    private void CredentialsChanged(string provider)
    {
        foreach (var job in _jobs.Values.Where(j => j.Enabled && j.Rows.Any(r => (r.External ?? r.Local)?.EffectiveRepositorySource == provider)))
        {
            job.Context = Context; job.Pending = true; job.Due = default; job.Progress = null; job.Attempted.Clear(); job.Revision++;
        }
        if (_activeProvider == provider) _activeRequest?.Cancel();
        // Only the new context is resumed; the coordinator retains the old context's cooldown.
        StartWorker();
    }
    private void StartWorker()
    {
        if (_disposed || _session.IsClosed) return;
        if (_wake.CurrentCount == 0) _wake.Release();
        if (_work is { IsCompleted: false } || !_jobs.Values.Any(j => j.Enabled && j.Pending)) return;
        _work = _session.RunAsync(RunAsync);
    }
    private List<CatalogSyncRowItem> FetchRows(Job job) => job.Rows.Where(row =>
    {
        var game = row.External ?? row.Local;
        if (game == null || string.IsNullOrWhiteSpace(game.Repository)) return false;
        // Retain attempted targets for aggregate progress, but never automatically
        // recheck usable shared or credential-appropriate successes, even if stale.
        var token = game.GetReleaseApiToken(_settings.Current);
        return !job.Source.IsCommunityManaged ||
            job.Attempted.Contains(CatalogPlatformIndex.Key(game.EffectiveRepositorySource, game.Repository, game.PreferredVersion, token)) ||
            !CatalogPlatformIndex.TryGet(game.EffectiveRepositorySource, game.Repository, game.PreferredVersion, token, out _);
    }).ToList();
    private async Task RunAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var pending = _jobs.Values.Where(j => j.Enabled && j.Pending).ToList();
                if (pending.Count == 0) return;
                var job = pending.Where(j => j.Due <= _clock.GetUtcNow())
                    .OrderByDescending(j => ReferenceEquals(j.Source, _workspace.ActiveSource)).FirstOrDefault();
                if (job == null)
                {
                    using var wait = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                    var signal = _wake.WaitAsync(wait.Token);
                    var delay = Task.Delay(pending.Min(j => j.Due) - _clock.GetUtcNow(), _clock, wait.Token);
                    await Task.WhenAny(signal, delay);
                    await wait.CancelAsync();
                    continue;
                }
                job.Pending = false;
                var revision = job.Revision;
                var context = Context;
                var viewRevision = _viewRevision;
                var allRows = FetchRows(job);
                var github = _settings.Current.GitHubApiToken;
                var gitlab = _settings.Current.GitLabApiToken;
                string Token(GameInfo game) => game.EffectiveRepositorySource == "gitlab" ? gitlab : github;
                string Key(CatalogSyncRowItem row)
                {
                    var game = (row.External ?? row.Local)!;
                    return CatalogPlatformIndex.Key(game.EffectiveRepositorySource, game.Repository, game.PreferredVersion, Token(game));
                }
                // One target per quantum lets a newly viewed catalog take priority at the next request boundary.
                var next = allRows.FirstOrDefault(row => !job.Attempted.Contains(Key(row)));
                var rows = next == null ? [] : allRows.Where(row => Key(row) == Key(next)).ToList();
                var force = job.Force;
                var retries = job.Retries.GetValueOrDefault(context);
                bool Present() => !_disposed && viewRevision == _viewRevision && ReferenceEquals(job.Source, _workspace.ActiveSource) && _isActive();
                using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                _activeRequest = request;
                _activeProvider = (next?.External ?? next?.Local)?.EffectiveRepositorySource;
                try
                {
                    await CatalogReleaseIndexWarmup.WarmAsync(_manager.HttpClient, rows, Token, request.Token,
                        () => _dispatch(() =>
                        {
                            if (_disposed) return;
                            RefreshReviewStatus(job);
                            if (Present()) { _model.DeferPlatformDiscoveries(); _refresh(); }
                        }),
                        progress => _dispatch(() =>
                        {
                            if (revision != job.Revision || context != Context) return;
                            var aggregate = CatalogReleaseIndexWarmup.Snapshot(_manager.HttpClient, CatalogReleaseIndexWarmup.CollectTargets(allRows, Token),
                                running: progress.Outcome == CatalogReleaseWarmupOutcome.Running);
                            job.Progress = aggregate with { WillRetry = aggregate.Outcome == CatalogReleaseWarmupOutcome.RateLimited && retries < 2 && aggregate.Failure?.RetryAt != null };
                            if (Present()) _model.SetPlatformCheck(job.Progress);
                        }), force);
                }
                catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested)
                { job.Pending = true; continue; }
                finally { _activeRequest = null; _activeProvider = null; }
                if (revision != job.Revision || context != Context) { job.Pending = true; continue; }
                if (next != null) job.Attempted.Add(Key(next));
                if (job.Progress?.Failure is { } blocked && (blocked.IsRateLimited || blocked.StatusCode == System.Net.HttpStatusCode.Unauthorized))
                {
                    var blockedRows = allRows.Where(row => (row.External ?? row.Local)!.EffectiveRepositorySource == blocked.Provider && !job.Attempted.Contains(Key(row))).ToList();
                    CatalogReleaseIndexWarmup.MarkUnavailable(_manager.HttpClient, CatalogReleaseIndexWarmup.CollectTargets(blockedRows, Token), blocked);
                    foreach (var row in blockedRows) job.Attempted.Add(Key(row));
                }
                if (allRows.Any(row => !job.Attempted.Contains(Key(row)))) { job.Pending = true; continue; }
                job.Force = false;
                if (job.Progress is { Outcome: CatalogReleaseWarmupOutcome.RateLimited, Failure.RetryAt: { } retryAt } && retries < 2)
                {
                    job.Retries[context] = retries + 1;
                    job.Due = retryAt; job.Pending = true; job.Attempted.Clear();
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }
    /// <summary>Detach progress from the departing view; session work continues.</summary>
    public void Cancel() { _viewRevision++; _model.SetPlatformCheck(null); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _settings.CredentialsChanged -= CredentialsChanged;
        _workspace.PropertyChanged -= WorkspaceChanged;
        _lifetime.Cancel();
        // The launcher session drains RunAsync before disposing HTTP services.
    }
}
