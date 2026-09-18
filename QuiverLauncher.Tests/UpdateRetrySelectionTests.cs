using System.Net;
using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class UpdateRetrySelectionTests
{
    private sealed class Store(string path) : ISettingsStore
    {
        public AppSettings Current { get; } = new() { AppsPath = path };
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
    }

    private sealed class Handler : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"tag_name":"v2","assets":[{"name":"app-windows.zip","browser_download_url":"https://example.com/app.zip"}]}"""),
            });
        }
    }

    private sealed class Progress : IProgress<AppCheckProgress>
    {
        public List<AppCheckProgress> Updates { get; } = [];
        public void Report(AppCheckProgress value) => Updates.Add(value);
    }

    [Fact]
    public async Task Retry_selection_filters_before_local_status_scan_and_network_requests()
    {
        var root = Path.Combine(Path.GetTempPath(), "quiver-retry-" + Guid.NewGuid().ToString("N"));
        var handler = new Handler();
        using var client = new HttpClient(handler);
        using var manager = new GameManager(new Store(root), client);
        var requested = new GameInfo { Name = "Failed app", FolderName = "failed", Repository = "retry-" + Guid.NewGuid().ToString("N") + "/failed", Status = GameStatus.Installed };
        var successful = new GameInfo { Name = "Successful app", FolderName = "absent", Repository = "example/successful", Status = GameStatus.Installed, LatestVersion = "v1" };
        manager.Games.Add(requested);
        manager.Games.Add(successful);
        var folder = requested.GetInstallPath(manager.GamesFolder);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "version.txt"), "v1");
        File.WriteAllBytes(Path.Combine(folder, "app.exe"), [0x4D, 0x5A, 0x90, 0]);
        try
        {
            var progress = new Progress();
            var result = await manager.CheckInstalledUpdatesAsync(true, progress, TestContext.Current.CancellationToken,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { requested.InstanceKey, "removed-app" });
            result.Apps.Should().ContainSingle().Which.IdentityKey.Should().Be(requested.InstanceKey);
            result.Complete.Should().BeTrue();
            handler.Paths.Should().NotBeEmpty().And.OnlyContain(path => path.Contains(requested.Repository));
            progress.Updates.Should().OnlyContain(p => p.Total == 1);
            progress.Updates.Last().Completed.Should().Be(1);
            successful.Status.Should().Be(GameStatus.Installed, "successful apps must not even have their local status rescanned");
            successful.LatestVersion.Should().Be("v1");
        }
        finally { Directory.Delete(root, true); }
    }
}
