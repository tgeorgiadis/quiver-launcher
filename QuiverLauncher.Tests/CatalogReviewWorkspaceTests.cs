using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class CatalogReviewWorkspaceTests
{
    [Fact]
    public async Task Reveal_override_survives_refresh_but_resets_when_opening_a_review()
    {
        var store = new Store();
        store.Current.CatalogPlatformFilters = ["Windows"];
        var model = new CatalogSyncViewModel();
        using var workspace = new CatalogReviewWorkspace(model, new SettingsViewModel(store), new ReviewService(),
            (_, _, _) => Task.FromResult(true), () => Task.CompletedTask);
        await workspace.OpenAsync(new() { Id = "first" }, CatalogReviewFilter.NeedsReview, CancellationToken.None);
        model.RevealAllPendingReviews();
        await workspace.RefreshAsync(CancellationToken.None);
        model.EffectivePlatformFilters.Should().BeEmpty();
        store.Current.CatalogPlatformFilters.Should().Equal("Windows");
        await workspace.OpenAsync(new() { Id = "second" }, CatalogReviewFilter.NeedsReview, CancellationToken.None);
        model.EffectivePlatformFilters.Should().Equal("Windows");
    }
    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new();
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
    private sealed class ReviewService : ICatalogReviewService
    {
        public TaskCompletionSource? PendingFetch;
        public TaskCompletionSource<List<GameInfo>>? PendingLocal;
        public int Saves;
        public int Acknowledgements;
        public string? SavedSource;
        public Task<List<GameInfo>> LoadLocalAsync() => PendingLocal?.Task ?? Task.FromResult(new List<GameInfo>());
        public Task<List<GameInfo>> LoadCachedAsync(string id) => Task.FromResult(new List<GameInfo> { new() { Name = id, Repository = "owner/" + id, FolderName = id } });
        public Task FetchAsync(AppCatalogSource source) => PendingFetch?.Task ?? Task.CompletedTask;
        public Task SaveLocalAsync(List<GameInfo> apps, AppCatalogSource source, CancellationToken token) { Saves++; SavedSource = source.Id; return Task.CompletedTask; }
        public void RefreshAvailability(AppCatalogSource source, List<GameInfo> local, List<GameInfo> external) { }
        public void Acknowledge(AppCatalogSource source) { Acknowledgements++; }
    }

    private static (CatalogReviewWorkspace Workspace, CatalogSyncViewModel Model) Create(ReviewService service)
    {
        var model = new CatalogSyncViewModel();
        return (new(model, new SettingsViewModel(new Store()), service, (_, _, _) => Task.FromResult(true), () => Task.CompletedTask), model);
    }

    [Fact]
    public async Task New_source_wins_over_late_fetch_and_failure()
    {
        var service = new ReviewService { PendingFetch = new() };
        var (workspace, model) = Create(service);
        var old = workspace.OpenAsync(new() { Id = "old" }, CatalogReviewFilter.All, CancellationToken.None);
        var pending = service.PendingFetch;
        service.PendingFetch = null;
        await workspace.OpenAsync(new() { Id = "latest" }, CatalogReviewFilter.All, CancellationToken.None);
        pending.SetException(new IOException("old failed"));
        await old;
        workspace.ActiveSource!.Id.Should().Be("latest");
        model.Source!.Id.Should().Be("latest");
        workspace.Error.Should().BeNull();
        workspace.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task Closing_load_keeps_late_results_out_of_feature_state()
    {
        var service = new ReviewService { PendingLocal = new() };
        var (workspace, model) = Create(service);
        var pending = workspace.OpenAsync(new() { Id = "source" }, CatalogReviewFilter.All, CancellationToken.None);
        workspace.Close();
        service.PendingLocal.SetResult([]);
        await pending;
        workspace.ActiveSource.Should().BeNull();
        model.AllRows.Should().BeEmpty();
    }

    [Fact]
    public async Task Accepted_add_survives_navigation_and_other_mutations_wait()
    {
        var service = new ReviewService();
        var (workspace, model) = Create(service);
        await workspace.OpenAsync(new() { Id = "first" }, CatalogReviewFilter.All, CancellationToken.None);
        var row = model.AllRows.Single();
        service.PendingLocal = new();
        var mutation = workspace.ExecuteAsync(CatalogReviewAction.Add, row.IdentityKey, CancellationToken.None);
        var acknowledgement = workspace.ExecuteAsync(CatalogReviewAction.Acknowledge, null, CancellationToken.None);
        service.Acknowledgements.Should().Be(0);
        workspace.Close();
        service.PendingLocal.SetResult([]);
        await Task.WhenAll(mutation, acknowledgement);
        service.Saves.Should().Be(1);
        service.SavedSource.Should().Be("first");
        workspace.IsBusy.Should().BeFalse();
        service.PendingLocal = null;
        await workspace.OpenAsync(new() { Id = "second" }, CatalogReviewFilter.All, CancellationToken.None);
        await workspace.ExecuteAsync(CatalogReviewAction.Acknowledge, null, CancellationToken.None);
        service.Acknowledgements.Should().Be(1);
    }
}
