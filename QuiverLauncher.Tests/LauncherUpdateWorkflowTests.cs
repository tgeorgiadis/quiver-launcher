using FluentAssertions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class LauncherUpdateWorkflowTests
{
    private sealed class ReviewPresentation : IUpdatePresentation
    {
        public bool CanPresentResults => true;
        public bool CanShowFailureSummary => false;
        public int ReviewsOpened;
        public void OpenAppUpdatesReview() => ReviewsOpened++;
        public void OpenCatalogSources() => throw new InvalidOperationException();
        public Task OpenCatalogReviewAsync() => throw new InvalidOperationException();
        public void UpdateStatusChanged() { }
    }

    [AvaloniaTheory]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, true, true)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, false, false)]
    public async Task Explicit_checks_show_available_apps_independently_of_automatic_prompt_setting(
        bool manual, bool automaticPrompts, bool pending, bool expectPrompt)
    {
        var store = new Store();
        store.Current.PromptAppUpdateReviews = automaticPrompts;
        using var manager = new GameManager(store);
        using var library = new LibraryViewModel(manager, new SettingsViewModel(store));
        var session = new LauncherSession();
        var overlay = new MessagePromptView(); overlay.Configure(session);
        using var dialogs = new LauncherDialogLifetime(session.Token, action => action());
        var presentation = new ReviewPresentation();
        var window = new Window { Content = overlay, Width = 700, Height = 500 };
        var workflow = new LauncherUpdateWorkflow(manager, library.Settings, library, new ShellViewModel(), session,
            new LauncherPromptService(session, dialogs, overlay), presentation, () => null, new VelopackUpdateService(),
            () => Task.CompletedTask, _ => Task.FromResult(false));
        if (pending) manager.Games.Add(new GameInfo { Name = "Manual candidate", Repository = "fixture/app", Status = GameStatus.UpdateAvailable });
        manager.Games.Add(new GameInfo { Name = "Automatic candidate", Repository = "fixture/auto", AutoUpdate = true, Status = GameStatus.UpdateAvailable });
        try
        {
            window.Show();
            var showing = ((IUpdateCheckWorkflow)workflow).PresentCheckResultAsync(new() { CheckSucceeded = true }, 0, manual);
            overlay.Model.IsOpen.Should().Be(expectPrompt);
            if (expectPrompt)
            {
                overlay.Model.Body.Should().Contain("Manual candidate").And.NotContain("Automatic candidate");
                overlay.Model.Complete(MessagePromptResult.Yes);
            }
            await showing.WaitAsync(TimeSpan.FromSeconds(3));
            presentation.ReviewsOpened.Should().Be(expectPrompt ? 1 : 0);
            store.Current.PromptAppUpdateReviews.Should().Be(automaticPrompts);
        }
        finally { await session.DisposeAsync(); window.Close(); }
    }

    private sealed class BlockedCatalog : HttpMessageHandler
    {
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Cancelled;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { Cancelled = true; throw; }
            throw new InvalidOperationException();
        }
    }

    [Fact]
    public async Task Foreground_finishes_while_catalog_is_blocked_and_shutdown_cancels_secondary_work()
    {
        var store = new Store();
        var network = new BlockedCatalog();
        using var http = new HttpClient(network);
        using var manager = new GameManager(store, http);
        using var library = new LibraryViewModel(manager, new SettingsViewModel(store));
        var session = new LauncherSession();
        var shell = new ShellViewModel();
        var workflow = new LauncherUpdateWorkflow(manager, library.Settings, library, shell, session, null!, new Presentation(),
            () => null, new VelopackUpdateService(), () => Task.CompletedTask, _ => Task.FromResult(false));
        var coordinator = new UpdateCheckCoordinator(workflow);
        await coordinator.CheckAsync(false, false, session.Token).WaitAsync(TimeSpan.FromSeconds(2));
        await network.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        coordinator.IsChecking.Should().BeFalse();
        shell.LastUpdateCheckTime.Should().NotBeNull();
        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        network.Cancelled.Should().BeTrue();
    }

    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new() { AppsPath = Path.Combine(Path.GetTempPath(), "quiver-workflow-tests", Guid.NewGuid().ToString("N")) };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
    private sealed class Presentation : IUpdatePresentation
    {
        public bool CanPresentResults => false;
        public bool CanShowFailureSummary => false;
        public int StatusChanges { get; private set; }
        public void OpenAppUpdatesReview() => throw new Exception("Unexpected review");
        public void OpenCatalogSources() => throw new Exception("Unexpected catalog");
        public Task OpenCatalogReviewAsync() => throw new Exception("Unexpected catalog review");
        public void UpdateStatusChanged() => StatusChanges++;
    }
    [Fact]
    public async Task Overlapping_automatic_requests_share_work_and_leave_manual_apps_pending()
    {
        var store = new Store();
        using var manager = new GameManager(store);
        using var library = new LibraryViewModel(manager, new SettingsViewModel(store));
        var session = new LauncherSession();
        var automatic = new GameInfo { Name = "Automatic", Repository = "owner/auto", AutoUpdate = true, Status = GameStatus.UpdateAvailable };
        var manual = new GameInfo { Name = "Manual", Repository = "owner/manual", Status = GameStatus.UpdateAvailable };
        manager.Games.Add(automatic); manager.Games.Add(manual);
        var installation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var installed = new List<GameInfo>();
        var workflow = new LauncherUpdateWorkflow(manager, library.Settings, library, new ShellViewModel(), session, null!, new Presentation(),
            () => null, new VelopackUpdateService(), () => Task.CompletedTask, game => { installed.Add(game); return installation.Task; });
        var first = workflow.ApplyAutoUpdatesAsync(false);
        var second = workflow.ApplyAutoUpdatesAsync(true);
        second.Should().BeSameAs(first);
        installed.Should().Equal(automatic);
        installation.SetResult(true);
        (await first).Should().Be(1);
        (await second).Should().Be(1);
        workflow.GetPendingAppUpdates().Should().Contain(manual);
        await session.DisposeAsync();
    }
    [Fact]
    public async Task Failed_automatic_check_can_retry_and_late_completion_does_not_update_closed_shell()
    {
        var store = new Store();
        using var manager = new GameManager(store);
        using var library = new LibraryViewModel(manager, new SettingsViewModel(store));
        var session = new LauncherSession();
        manager.Games.Add(new GameInfo { Name = "Automatic", Repository = "owner/auto", AutoUpdate = true, Status = GameStatus.UpdateAvailable });
        var attempt = 0;
        var installation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var presentation = new Presentation();
        var workflow = new LauncherUpdateWorkflow(manager, library.Settings, library, new ShellViewModel(), session, null!, presentation,
            () => null, new VelopackUpdateService(), () => Task.CompletedTask, _ => ++attempt == 1 ? Task.FromException<bool>(new IOException("Offline")) : installation.Task);
        (await workflow.ApplyAutoUpdatesAsync(false)).Should().Be(0);
        var statusChanges = presentation.StatusChanges;
        var retry = session.RunAsync(() => workflow.ApplyAutoUpdatesAsync(false));
        attempt.Should().Be(2);
        var shutdown = session.DisposeAsync().AsTask();
        installation.SetResult(true);
        await retry; await shutdown;
        presentation.StatusChanges.Should().Be(statusChanges);
        (await workflow.ApplyAutoUpdatesAsync(false)).Should().Be(0);
        attempt.Should().Be(2);
    }
}
