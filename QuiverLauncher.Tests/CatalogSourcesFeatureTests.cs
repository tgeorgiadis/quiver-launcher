using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class CatalogSourcesFeatureTests
{
    [AvaloniaTheory]
    [InlineData(0, "Browse apps")]
    [InlineData(3, "Review apps")]
    public async Task Primary_action_keeps_catalog_navigation_and_gamepad_action_order(int pendingCount, string label)
    {
        var store = new Store();
        store.Current.AppCatalogSources.Add(new() { Id = "source", Name = "Example", PendingReviewCount = pendingCount });
        var model = Create(store, new Sources());
        await using var session = new LauncherSession();
        var opened = new TaskCompletionSource<string>();
        var view = new CatalogSourcesView();
        view.Configure(model, session, new Host(), () => true, id => { opened.TrySetResult(id); return Task.CompletedTask; });
        var window = new Window { Content = view, Width = 800, Height = 500 };
        try
        {
            await model.RefreshAsync(CancellationToken.None);
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var controls = view.Navigation.CollectCatalogSourceCardActionControls(model.Sources.Single());
            controls.Should().HaveCount(3);
            controls[0].Should().BeOfType<CheckBox>();
            CatalogSourcesNavigation.GetDefaultCatalogSourceCardActionIndex(controls).Should().Be(1);
            var primary = (Button)controls[1];
            primary.Content.Should().Be(label);
            GamepadControlActivation.ActivateButton(primary);
            (await opened.Task.WaitAsync(TimeSpan.FromSeconds(3))).Should().Be("source");
        }
        finally { window.Close(); }
    }

    private sealed class Host : IFeatureNavigationHost
    {
        public GamepadNavigationService Navigation { get; } = new();
        public GamepadNavigationZone MainContentZone => GamepadNavigationZone.CatalogSources;
        public bool IsFocusActive => false;
        public bool ApplyTransition(GamepadZoneTransition transition) => false;
        public void ClearFocus() { }
        public void ClearSidebarFocus() { }
        public void FocusCard(bool stealFocus) { }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Realized_source_checkboxes_preserve_saved_state_and_persist_user_toggles(bool delayAvailability)
    {
        var store = new Store();
        store.Current.AppCatalogSources.Add(new() { Id = "on", Name = "Enabled source", Enabled = true });
        store.Current.AppCatalogSources.Add(new() { Id = "off", Name = "Disabled source", Enabled = false });
        var service = new Sources { PendingAvailability = delayAvailability ? new() : null };
        var model = Create(store, service);
        model.SourceListFilter = CatalogSourceListFilter.All;
        await using var session = new LauncherSession();
        var view = new CatalogSourcesView();
        view.Configure(model, session, new Host(), () => true, _ => Task.CompletedTask);
        var window = new Window { Content = view, Width = 1000, Height = 700 };
        CheckBox Checkbox(string id) => view.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Tag as string == id);
        try
        {
            window.Show();
            await model.RefreshAsync(CancellationToken.None);
            Dispatcher.UIThread.RunJobs();
            Checkbox("on").IsChecked.Should().BeTrue();
            Checkbox("off").IsChecked.Should().BeFalse();
            store.Saves.Should().Be(0, "realizing controls must not change settings");
            GamepadControlActivation.ActivateCheckBox(Checkbox("off"));
            Dispatcher.UIThread.RunJobs();
            store.Current.AppCatalogSources.Single(s => s.Id == "off").Enabled.Should().BeTrue();
            Checkbox("off").IsChecked.Should().BeTrue();
            store.Saves.Should().Be(1);
            GamepadControlActivation.ActivateCheckBox(Checkbox("on"));
            Dispatcher.UIThread.RunJobs();
            store.Current.AppCatalogSources.Single(s => s.Id == "on").Enabled.Should().BeFalse();
            Checkbox("on").IsChecked.Should().BeFalse();
            store.Saves.Should().Be(2);
            // Reverse a change even while the first availability request is pending.
            GamepadControlActivation.ActivateCheckBox(Checkbox("off"));
            Dispatcher.UIThread.RunJobs();
            store.Current.AppCatalogSources.Single(s => s.Id == "off").Enabled.Should().BeFalse();
            store.Saves.Should().Be(3);
            service.PendingAvailability?.TrySetResult();
            await model.RefreshAsync(CancellationToken.None);
            Dispatcher.UIThread.RunJobs();
            Checkbox("off").IsChecked.Should().BeFalse();
            Checkbox("on").IsChecked.Should().BeFalse();
            store.Saves.Should().Be(3);
        }
        finally { service.PendingAvailability?.TrySetResult(); window.Close(); }
    }

    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; private set; } = new() { FirstStartup = false };
        public int Saves;
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { Current = settings; Saves++; }
    }
    private sealed class Sources : ICatalogSourcesService
    {
        public TaskCompletionSource? PendingUsage;
        public TaskCompletionSource? PendingRefresh;
        public TaskCompletionSource? PendingAvailability;
        public int RefreshCount;
        public int AvailabilityCount;
        public Func<AppSettings, Task>? Usage;
        public Task RefreshUsageAsync(AppSettings settings) => Usage?.Invoke(settings) ?? PendingUsage?.Task ?? Task.CompletedTask;
        public Task<(List<GameInfo> Apps, string? Version, string? Error)> LoadAsync(string location) => Task.FromResult<(List<GameInfo>, string?, string?)>(([], null, null));
        public Task RegisterAsync(AppCatalogSource source) => Task.CompletedTask;
        public void DeleteCache(string id) { }
        public Task RefreshAvailabilityAsync(AppCatalogSource source) { AvailabilityCount++; return PendingAvailability?.Task ?? Task.CompletedTask; }
        public Task RefreshAllAsync(AppSettings settings) { RefreshCount++; return PendingRefresh?.Task ?? Task.CompletedTask; }
    }
    private static CatalogViewModel Create(Store store, Sources service, List<string>? calls = null)
    {
        var model = new CatalogViewModel();
        model.Configure(new SettingsViewModel(store), service, (message, title, question) => { calls?.Add(title); return Task.FromResult(true); },
            () => { calls?.Add("library"); return Task.CompletedTask; },
            () => { calls?.Add("badges"); return Task.CompletedTask; },
            () => { calls?.Add("prompt"); return Task.CompletedTask; });
        return model;
    }

    [Fact]
    public async Task Closed_load_does_not_publish_late_rows()
    {
        var store = new Store();
        store.Current.AppCatalogSources.Add(new() { Id = "source", Enabled = true });
        var service = new Sources { PendingUsage = new() };
        var model = Create(store, service);
        var changes = 0;
        model.ListChanged += () => changes++;
        using var cancellation = new CancellationTokenSource();
        var pending = model.RefreshAsync(cancellation.Token);
        var cachedRows = model.Sources.ToArray();
        cachedRows.Should().ContainSingle("cached summaries should display before the refresh completes");
        var initialChanges = changes;
        cancellation.Cancel();
        service.PendingUsage.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        model.Sources.Should().Equal(cachedRows);
        changes.Should().Be(initialChanges, "cancelled refreshes must not publish late rows");
    }

    [Fact]
    public async Task Filter_changed_during_load_uses_latest_filter()
    {
        var store = new Store();
        store.Current.AppCatalogSources.Add(new() { Id = "enabled", Enabled = true });
        store.Current.AppCatalogSources.Add(new() { Id = "disabled", Enabled = false });
        var service = new Sources { PendingUsage = new() };
        var model = Create(store, service);
        var pending = model.RefreshAsync(CancellationToken.None);
        model.SourceListFilter = CatalogSourceListFilter.Disabled;
        await model.RefreshAsync(CancellationToken.None);
        service.PendingUsage.SetResult();
        await pending;
        model.Sources.Should().ContainSingle().Which.SourceId.Should().Be("disabled");
    }

    [Fact]
    public async Task Settings_reloaded_during_usage_refresh_recomputes_current_sources()
    {
        var store = new Store();
        store.Current.AppCatalogSources = [new() { Id = "old", Enabled = true }];
        var gate = new TaskCompletionSource();
        var attempts = 0;
        var service = new Sources { Usage = async settings =>
        {
            if (++attempts == 1) await gate.Task;
            foreach (var source in settings.AppCatalogSources)
            {
                source.ListAppCount = 62;
                source.LibraryAppCount = 12;
                source.PendingReviewCount = 50;
            }
        }};
        var model = Create(store, service);
        var refresh = model.RefreshAsync(CancellationToken.None);
        store.Save(new AppSettings { AppCatalogSources = [new() { Id = "new", Enabled = true }] });
        gate.SetResult();
        await refresh;

        attempts.Should().Be(2);
        model.Sources.Should().ContainSingle().Which.SourceId.Should().Be("new");
        model.Sources[0].UsageStatsShort.Should().Be("12 of 62 apps in your library");
        model.PendingReviewCount.Should().Be(50);
    }

    [Fact]
    public async Task Refresh_requested_during_load_reruns_and_persists_completed_review()
    {
        var store = new Store();
        store.Current.AppCatalogSources = [new() { Id = "source", Enabled = true, CachedListVersion = "1", UpdateAvailable = true }];
        var gate = new TaskCompletionSource();
        var attempts = 0;
        var service = new Sources { Usage = async settings =>
        {
            if (++attempts == 1) await gate.Task;
            var source = settings.AppCatalogSources.Single();
            source.ListAppCount = 62;
            source.LibraryAppCount = 62;
            source.UpdateAvailable = false;
            source.AcknowledgedListVersion = "1";
        }};
        var model = Create(store, service);
        var refresh = model.RefreshAsync(CancellationToken.None);
        await model.RefreshAsync(CancellationToken.None);
        gate.SetResult();
        await refresh;

        attempts.Should().Be(2);
        store.Saves.Should().Be(1);
        model.Sources.Single().IsAllReviewed.Should().BeTrue();
    }

    [Fact]
    public async Task Concurrent_refresh_is_guarded_and_failure_allows_retry()
    {
        var store = new Store();
        var service = new Sources { PendingRefresh = new() };
        var calls = new List<string>();
        var model = Create(store, service, calls);
        var first = model.RefreshAllAsync(CancellationToken.None);
        await model.RefreshAllAsync(CancellationToken.None);
        service.RefreshCount.Should().Be(1);
        service.PendingRefresh.SetException(new IOException("offline"));
        await first;
        model.IsRefreshing.Should().BeFalse();
        calls.Should().Equal("Refresh Error");
        service.PendingRefresh = null;
        await model.RefreshAllAsync(CancellationToken.None);
        service.RefreshCount.Should().Be(2);
        calls.Should().Contain("badges");
    }

    [AvaloniaFact]
    public async Task Typed_bindings_show_rows_and_initial_enabled_value_does_not_persist()
    {
        var store = new Store();
        store.Current.AppCatalogSources.Add(new() { Id = "source", Name = "Example", Enabled = true });
        var service = new Sources();
        var model = Create(store, service);
        var view = new CatalogSourcesView { DataContext = model };
        await model.RefreshAsync(CancellationToken.None);
        Dispatcher.UIThread.RunJobs();
        view.FindControl<ItemsControl>("CatalogSourcesItemsControl")!.ItemCount.Should().Be(1);
        await model.SetEnabledAsync("source", true, CancellationToken.None);
        store.Saves.Should().Be(0);
        service.AvailabilityCount.Should().Be(0);
        await model.SetEnabledAsync("source", false, CancellationToken.None);
        store.Saves.Should().Be(1);
        service.AvailabilityCount.Should().Be(1);
        model.Sources.Should().BeEmpty();
    }
}
