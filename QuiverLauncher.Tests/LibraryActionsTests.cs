using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class LibraryActionsTests
{
    private sealed class Store(string directory) : ISettingsStore
    {
        public AppSettings Current { get; } = new() { AppsPath = Path.Combine(directory, "Apps") };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }
    [Fact]
    public async Task Closing_session_while_remove_confirmation_is_pending_prevents_catalog_mutation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "quiver-action-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var store = new Store(directory);
        var catalog = new AppCatalogService(dataDirectory: directory);
        var game = new GameInfo { Name = "Example", Repository = "owner/example", FolderName = "Example", IsInLocalAppsJson = true };
        await catalog.SaveLocalAppsAsync([game]);
        using var manager = new GameManager(store, catalogService: catalog);
        using var library = new LibraryViewModel(manager, new SettingsViewModel(store));
        var session = new LauncherSession();
        var prompt = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var actions = new LibraryActions(manager, new LibraryPersistenceService(manager), library.Settings, session, library,
            () => throw new Exception("Unexpected picker"), (_, _, _, _) => { entered.SetResult(); return prompt.Task; },
            _ => throw new Exception("Unexpected URL"), () => throw new Exception("Unexpected catalog refresh"));
        var pending = actions.RemoveEntryAsync(game);
        await entered.Task;
        var shutdown = session.DisposeAsync().AsTask();
        shutdown.IsCompleted.Should().BeFalse();
        prompt.SetResult(true);
        await pending;
        await shutdown;
        (await catalog.LoadLocalAppsAsync()).Should().ContainSingle(g => g.Repository == "owner/example");
    }
    [Fact]
    public async Task Runner_prompt_respects_already_cancelled_host_lifetime()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var action = () => GameDialogService.ShowLinuxWindowsRunnerDialogAsync("unused", cancellationToken: cancellation.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
    }
}
