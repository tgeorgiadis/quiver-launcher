using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.ViewModels;

namespace QuiverLauncher.Tests;

public class ExistingInstallationRecoveryTests
{
    private sealed class Store : ISettingsStore
    {
        public AppSettings Current { get; } = new();
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }

    [AvaloniaTheory]
    [InlineData(false, "installed")]
    [InlineData(true, "installed")]
    [InlineData(false, "no-version")]
    [InlineData(true, "no-version")]
    [InlineData(false, "empty")]
    [InlineData(true, "empty")]
    [InlineData(false, "incomplete")]
    [InlineData(true, "incomplete")]
    public async Task Readding_to_empty_library_reuses_complete_installations_without_downloading(bool bulk, string fixture)
    {
        var previous = QuiverLauncherPaths.OverrideUserDataRoot;
        var root = Path.Combine(Path.GetTempPath(), "quiver-readd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        QuiverLauncherPaths.OverrideUserDataRoot = root;
        try
        {
            var store = new Store();
            store.Current.AppsPath = Path.Combine(root, "Apps");
            var handler = new CountingNoNetwork();
            using var http = new HttpClient(handler);
            using var manager = new GameManager(store, http);
            manager.UiThreadInvoker = action => Dispatcher.UIThread.InvokeAsync(action).GetTask();
            var service = new CatalogReviewService(manager, new SettingsViewModel(store), () => { });
            var external = new GameInfo { Name = "Recovery fixture", Repository = "recovery-fixture/app", FolderName = "RecoveryApp" };
            var install = Path.Combine(manager.GamesFolder, external.FolderName);
            Directory.CreateDirectory(install);
            if (fixture != "empty")
            {
                if (OperatingSystem.IsMacOS()) Directory.CreateDirectory(Path.Combine(install, "Game.app"));
                else File.WriteAllText(Path.Combine(install, "Game.exe"), "existing app payload");
                File.WriteAllText(Path.Combine(install, "save.dat"), "existing user progress");
            }
            if (fixture != "no-version") File.WriteAllText(Path.Combine(install, "version.txt"), "v1.2.3");
            if (fixture == "incomplete")
                File.WriteAllText(Path.Combine(install, QuiverLauncher.Core.Services.GameInstallationService.IncompleteInstallFileName), "unfinished");
            var originalFiles = Directory.GetFiles(install).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
            await manager.CatalogService.SaveLocalAppsAsync([]);
            var source = new AppCatalogSource { Id = "recovery" };
            if (bulk)
                await service.SaveLocalAsync([external], source, TestContext.Current.CancellationToken);
            else
            {
                var commit = await service.CommitAddAsync(source, external, false);
                commit.Outcome.Should().Be(CatalogAddOutcome.Added);
                await service.PresentAddedAsync(commit.App!);
            }
            var added = manager.Games.Should().ContainSingle().Subject;
            var complete = fixture is "installed" or "no-version";
            added.CanDownload.Should().Be(!complete);
            added.CanLaunch.Should().Be(complete);
            if (complete) added.InstalledVersion.Should().Be(fixture == "installed" ? "v1.2.3" : "0.0.0");
            added.GetInstallPath(manager.GamesFolder).Should().Be(install);
            foreach (var (name, bytes) in originalFiles)
                File.ReadAllBytes(Path.Combine(install, name)).Should().Equal(bytes);
            (await manager.CatalogService.LoadLocalAppsAsync()).Should().ContainSingle();
            handler.Requests.Should().Be(0, "recovering an existing install must not download anything");
        }
        finally
        {
            QuiverLauncherPaths.OverrideUserDataRoot = previous;
            Directory.Delete(root, true);
        }
    }

    private sealed class CountingNoNetwork : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            throw new InvalidOperationException("Unexpected network request while recovering an installation.");
        }
    }
}
