using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.Services.Mods;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class ModsCatalogWorkspaceTests
{
    [Fact]
    public void Opening_another_game_clears_previous_mods_and_invalidates_old_actions()
    {
        var model = new ModsViewModel();
        var first = model.Open(Game("first"), false);
        model.Catalog.Add(Package("old"));
        model.SearchText = "old query";
        model.SourceFilterKey = "old source";
        model.Status = "old status";
        var next = Game("next");
        model.Open(next, true).Should().BeGreaterThan(first);
        model.Game.Should().BeSameAs(next);
        model.Catalog.Should().BeEmpty();
        model.SearchText.Should().BeEmpty();
        model.SourceFilterKey.Should().BeNull();
        model.Status.Should().BeEmpty();
        model.IncludeNsfw.Should().BeTrue();
        var version = model.OpenVersion;
        model.Close();
        model.OpenVersion.Should().BeGreaterThan(version);
        model.Game.Should().BeNull();
    }

    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new() { AppsPath = Path.Combine(Path.GetTempPath(), "quiver-mod-workspace", Guid.NewGuid().ToString("N")) };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
    private sealed class Provider : IModProvider
    {
        public Queue<Task<IReadOnlyList<ModPackage>>> Loads { get; } = [];
        public CancellationToken LastToken;
        public string Id => "fake";
        public string DisplayName => Id;
        public bool SupportsPagedListing => false;
        public bool SupportsRemoteSearch => false;
        public bool TryParseSource(string url, out ModSourceRef source) { source = new() { ProviderId = Id, SourceKey = url, SourceUrl = url, DisplayLabel = url }; return true; }
        public void ForceRefreshOnNextList() { }
        public Task<IReadOnlyList<ModPackage>> ListPackagesAsync(ModSourceRef source, ModListOptions? options = null, CancellationToken cancellationToken = default) { LastToken = cancellationToken; return Loads.Dequeue(); }
        public Task<ModPackagePage> ListPackagesPageAsync(ModSourceRef source, string? pageToken, int pageSize, ModListOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ModPackagePage> SearchPackagesPageAsync(ModSourceRef source, string query, string? pageToken, int pageSize, ModListOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> DownloadAsync(ModPackageVersion version, IProgress<double>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IReadOnlySet<string> GetArchiveMetadataFileNames() => new HashSet<string>();
        public Task<string?> GetReadmeAsync(ModPackage package, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> GetChangelogAsync(ModPackage package, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }
    private static ModPackage Package(string id) => new() { ProviderId = "fake", SourceKey = "source", Id = id, Name = id, Owner = "owner", FullName = id };
    private static GameInfo Game(string id) => new() { Name = id, Repository = "owner/" + id, ModsPath = "mods", ModsSources = [new() { Provider = "fake", SourceUrl = "source" }] };

    [Fact]
    public async Task Replaced_game_cancels_load_and_rejects_its_late_rows()
    {
        var store = new Store();
        using var manager = new GameManager(store);
        await using var session = new LauncherSession();
        var provider = new Provider();
        var old = new TaskCompletionSource<IReadOnlyList<ModPackage>>();
        provider.Loads.Enqueue(old.Task);
        provider.Loads.Enqueue(Task.FromResult<IReadOnlyList<ModPackage>>([Package("new")]));
        var model = new ModsViewModel { Game = Game("first") };
        var workspace = new ModsCatalogWorkspace(manager, session, model, action => { action(); return Task.CompletedTask; }, new(new([provider])));
        var first = workspace.RefreshAsync(false);
        var cancelled = provider.LastToken;
        workspace.Cancel();
        model.Game = Game("second");
        await workspace.RefreshAsync(false);
        cancelled.IsCancellationRequested.Should().BeTrue();
        old.SetResult([Package("old")]);
        await first;
        model.Rows.Should().ContainSingle(row => row.Package.Id == "new");
        model.ListIsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task Closing_session_drains_provider_before_disposal_without_publishing_rows()
    {
        using var manager = new GameManager(new Store());
        var session = new LauncherSession();
        var provider = new Provider();
        var pending = new TaskCompletionSource<IReadOnlyList<ModPackage>>();
        provider.Loads.Enqueue(pending.Task);
        var model = new ModsViewModel { Game = Game("app") };
        var workspace = new ModsCatalogWorkspace(manager, session, model, action => { action(); return Task.CompletedTask; }, new(new([provider])));
        var load = workspace.RefreshAsync(false);
        workspace.Cancel();
        var shutdown = session.DisposeAsync().AsTask();
        shutdown.IsCompleted.Should().BeFalse();
        pending.SetResult([Package("late")]);
        await load;
        await shutdown;
        model.Rows.Should().BeEmpty();
        model.ListIsLoading.Should().BeFalse();
    }
}
