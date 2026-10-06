using System.Net;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using QuiverLauncher.Views;

namespace QuiverLauncher.Tests;

public class InstalledUpdateCheckTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public List<string> Requests = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            // These apps are not in the quiverlauncher.com catalog.
            if (request.RequestUri!.Host == "api.quiverlauncher.com") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            Requests.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                """{"tag_name":"2.0","assets":[{"name":"app.zip","browser_download_url":"https://example.com/app.zip"}]}""") });
        }
    }

    [AvaloniaFact]
    public async Task Installed_hidden_apps_are_checked_without_reloading_library_or_fetching_catalogs_and_artwork()
    {
        var previous = QuiverLauncherPaths.OverrideUserDataRoot;
        var root = Path.Combine(Path.GetTempPath(), "quiver-installed-check", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); QuiverLauncherPaths.OverrideUserDataRoot = root;
        try
        {
            var store = new FileSettingsStore();
            store.Current.AppsPath = Path.Combine(root, "Apps");
            store.Save(store.Current);
            var handler = new Handler();
            using var client = new HttpClient(handler);
            using var manager = new GameManager(store, client);
            var source = new[]
            {
                new GameInfo { Name = "Visible", FolderName = "visible", Repository = "fixture/visible" },
                new GameInfo { Name = "Hidden", FolderName = "hidden", Repository = "fixture/hidden" },
                new GameInfo { Name = "Not installed", FolderName = "absent", Repository = "fixture/absent" },
                new GameInfo { Name = "Removed executable", FolderName = "blocked", Repository = "fixture/blocked" }
            };
            foreach (var game in source.Take(2))
            {
                Directory.CreateDirectory(game.GetInstallPath(store.Current.AppsPath));
                await File.WriteAllTextAsync(Path.Combine(game.GetInstallPath(store.Current.AppsPath), "version.txt"), "1.0", TestContext.Current.CancellationToken);
                await File.WriteAllTextAsync(Path.Combine(game.GetInstallPath(store.Current.AppsPath),
                    OperatingSystem.IsMacOS() ? "app" : "app.exe"), "#!/bin/sh\nexit 0\n", TestContext.Current.CancellationToken);
            }
            var blockedPath = source[3].GetInstallPath(store.Current.AppsPath);
            Directory.CreateDirectory(blockedPath);
            await File.WriteAllTextAsync(Path.Combine(blockedPath, "version.txt"), "1.0", TestContext.Current.CancellationToken);
            await manager.CatalogService.SaveLocalAppsAsync(source.ToList());
            await manager.ReloadLibraryFromDiskAsync(allowNetwork: false);
            var originals = manager.LibraryApps.ToArray();
            manager.LibrarySearchText = "Visible";
            manager.Games.Clear(); manager.Games.Add(originals.Single(a => a.Name == "Visible"));
            var visibleCollection = manager.Games;
            var result = await manager.CheckInstalledUpdatesAsync(true, null, TestContext.Current.CancellationToken);
            result.Successful.Should().Be(2);
            handler.Requests.Should().HaveCount(2).And.OnlyContain(url => url.EndsWith("/releases/latest"));
            manager.LibraryApps.Should().Equal(originals);
            manager.Games.Should().BeSameAs(visibleCollection).And.ContainSingle();
            originals.Single(a => a.Name == "Hidden").Status.Should().Be(GameStatus.UpdateAvailable);
            originals.Single(a => a.Name == "Not installed").LatestVersion.Should().BeNullOrEmpty();
            originals.Single(a => a.Name == "Removed executable").Status.Should().Be(GameStatus.NotInstalled);
            originals.Single(a => a.Name == "Removed executable").LatestVersion.Should().BeNullOrEmpty();
        }
        finally { QuiverLauncherPaths.OverrideUserDataRoot = previous; Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task Dismissing_incomplete_status_hides_strip_but_preserves_result_until_next_check()
    {
        var path = Path.Combine(Path.GetTempPath(), $"quiver-dismiss-status-{Guid.NewGuid():N}.json");
        var store = new FileSettingsStore(path);
        store.Current.FirstStartup = false;
        store.Current.EnableGamepadInput = false;
        var view = new MainView(new() { SettingsStore = store, InitializeOnOpen = false, EnableInput = false, EnableMusic = false });
        var window = new Window { Content = view, Width = 1280, Height = 720 };
        try
        {
            window.Show();
            view.Shell.IsCheckingUpdates = true;
            view.Shell.LastUpdateCheckTime = DateTime.Now;
            view.Shell.LastLauncherCheckNote = "Update check incomplete · Some checks did not finish";
            view.Shell.UpdateCheckStatus = view.Shell.LastLauncherCheckNote;
            view.Shell.IsCheckingUpdates = false;
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var strip = view.FindControl<UpdateCheckStatusView>("UpdateCheckStatus")!;
            var dismiss = strip.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "×"));
            dismiss.IsEffectivelyVisible.Should().BeTrue();
            dismiss.Focus();
            dismiss.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            view.Shell.ShowUpdateCheckStatus.Should().BeFalse();
            dismiss.IsEffectivelyVisible.Should().BeFalse();
            view.FindControl<Button>("CheckForUpdatesButton")!.IsFocused.Should().BeTrue();
            view.Shell.CheckForUpdatesToolTip.Should().Contain("Some checks did not finish");
            view.Shell.UpdatesUpToDateBadgeVisible.Should().BeFalse();
            view.Shell.NotifyUpdateCheckUiProperties();
            view.Shell.ShowUpdateCheckStatus.Should().BeFalse("ordinary refresh must not reopen a dismissed status");
            view.Shell.IsCheckingUpdates = true;
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            view.Shell.ShowUpdateCheckStatus.Should().BeTrue();
            dismiss.IsEffectivelyVisible.Should().BeFalse("active checks offer Cancel instead");
            view.Shell.DismissUpdateCheckStatus();
            view.Shell.ShowUpdateCheckStatus.Should().BeTrue();
            view.Shell.IsCheckingUpdates = false;
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            dismiss.IsEffectivelyVisible.Should().BeTrue("a new incomplete check can be dismissed again");
        }
        finally { window.Close(); await view.ShutdownAsync(); File.Delete(path); }
    }
}
