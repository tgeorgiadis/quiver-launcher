using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

/// <summary>Opt-in: creates a unique disposable app using an already-installed user runtime.</summary>
public sealed class FlatpakLinuxIntegrationTests
{
    [Fact]
    public async Task Real_bundle_install_launch_update_and_uninstall_preserve_data()
    {
        var runtime = Environment.GetEnvironmentVariable("QUIVER_FLATPAK_TEST_RUNTIME");
        Assert.SkipUnless(OperatingSystem.IsLinux() && !string.IsNullOrWhiteSpace(runtime),
            "Requires Linux, Flatpak/libflatpak, and QUIVER_FLATPAK_TEST_RUNTIME pointing to an installed user runtime. See docs/flatpak.md.");
        Assert.Matches(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_]+/[A-Za-z0-9_.-]+$", runtime!);
        var root = Path.Combine(Path.GetTempPath(), "quiver-flatpak-integration-" + Guid.NewGuid().ToString("N"));
        var id = "io.github.quiver.Test" + Guid.NewGuid().ToString("N");
        var runner = new FlatpakProcessRunner();
        var service = new FlatpakService(runner, new FlatpakBundleReader(), Path.Combine(root, "owners"));
        var gamePath = Path.Combine(root, "game");
        var build = Path.Combine(root, "build");
        var bin = Path.Combine(build, "files", "bin");
        string? dataPath = null;
        Directory.CreateDirectory(bin);
        Directory.CreateDirectory(Path.Combine(build, "export"));
        try
        {
            await service.CheckAvailableAsync(TestContext.Current.CancellationToken);
            var runtimeInfo = await runner.RunAsync(["info", "--user", "runtime/" + runtime], TestContext.Current.CancellationToken);
            runtimeInfo.ExitCode.Should().Be(0, "the integration runtime must already be installed: {0}", runtimeInfo.Error);
            File.WriteAllText(Path.Combine(build, "metadata"), $"[Application]\nname={id}\nruntime={runtime}\ncommand=quiver-test\n");

            async Task<string> Bundle(string version)
            {
                var executable = Path.Combine(bin, "quiver-test");
                File.WriteAllText(executable, "#!/bin/sh\nmkdir -p \"$XDG_DATA_HOME\"\n" +
                    "test -f \"$XDG_DATA_HOME/quiver-save\" || printf saved > \"$XDG_DATA_HOME/quiver-save\"\n" +
                    $"printf '{version}\\n'\n");
                File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                var repository = Path.Combine(root, "repo");
                var export = await runner.RunAsync(["build-export", "--arch=" + runtime!.Split('/')[1], repository, build, "stable"],
                    TestContext.Current.CancellationToken);
                export.ExitCode.Should().Be(0, export.Error);
                var path = Path.Combine(root, version + ".flatpak");
                var bundle = await runner.RunAsync(["build-bundle", "--arch=" + runtime.Split('/')[1], repository, path, id, "stable"],
                    TestContext.Current.CancellationToken);
                bundle.ExitCode.Should().Be(0, bundle.Error);
                return path;
            }

            var first = await service.InstallAsync(await Bundle("v1"), "v1", gamePath, token: TestContext.Current.CancellationToken);
            dataPath = FlatpakService.DataDirectory(first);
            var launched = await runner.RunAsync(FlatpakService.LaunchArguments(first), TestContext.Current.CancellationToken);
            launched.ExitCode.Should().Be(0, launched.Error);
            launched.Output.Trim().Should().Be("v1");
            var save = Path.Combine(dataPath, "data", "quiver-save");
            File.ReadAllText(save).Should().Be("saved");

            var second = await service.InstallAsync(await Bundle("v2"), "v2", gamePath, token: TestContext.Current.CancellationToken);
            second.Commit.Should().NotBe(first.Commit);
            launched = await runner.RunAsync(FlatpakService.LaunchArguments(second), TestContext.Current.CancellationToken);
            launched.ExitCode.Should().Be(0, launched.Error);
            launched.Output.Trim().Should().Be("v2");
            await service.UninstallAsync(gamePath, TestContext.Current.CancellationToken);
            (await service.GetStateAsync(gamePath, TestContext.Current.CancellationToken))!.Installed.Should().BeFalse();
            File.ReadAllText(save).Should().Be("saved");
        }
        finally
        {
            if (FlatpakService.HasReceipt(gamePath))
                await service.UninstallAsync(gamePath);
            // Only the unique disposable app created above; never touch runtime or other app data.
            if (dataPath != null && Path.GetFileName(dataPath) == id && Directory.Exists(dataPath))
                Directory.Delete(dataPath, recursive: true);
            TestFixtures.CleanupDirectory(root);
        }
    }
}
