using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using FluentAssertions;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public sealed class LinuxLauncherScriptTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "quiver-script-tests", Guid.NewGuid().ToString("N"));
    private string GamePath => Path.Combine(_root, "Nectar's $ game");
    public LinuxLauncherScriptTests() => Directory.CreateDirectory(GamePath);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public async Task Nectar_package_keeps_small_wrappers_and_their_companion_files_together()
    {
        var archivePath = Path.Combine(_root, "nectar-linux.zip");
        var payload = new Dictionary<string, byte[]>
        {
            ["nectar"] = Encoding.UTF8.GetBytes("#!/bin/sh\nexec ./lib/ld-linux-x86-64.so.2 --library-path ./lib ./nectar.real \"$@\"\n"),
            ["nectar-launcher"] = Encoding.UTF8.GetBytes("#!/bin/sh\nexec ./lib/ld-linux-x86-64.so.2 --library-path ./lib ./nectar-launcher.real \"$@\"\n"),
            ["nectar.real"] = Elf(),
            ["nectar-pal.real"] = Elf(),
            ["nectar-launcher.real"] = Elf(),
            ["lib/ld-linux-x86-64.so.2"] = Elf(),
            ["lib/libc.so.6"] = Elf(),
            ["lib/licenses/COPYING"] = Encoding.UTF8.GetBytes(new string('x', 4096)),
            ["README.txt"] = Encoding.UTF8.GetBytes("Run ./nectar-launcher"),
        };
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            foreach (var (name, bytes) in payload)
            {
                var entry = archive.CreateEntry("nectar-linux/" + name);
                entry.ExternalAttributes = (0x8000 | 0x1ED) << 16; // regular file, 0755
                using var stream = entry.Open();
                stream.Write(bytes);
            }

        await GameInstallationService.InstallOrUpdateGameAsync(archivePath, GamePath, "nectar-linux.zip", "fixture");
        var candidates = GameInstallationService.FindExecutableCandidates(GamePath, SearchOption.AllDirectories,
            null, out var wine, OSPlatform.Linux);
        candidates.Select(Path.GetFileName).Should().Equal("nectar", "nectar-launcher");
        wine.Should().BeFalse();
        foreach (var (name, bytes) in payload)
            File.ReadAllBytes(Path.Combine(GamePath, name)).Should().Equal(bytes);
        if (OperatingSystem.IsLinux())
            File.GetUnixFileMode(Path.Combine(GamePath, "lib/ld-linux-x86-64.so.2"))
                .HasFlag(UnixFileMode.UserExecute).Should().BeTrue();

        var game = new GameInfo { FolderName = Path.GetFileName(GamePath) };
        game.SaveSelectedExecutable(Path.Combine(GamePath, "nectar-launcher"), _root);
        game.LoadSelectedExecutable(_root).Should().Be(Path.Combine(GamePath, "nectar-launcher"));
    }

    [Fact]
    public void Mixed_windows_and_shell_package_keeps_both_choices()
    {
        File.WriteAllText(Path.Combine(GamePath, "launch.sh"), "#!/bin/sh\nexit 0\n");
        File.WriteAllText(Path.Combine(GamePath, "game.exe"), "MZ");
        var candidates = GameInstallationService.FindExecutableCandidates(GamePath, SearchOption.TopDirectoryOnly,
            null, out _, OSPlatform.Linux);
        candidates.Select(Path.GetFileName).Should().Equal("game.exe", "launch.sh");
    }

    [Theory]
    [InlineData("nectar-launcher", false)]
    [InlineData("launch.sh", true)]
    public async Task Linux_launch_and_shortcuts_use_saved_wrapper_natively(string name, bool throughAppAction)
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Skip("Requires Linux to execute the disposable shell wrapper.");
            return;
        }
        var wrapper = Path.Combine(GamePath, name);
        Directory.CreateDirectory(Path.Combine(GamePath, "lib"));
        // A real native interpreter stands in for Nectar's bundled ELF loader.
        var loader = Path.Combine(GamePath, "lib", "fixture-loader");
        File.Copy("/bin/sh", loader);
        File.SetUnixFileMode(loader, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.WriteAllText(wrapper, "#!/bin/sh\nexec ./lib/fixture-loader ./nectar-launcher.real \"$@\"\n");
        File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.WriteAllText(Path.Combine(GamePath, "nectar-launcher.real"), "printf '%s\\n' \"$PWD\" \"$@\" > launched.txt\n");
        File.WriteAllText(Path.Combine(GamePath, "game.exe"), "MZ");
        var game = new GameInfo
        {
            Name = "Disposable Nectar", FolderName = Path.GetFileName(GamePath), Status = GameStatus.Installed,
            LinuxRunner = "custom", LinuxCustomLaunchCommand = "/bin/false '{exe}'"
        };
        game.SaveSelectedExecutable(wrapper, _root);
        Process? launched = null;
        game.GameProcessStarted += process => launched = process;
        using var client = new HttpClient();
        var result = throughAppAction
            ? await game.PerformActionAsync(client, _root, new AppSettings(), HeadlessGameDownloadDialogs.Instance)
            : await GameLaunchService.LaunchAsync(game, _root);
        result.Should().BeTrue();
        launched.Should().NotBeNull("GUI and CLI launches must still notify process tracking");
        using (launched)
        {
            await launched!.WaitForExitAsync(TestContext.Current.CancellationToken);
            launched.ExitCode.Should().Be(0);
        }
        File.ReadAllLines(Path.Combine(GamePath, "launched.txt"))[0].Should().Be(GamePath);
        File.Exists(Path.Combine(GamePath, "LastPlayed.txt")).Should().BeTrue();
        File.GetUnixFileMode(wrapper).HasFlag(UnixFileMode.UserExecute).Should().BeTrue();

        game.SelectedExecutable = null; // Simulate a later shortcut worker or CLI session.
        var target = await GameShortcutLaunch.PrepareAsync(game, _root, new(), _ => throw new Exception("Must reuse selection"));
        target!.FileName.Should().Be(wrapper);
        target.WorkingDirectory.Should().Be(GamePath);
        target.Arguments.Should().BeEmpty();
        var desktop = ShortcutHelper.BuildLinuxDesktopFile(game.Name!, target, null);
        desktop.Should().Contain(name).And.Contain("Path=" + GamePath).And.NotContain("/bin/false");
        var steamPath = Path.Combine(_root, "shortcuts.vdf");
        ShortcutHelper.WriteGameToSteamFile(steamPath, game, target, null, out _);
        var steam = Encoding.UTF8.GetString(File.ReadAllBytes(steamPath));
        steam.Should().Contain("exe\0\"" + wrapper.Replace("$", "\\$") + "\"\0")
            .And.Contain("StartDir\0\"" + GamePath.Replace("$", "\\$") + "\"\0").And.NotContain("/bin/false");
    }

    private static byte[] Elf()
    {
        var header = new byte[64];
        header[0] = 0x7f; header[1] = (byte)'E'; header[2] = (byte)'L'; header[3] = (byte)'F';
        header[4] = 2; header[5] = 1; header[6] = 1; header[16] = 2; header[24] = 1;
        return header;
    }
}
