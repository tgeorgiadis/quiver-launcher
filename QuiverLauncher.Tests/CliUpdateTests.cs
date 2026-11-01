using System.IO.Compression;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class CliUpdateTests
{
    [Fact]
    public async Task Update_loads_files_for_version_only_catalog_evidence_and_keeps_the_installed_build()
    {
        var root = Path.Combine(Path.GetTempPath(), "QuiverCliUpdateTests", Guid.NewGuid().ToString("N"));
        var previousRoot = QuiverLauncherPaths.OverrideUserDataRoot;
        var previousStore = SettingsStoreProvider.Default;
        var previousOutput = Console.Out;
        using var output = new StringWriter();
        Directory.CreateDirectory(root);
        try
        {
            Console.SetOut(output);
            QuiverLauncherPaths.OverrideUserDataRoot = root;
            var gamesFolder = Path.Combine(root, "Apps");
            var store = new FileSettingsStore(Path.Combine(root, "settings.json"));
            store.Save(new AppSettings { Platform = TestPlatforms.ForWindowsPayload, AppsPath = gamesFolder });
            SettingsStoreProvider.Default = store;
            var repository = "cli-version-only/" + Guid.NewGuid().ToString("N");
            var target = new GitHubRelease
            {
                tag_name = "v2.0.0",
                assets = [
                    new() { name = "game-v2.0.0-win64.zip", browser_download_url = "https://example.com/installed-build.zip" },
                    new() { name = "game-v2.0.0-win32.zip", browser_download_url = "https://example.com/other-build.zip" },
                ],
            };
            var newerUnverified = new GitHubRelease
            {
                tag_name = "v3.0.0",
                assets = [new() { name = "game-v3.0.0-win64.zip", browser_download_url = "https://example.com/unverified.zip" }],
            };
            byte[] archive;
            using (var stream = new MemoryStream())
            {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    using var payload = zip.CreateEntry("game.exe").Open();
                    payload.Write([0x4D, 0x5A, 0x02]);
                }
                archive = stream.ToArray();
            }
            using var client = new HttpClient(new Handler(request =>
            {
                if (request.RequestUri!.Host == "api.github.com")
                {
                    var path = request.RequestUri.AbsolutePath;
                    var json = path.EndsWith("/latest") ? JsonSerializer.Serialize(newerUnverified)
                        : path.EndsWith("/tags/v2.0.0") ? JsonSerializer.Serialize(target)
                        : path.EndsWith("/releases") ? JsonSerializer.Serialize(new[] { newerUnverified, target })
                        : throw new InvalidOperationException($"Unexpected release endpoint: {request.RequestUri}");
                    return new(HttpStatusCode.OK) { Content = new StringContent(json) };
                }
                if (request.RequestUri.AbsoluteUri == "https://example.com/installed-build.zip")
                    return new(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) };
                throw new InvalidOperationException($"Must not download another build or release: {request.RequestUri}");
            }));
            using var manager = new GameManager(store, client, new AppCatalogService(dataDirectory: root));
            var game = new GameInfo
            {
                Name = "Catalog-only update",
                Repository = repository,
                FolderName = "Game",
                InstalledVersion = "v1.0.0",
                CatalogVerifiedVersion = target.tag_name,
                CatalogVerifiedAt = DateTimeOffset.UtcNow,
                Status = GameStatus.UpdateAvailable,
            };
            manager.Games.Add(game);
            var gamePath = game.GetInstallPath(gamesFolder);
            Directory.CreateDirectory(gamePath);
            await File.WriteAllTextAsync(Path.Combine(gamePath, "version.txt"), "v1.0.0", TestContext.Current.CancellationToken);
            await File.WriteAllBytesAsync(Path.Combine(gamePath, "game.exe"), [0x4D, 0x5A, 0x01], TestContext.Current.CancellationToken);
            await new InstalledBuild("game-v1.0.0-win64.zip", new string('0', 64)).WriteAsync(gamePath);
            GitHubApiCache.SetCache(null, repository, "v1.0.0", "", new GitHubRelease
            {
                tag_name = "v1.0.0",
                assets = [new() { name = "old-win64.zip", browser_download_url = "https://example.com/old.zip" }],
            }, persist: false);

            var exitCode = await new CLIHandler(manager).Execute(["--update"])
                .WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

            exitCode.Should().Be(0, "CLI output: {0}", output.ToString());
            File.ReadAllText(Path.Combine(gamePath, "version.txt")).Trim().Should().Be("v2.0.0");
            File.ReadAllBytes(Path.Combine(gamePath, "game.exe")).Should().Equal(0x4D, 0x5A, 0x02);
            InstalledBuild.Read(gamePath)!.FileName.Should().Be("game-v2.0.0-win64.zip");
            game.Status.Should().Be(GameStatus.Installed);
        }
        finally
        {
            Console.SetOut(previousOutput);
            SettingsStoreProvider.Default = previousStore;
            QuiverLauncherPaths.OverrideUserDataRoot = previousRoot;
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
