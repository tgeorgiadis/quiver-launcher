using System.Net;
using Avalonia;
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

public class LibraryLoadingTests
{
    [AvaloniaTheory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Large_library_prepares_all_cards_before_scrolling(bool grid, bool compact)
    {
        await using var fixture = new Fixture();
        fixture.View.SettingsModel.UseGridView = grid;
        fixture.View.SettingsModel.GridCompactCards = compact;
        await fixture.Manager.CatalogService.SaveLocalAppsAsync(Enumerable.Range(0, 150).Select(i => new GameInfo
            { Name = $"App {i:D3}", Repository = "fixture/app" + i, FolderName = "App" + i }).ToList());
        var window = new Window { Width = 1200, Height = 800, Content = fixture.View };
        window.Show(); window.UpdateLayout();
        var startup = fixture.View.InitializeGamesAsync();
        try
        {
            await fixture.Network.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(250, TestContext.Current.CancellationToken);
            window.UpdateLayout();
            fixture.View.Library.IsInitialLoading.Should().BeFalse();
            var items = fixture.Library.GetActiveGamesItemsControl()!;
            items.Items.Count.Should().Be(150);
            items.GetRealizedContainers().Count().Should().Be(150,
                "all cards are prepared up front so scrolling does not load cards on demand");
            var lastContainer = items.ContainerFromIndex(149);
            lastContainer.Should().NotBeNull();
            var last = fixture.Manager.Games.Last();
            fixture.Library.Navigation.ApplyLibraryGamepadSelection(149);
            await Task.Delay(250, TestContext.Current.CancellationToken);
            window.UpdateLayout();
            items.ContainerFromIndex(149).Should().BeSameAs(lastContainer);
            fixture.Library.FindGameCardRoot(last).Should().NotBeNull();
            last.IsGamepadFocused.Should().BeTrue();
            fixture.Library.FindGameOptionsButton(last).Should().NotBeNull();
            var scroll = fixture.Library.FindControl<ScrollViewer>("LibraryContentPanel")!;
            scroll.Offset.Y.Should().BeGreaterThan(0);
            var selectedCard = fixture.Library.FindGameCardRoot(last)!;
            var position = selectedCard.TranslatePoint(default, scroll)!.Value;
            position.Y.Should().BeLessThan(scroll.Bounds.Height);
            (position.Y + selectedCard.Bounds.Height).Should().BeGreaterThan(0);

            // A filter rebuild must discard recycled cards for removed apps.
            await fixture.View.Library.SearchAsync("App 000", CancellationToken.None, debounce: false);
            await Task.Delay(100, TestContext.Current.CancellationToken);
            window.UpdateLayout();
            items.Items.Count.Should().Be(1);
            items.ContainerFromIndex(0)!.DataContext.Should().BeSameAs(fixture.Manager.Games[0]);
            fixture.Library.FindGameCardRoot(last).Should().BeNull();
        }
        finally
        {
            window.Close();
            await fixture.View.ShutdownAsync(); await startup.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class BlockedNetwork : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new(HttpStatusCode.ServiceUnavailable);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string? _previous = QuiverLauncherPaths.OverrideUserDataRoot;
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "quiver-loading-" + Guid.NewGuid().ToString("N"));
        public BlockedNetwork Network { get; } = new();
        public HttpClient Client { get; }
        public FileSettingsStore Store { get; }
        public GameManager Manager { get; }
        public MainView View { get; }
        public LibraryView Library => View.FindControl<LibraryView>("LibraryPanel")!;
        public Fixture(bool initializeOnOpen = false)
        {
            Directory.CreateDirectory(Root); QuiverLauncherPaths.OverrideUserDataRoot = Root;
            Store = new FileSettingsStore();
            Store.Current.FirstStartup = false;
            Store.Current.AppsPath = Path.Combine(Root, "Apps");
            // Keep startup on the library instead of the first-run Browse page.
            Store.Current.LocalFirstCatalogMigrationComplete = true;
            Store.Save(Store.Current);
            Client = new HttpClient(Network);
            Manager = new GameManager(Store, Client);
            View = new MainView(new() { SettingsStore = Store, GameManager = Manager,
                InitializeOnOpen = initializeOnOpen, EnableInput = false, EnableMusic = false });
        }
        public async ValueTask DisposeAsync()
        {
            await View.ShutdownAsync(); Manager.Dispose(); Client.Dispose();
            QuiverLauncherPaths.OverrideUserDataRoot = _previous;
            Directory.Delete(Root, true);
        }
    }

    [AvaloniaFact]
    public async Task Normal_host_has_loading_state_before_it_opens()
    {
        await using var fixture = new Fixture(initializeOnOpen: true);
        fixture.View.Library.IsInitialLoading.Should().BeTrue();
        fixture.Library.FindControl<StackPanel>("LibraryLoadingPanel")!.IsVisible.Should().BeTrue();
        fixture.Library.FindControl<StackPanel>("EmptyLibraryPanel")!.IsVisible.Should().BeFalse();
    }

    [AvaloniaTheory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Delayed_local_load_shows_indicator_until_cards_are_published(bool grid, bool compact)
    {
        await using var fixture = new Fixture();
        fixture.View.SettingsModel.UseGridView = grid;
        fixture.View.SettingsModel.GridCompactCards = compact;
        await fixture.Manager.CatalogService.SaveLocalAppsAsync([new GameInfo
        { Name = "Saved app", Repository = "loading-fixture/" + Guid.NewGuid().ToString("N"), FolderName = "App" }]);
        using var fileLock = new FileStream(fixture.Manager.CatalogService.AppsConfigPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var startup = fixture.View.InitializeGamesAsync();
        var panel = fixture.Library.FindControl<StackPanel>("LibraryLoadingPanel")!;
        fixture.View.Library.IsInitialLoading.Should().BeTrue();
        panel.IsVisible.Should().BeTrue();
        panel.GetVisualDescendants().OfType<TextBlock>().Single().Text.Should().Be("Loading your library…");
        panel.GetVisualDescendants().OfType<LibraryLoadingBar>().Single().IsActive.Should().BeTrue();
        panel.GetVisualDescendants().OfType<Control>().Should().OnlyContain(control => !control.Focusable);
        fixture.Library.FindControl<StackPanel>("EmptyLibraryPanel")!.IsVisible.Should().BeFalse();
        fixture.Library.FindControl<StackPanel>("LibrarySearchNoMatchesPanel")!.IsVisible.Should().BeFalse();
        fixture.Library.UpdateEmptyState(false); panel.IsVisible.Should().BeFalse();
        fixture.Library.UpdateEmptyState(true); panel.IsVisible.Should().BeTrue();
        fileLock.Dispose();
        await fixture.Network.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Dispatcher.UIThread.RunJobs();
        fixture.Manager.Games.Should().ContainSingle();
        fixture.View.Library.IsInitialLoading.Should().BeFalse();
        panel.IsVisible.Should().BeFalse();
        fixture.Library.FindControl<ScrollViewer>("LibraryContentPanel")!.IsVisible.Should().BeTrue();
        fixture.View.Library.BeginInitialLoad();
        fixture.View.Library.IsInitialLoading.Should().BeFalse("later refreshes keep the displayed library usable");
        await fixture.View.ShutdownAsync(); await startup.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [AvaloniaFact]
    public async Task Empty_library_onboarding_only_appears_after_successful_load()
    {
        await using var fixture = new Fixture();
        using var fileLock = new FileStream(fixture.Manager.CatalogService.AppsConfigPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var startup = fixture.View.InitializeGamesAsync();
        fixture.View.Library.IsLibraryEmpty.Should().BeFalse();
        fileLock.Dispose();
        // An empty library makes no startup requests, so wait for startup itself.
        await startup.WaitAsync(TimeSpan.FromSeconds(5));
        Dispatcher.UIThread.RunJobs();
        fixture.View.Library.IsInitialLoading.Should().BeFalse();
        fixture.Library.FindControl<StackPanel>("EmptyLibraryPanel")!.IsVisible.Should().BeTrue();
        await fixture.View.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Failed_load_clears_indicator_without_empty_library_onboarding()
    {
        await using var fixture = new Fixture();
        await File.WriteAllTextAsync(fixture.Manager.CatalogService.AppsConfigPath, "{broken");
        var startup = fixture.View.InitializeGamesAsync();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (fixture.View.Library.IsInitialLoading && DateTime.UtcNow < deadline)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        fixture.View.Library.IsInitialLoading.Should().BeFalse();
        fixture.Library.FindControl<StackPanel>("LibraryLoadingPanel")!.IsVisible.Should().BeFalse();
        fixture.Library.FindControl<StackPanel>("EmptyLibraryPanel")!.IsVisible.Should().BeFalse();
        fixture.View.Library.IsLibraryEmpty.Should().BeFalse();
        await fixture.View.ShutdownAsync(); await startup.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [AvaloniaFact]
    public async Task Closing_during_local_load_clears_the_indicator()
    {
        await using var fixture = new Fixture();
        using var fileLock = new FileStream(fixture.Manager.CatalogService.AppsConfigPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var startup = fixture.View.InitializeGamesAsync();
        fixture.View.Library.IsInitialLoading.Should().BeTrue();
        var shutdown = fixture.View.ShutdownAsync();
        fixture.View.Library.IsInitialLoading.Should().BeFalse();
        fixture.Library.FindControl<StackPanel>("LibraryLoadingPanel")!.IsVisible.Should().BeFalse();
        fileLock.Dispose();
        await startup.WaitAsync(TimeSpan.FromSeconds(5)); await shutdown;
    }
}
