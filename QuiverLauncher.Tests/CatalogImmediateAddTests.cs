using System.Collections.Specialized;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class CatalogImmediateAddTests(ITestOutputHelper output)
{
    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new();
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
    private sealed class ControlledService : ICatalogReviewService
    {
        public List<GameInfo> Library = [];
        public List<GameInfo> External = [App(0), App(1), App(2)];
        public TaskCompletionSource Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool FailFirst, FailSecond, FailPreparation;
        public int Commits, CachedReads;
        public Task<List<GameInfo>> LoadLocalAsync() => Task.FromResult(Library.ToList());
        public Task<List<GameInfo>> LoadCachedAsync(string id) { CachedReads++; return Task.FromResult(External); }
        public Task FetchAsync(AppCatalogSource source) => Task.CompletedTask;
        public Task SaveLocalAsync(List<GameInfo> apps, AppCatalogSource source, CancellationToken token) { Library = apps; return Task.CompletedTask; }
        public void RefreshAvailability(AppCatalogSource source, List<GameInfo> local, List<GameInfo> external) { }
        public void Acknowledge(AppCatalogSource source) { }
        public async Task<CatalogAddCommit> CommitAddAsync(AppCatalogSource source, GameInfo external, bool autoUpdate)
        {
            await Gate.Task;
            ++Commits;
            if (Commits == 1 && FailFirst || Commits == 2 && FailSecond) throw new IOException("disk full");
            var result = CatalogReviewService.PlanAdd(Library, external, autoUpdate);
            Library = result.Library;
            return result;
        }
        public Task PresentAddedAsync(GameInfo app) => FailPreparation ? Task.FromException(new IOException("preparation denied")) : Task.CompletedTask;
    }
    private static GameInfo App(int i) => new() { Name = $"App {i:D3}", Repository = "synthetic/app" + i, FolderName = "App" + i };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Immediate_feedback_duplicate_guard_and_independent_rollback(bool fail)
    {
        var service = new ControlledService { FailFirst = fail };
        var model = new CatalogSyncViewModel();
        var messages = new List<string>();
        var reconciliations = 0;
        using var workspace = new CatalogReviewWorkspace(model, new(new Store()), service,
            (m, _, _) => { messages.Add(m); return Task.FromResult(true); }, () => { reconciliations++; return Task.CompletedTask; });
        await workspace.OpenAsync(new() { Id = "source", CachedListVersion = "1" }, CatalogReviewFilter.NotInLibrary, CancellationToken.None);
        var rows = model.AllRows.ToList();
        var reads = service.CachedReads;
        var first = workspace.ExecuteAsync(CatalogReviewAction.Add, rows[0].IdentityKey, CancellationToken.None);
        rows[0].ShowAddButton.Should().BeFalse();
        rows[0].IsAddPending.Should().BeTrue();
        rows[0].CanAdd.Should().BeFalse();
        rows[0].Local.Should().BeNull();
        model.NeedsReviewCount.Should().Be(3);
        model.GetFilteredRows().Should().Contain(rows[0]);
        await workspace.ExecuteAsync(CatalogReviewAction.Add, rows[0].IdentityKey, CancellationToken.None);
        var second = workspace.ExecuteAsync(CatalogReviewAction.Add, rows[1].IdentityKey, CancellationToken.None);
        rows[1].ShowAddButton.Should().BeFalse();
        service.Gate.SetResult();
        await Task.WhenAll(first, second);
        service.Commits.Should().Be(2);
        service.CachedReads.Should().Be(reads, "Add reuses loaded catalog definitions");
        service.Library.Should().HaveCount(fail ? 1 : 2);
        rows[0].CanAdd.Should().Be(fail);
        rows[0].ShowAddButton.Should().Be(fail);
        model.AllRows.Should().Equal(rows, "row identity must survive reconciliation");
        model.NeedsReviewCount.Should().Be(fail ? 2 : 1);
        reconciliations.Should().Be(1);
        messages.Count.Should().Be(fail ? 1 : 0);
    }

    [Fact]
    public async Task Last_failed_save_still_flushes_notifications_for_previous_success()
    {
        var service = new ControlledService { FailSecond = true };
        var model = new CatalogSyncViewModel();
        var refreshes = 0;
        using var workspace = new CatalogReviewWorkspace(model, new(new Store()), service,
            (_, _, _) => Task.FromResult(true), () => { refreshes++; return Task.CompletedTask; });
        await workspace.OpenAsync(new() { Id = "source" }, CatalogReviewFilter.All, CancellationToken.None);
        var first = workspace.ExecuteAsync(CatalogReviewAction.Add, model.AllRows[0].IdentityKey, CancellationToken.None);
        var second = workspace.ExecuteAsync(CatalogReviewAction.Add, model.AllRows[1].IdentityKey, CancellationToken.None);
        service.Gate.SetResult();
        await Task.WhenAll(first, second);
        service.Library.Should().ContainSingle().Which.Repository.Should().Be("synthetic/app0");
        model.AllRows[1].CanAdd.Should().BeTrue();
        refreshes.Should().Be(1);
    }

    [Fact]
    public async Task Accepted_writes_drain_on_shutdown_after_navigation_and_keep_preparation_failures_saved()
    {
        var service = new ControlledService { FailPreparation = true };
        var model = new CatalogSyncViewModel();
        var session = new LauncherSession();
        var disposed = false;
        using var workspace = new CatalogReviewWorkspace(model, new(new Store()), service,
            (_, _, _) => Task.FromResult(true), () => Task.CompletedTask, session.CatalogMutations);
        session.OnShutdown(() => disposed = true);
        var source = new AppCatalogSource { Id = "source" };
        await workspace.OpenAsync(source, CatalogReviewFilter.All, session.Token);
        var first = session.RunAsync(() => workspace.ExecuteAsync(CatalogReviewAction.Add, model.AllRows[0].IdentityKey, session.Token));
        var second = session.RunAsync(() => workspace.ExecuteAsync(CatalogReviewAction.Add, model.AllRows[1].IdentityKey, session.Token));
        workspace.Close();
        await workspace.OpenAsync(source, CatalogReviewFilter.All, session.Token);
        model.AllRows.Take(2).Should().OnlyContain(r => r.IsAddPending);
        var shutdown = session.DisposeAsync().AsTask();
        disposed.Should().BeFalse();
        service.Gate.SetResult();
        await Task.WhenAll(first, second, shutdown);
        service.Library.Should().HaveCount(2);
        disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Queued_folder_collision_does_not_overwrite_first_add()
    {
        var service = new ControlledService();
        service.External[1].FolderName = service.External[0].FolderName;
        var model = new CatalogSyncViewModel();
        var messages = new List<string>();
        using var workspace = new CatalogReviewWorkspace(model, new(new Store()), service,
            (m, _, _) => { messages.Add(m); return Task.FromResult(true); }, () => Task.CompletedTask);
        await workspace.OpenAsync(new() { Id = "source" }, CatalogReviewFilter.All, CancellationToken.None);
        var first = workspace.ExecuteAsync(CatalogReviewAction.Add, model.AllRows[0].IdentityKey, CancellationToken.None);
        var second = workspace.ExecuteAsync(CatalogReviewAction.Add, model.AllRows[1].IdentityKey, CancellationToken.None);
        service.Gate.SetResult();
        await Task.WhenAll(first, second);
        service.Library.Should().ContainSingle().Which.Repository.Should().Be("synthetic/app0");
        messages.Should().ContainSingle().Which.Should().Contain("Folder");
        model.AllRows[1].HasAddBlockedReason.Should().BeTrue();
    }

    [AvaloniaTheory]
    [InlineData(62, 100)]
    [InlineData(62, 500)]
    [InlineData(150, 100)]
    [InlineData(150, 500)]
    public async Task Populated_library_add_preserves_instances_artwork_and_rendered_focus(int catalogCount, int libraryCount)
    {
        var previousRoot = QuiverLauncherPaths.OverrideUserDataRoot;
        var root = Path.Combine(Path.GetTempPath(), "quiver-immediate-add", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        QuiverLauncherPaths.OverrideUserDataRoot = root;
        var window = new Window { Width = 1200, Height = 800 };
        try
        {
            var store = new Store();
            store.Current.AppsPath = Path.Combine(root, "Apps");
            using var http = new HttpClient(new NoNetwork());
            using var manager = new GameManager(store, http);
            manager.UiThreadInvoker = action => Dispatcher.UIThread.InvokeAsync(action).GetTask();
            var library = Enumerable.Range(1000, libraryCount).Select(App).ToList();
            var cache = Path.Combine(manager.CacheFolder, "Icons");
            Directory.CreateDirectory(cache);
            // A real decodable cached icon for every existing library entry.
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
            foreach (var app in library)
            {
                app.GameIconUrl = "https://example.invalid/" + app.FolderName + ".png";
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(app.GameIconUrl)))[..16].ToLowerInvariant();
                await File.WriteAllBytesAsync(Path.Combine(cache, $"{app.FolderName}_{hash}.png"), png);
            }
            await manager.CatalogService.SaveLocalAppsAsync(library);
            await manager.ReloadLibraryFromDiskAsync([], allowNetwork: false);
            var existing = manager.Games.ToList();
            existing.Should().HaveCount(libraryCount);
            existing.Should().OnlyContain(g => g.IconUrl.StartsWith(cache));
            var collection = manager.Games;
            var unrelatedChanges = new List<string>();
            foreach (var app in existing) app.PropertyChanged += (_, e) => unrelatedChanges.Add(e.PropertyName ?? "all");
            var changes = new List<NotifyCollectionChangedAction>();
            collection.CollectionChanged += (_, e) => changes.Add(e.Action);
            var source = new AppCatalogSource { Id = "test", Enabled = true, Location = Path.Combine(root, "catalog.json") };
            store.Current.AppCatalogSources = [source];
            await manager.CatalogService.ExportLocalAppsToFileAsync(source.Location, Enumerable.Range(0, catalogCount).Select(App).ToList());
            var view = new CatalogReviewView();
            var model = view.Model;
            var settings = new SettingsViewModel(store);
            var service = new MeasuredService(new CatalogReviewService(manager, settings, () => throw new Exception("Full library sorting during Add")));
            using var workspace = new CatalogReviewWorkspace(model, settings, service,
                (m, _, _) => throw new Exception(m), () => Task.CompletedTask);
            workspace.RowsChanged += () =>
            {
                foreach (var row in model.AllRows)
                    CatalogCompareService.ApplyReviewActionButtons(row, source, CatalogReviewFilter.All);
            };
            await workspace.OpenAsync(source, CatalogReviewFilter.All, CancellationToken.None);
            var rows = model.AllRows.ToList();
            model.Rows.UpdateWith(model.GetFilteredRows());
            var list = new ListBox
            {
                ItemsSource = model.Rows,
                ItemTemplate = (IDataTemplate)view.Resources["CatalogReviewGridCardTemplate"]!,
            };
            view.Content = list;
            window.Content = view;
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var card = list.ContainerFromIndex(0)!;
            var button = card.GetVisualDescendants().OfType<Button>().First(b => b.Content?.ToString() == "Add");
            button.Focus();
            var watch = Stopwatch.StartNew();
            var operation = workspace.ExecuteAsync(CatalogReviewAction.Add, rows[0].IdentityKey, CancellationToken.None);
            window.UpdateLayout();
            card.GetVisualDescendants().OfType<Button>().Should().NotContain(b =>
                b.IsEffectivelyVisible && (b.Content!.ToString() == "Add" || b.Content.ToString() == "Added"));
            button.IsFocused.Should().BeFalse();
            var feedback = watch.Elapsed.TotalMilliseconds;
            await operation;
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            card.GetVisualDescendants().OfType<Button>().Should().NotContain(b => b.IsEffectivelyVisible && b.Content!.ToString() == "Added");
            var details = card.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && b.Content?.ToString() == "Details");
            details.IsFocused.Should().BeTrue("focus moves to Details as soon as Add disappears");
            rows[0].ShowAddButton.Should().BeFalse();
            rows[0].GridCardDesktopChrome.Select(c => c.Kind).Should().Equal(
                CatalogReviewGridCardActions.ChromeKind.Details, CatalogReviewGridCardActions.ChromeKind.Remove);
            var total = watch.Elapsed.TotalMilliseconds;
            output.WriteLine($"catalog={catalogCount} library={libraryCount}: feedback={feedback:F1}ms, persistence={service.Persistence:F1}ms, library={service.Presentation:F1}ms, reconciliation={service.Reconciliation:F1}ms, total={total:F1}ms");
            feedback.Should().BeLessThan(100);
            // Loose CI guard; report the actual sub-500ms SSD target separately.
            total.Should().BeLessThan(2000);
            manager.Games.Should().BeSameAs(collection);
            manager.Games.Should().HaveCount(libraryCount + 1);
            manager.Games.Where(g => existing.Contains(g)).Should().Equal(existing);
            changes.Should().Equal(NotifyCollectionChangedAction.Add);
            unrelatedChanges.Should().BeEmpty();
            model.AllRows.Should().Equal(rows);
        }
        finally
        {
            window.Close();
            QuiverLauncherPaths.OverrideUserDataRoot = previousRoot;
            Directory.Delete(root, true);
        }
    }

    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new Exception("Network request during Add");
    }

    [Fact]
    public async Task Unreadable_library_is_not_treated_as_empty_before_a_mutation()
    {
        var root = Path.Combine(Path.GetTempPath(), "quiver-invalid-library", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var service = new AppCatalogService(dataDirectory: root);
            var path = Path.Combine(root, "apps.json");
            await File.WriteAllTextAsync(path, "{truncated library");
            var read = () => service.LoadLocalAppsForMutationAsync();
            await read.Should().ThrowAsync<System.Text.Json.JsonException>();
            (await File.ReadAllTextAsync(path)).Should().Be("{truncated library");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Atomic_replacement_failure_retains_original_library_and_allows_retry()
    {
        if (!OperatingSystem.IsWindows()) return; // Windows file sharing provides a deterministic replacement failure.
        var root = Path.Combine(Path.GetTempPath(), "quiver-atomic-add", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var service = new AppCatalogService(dataDirectory: root);
            await service.SaveLocalAppsAsync([App(0)]);
            var path = Path.Combine(root, "apps.json");
            var before = await File.ReadAllBytesAsync(path);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var save = async () => await service.SaveLocalAppsAsync([App(0), App(1)]);
                await save.Should().ThrowAsync<IOException>();
                (await File.ReadAllBytesAsync(path)).Should().Equal(before);
                Directory.GetFiles(root, "*.tmp").Should().BeEmpty();
            }
            await service.SaveLocalAppsAsync([App(0), App(2)]);
            (await service.LoadLocalAppsAsync()).Select(a => a.Repository).Should().Equal("synthetic/app0", "synthetic/app2");
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class MeasuredService(ICatalogReviewService inner) : ICatalogReviewService
    {
        public double Persistence, Presentation, Reconciliation;
        public Task<List<GameInfo>> LoadLocalAsync() => inner.LoadLocalAsync();
        public Task<List<GameInfo>> LoadCachedAsync(string id) => inner.LoadCachedAsync(id);
        public Task FetchAsync(AppCatalogSource source) => inner.FetchAsync(source);
        public Task SaveLocalAsync(List<GameInfo> apps, AppCatalogSource source, CancellationToken token) => throw new Exception("Full reload path during single Add");
        public void RefreshAvailability(AppCatalogSource source, List<GameInfo> local, List<GameInfo> external) => inner.RefreshAvailability(source, local, external);
        public void Acknowledge(AppCatalogSource source) => inner.Acknowledge(source);
        public async Task<CatalogAddCommit> CommitAddAsync(AppCatalogSource source, GameInfo external, bool autoUpdate)
        {
            var watch = Stopwatch.StartNew();
            var result = await inner.CommitAddAsync(source, external, autoUpdate);
            Persistence = watch.Elapsed.TotalMilliseconds;
            return result;
        }
        public async Task PresentAddedAsync(GameInfo app)
        {
            var watch = Stopwatch.StartNew(); await inner.PresentAddedAsync(app); Presentation = watch.Elapsed.TotalMilliseconds;
        }
        public async Task ReconcileAddedAsync(List<GameInfo> library, AppCatalogSource? source, IReadOnlyList<CatalogSyncRowItem> rows)
        {
            var watch = Stopwatch.StartNew(); await inner.ReconcileAddedAsync(library, source, rows); Reconciliation = watch.Elapsed.TotalMilliseconds;
        }
    }
}
