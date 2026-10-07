using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using QuiverLauncher.Core.Services;

namespace QuiverLauncher.Tests;

public class GameInstallationServiceMacBundleTests
{
    private const int UnixExecutableAttributes = unchecked((int)0x81ED0000);
    private const int UnixSymlinkAttributes = unchecked((int)0xA1FF0000);

    [Fact]
    public void CopyDirectory_recreates_symlinks_instead_of_following_them()
    {
        if (OperatingSystem.IsWindows())
            return;

        var root = NewTempDirectory();
        try
        {
            var source = Path.Combine(root, "src");
            var versionA = Path.Combine(source, "Versions", "A");
            Directory.CreateDirectory(versionA);
            File.WriteAllText(Path.Combine(versionA, "Lib"), "binary");
            Directory.CreateSymbolicLink(Path.Combine(source, "Versions", "Current"), "A");
            File.CreateSymbolicLink(Path.Combine(source, "Lib"), "Versions/Current/Lib");

            var dest = Path.Combine(root, "dest");
            GameInstallationService.CopyDirectory(source, dest);

            new DirectoryInfo(Path.Combine(dest, "Versions", "Current")).LinkTarget.Should().Be("A");
            new FileInfo(Path.Combine(dest, "Lib")).LinkTarget.Should().Be("Versions/Current/Lib");
            File.ReadAllText(Path.Combine(dest, "Lib")).Should().Be("binary");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task InstallOrUpdateGameAsync_keeps_framework_symlinks_in_zipped_app_bundle()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        var archivePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
        var gamePath = NewTempDirectory();

        try
        {
            File.WriteAllBytes(archivePath, CreateZip(
            [
                new ZipSpec("Game.app/Contents/MacOS/Game", Encoding.UTF8.GetBytes("game"), UnixExecutableAttributes),
                new ZipSpec("Game.app/Contents/Frameworks/Engine.framework/Versions/A/Engine", Encoding.UTF8.GetBytes("engine")),
                new ZipSpec("Game.app/Contents/Frameworks/Engine.framework/Versions/Current", Encoding.UTF8.GetBytes("A"), UnixSymlinkAttributes),
                new ZipSpec("Game.app/Contents/Frameworks/Engine.framework/Engine", Encoding.UTF8.GetBytes("Versions/Current/Engine"), UnixSymlinkAttributes),
            ]));

            await GameInstallationService.InstallOrUpdateGameAsync(archivePath, gamePath, "Game-macOS.zip", "v1.0.0");

            var framework = Path.Combine(gamePath, "Game.app", "Contents", "Frameworks", "Engine.framework");
            new DirectoryInfo(Path.Combine(framework, "Versions", "Current")).LinkTarget.Should().Be("A");
            new FileInfo(Path.Combine(framework, "Engine")).LinkTarget.Should().Be("Versions/Current/Engine");
            File.GetUnixFileMode(Path.Combine(gamePath, "Game.app", "Contents", "MacOS", "Game"))
                .Should().HaveFlag(UnixFileMode.UserExecute);
        }
        finally
        {
            File.Delete(archivePath);
            TryDeleteDirectory(gamePath);
        }
    }

    [Fact]
    public async Task InstallOrUpdateGameAsync_installs_app_bundle_from_dmg()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        var root = NewTempDirectory();
        var gamePath = NewTempDirectory();

        try
        {
            var dmgPath = CreateDmgWithAppBundle(root);

            await GameInstallationService.InstallOrUpdateGameAsync(dmgPath, gamePath, "Game-1.0-macOS.dmg", "v1.0.0");

            AssertInstalledAppBundle(gamePath);
            File.ReadAllText(Path.Combine(gamePath, "version.txt")).Should().Be("v1.0.0");
            Directory.EnumerateFileSystemEntries(gamePath).Select(Path.GetFileName)
                .Should().NotContain("Applications", "the drag-to-install shortcut is not part of the app");
            MountedImagePaths().Should().NotContain(p => p.Contains(dmgPath, StringComparison.Ordinal));
        }
        finally
        {
            TryDeleteDirectory(root);
            TryDeleteDirectory(gamePath);
        }
    }

    [Fact]
    public async Task InstallOrUpdateGameAsync_installs_app_bundle_from_dmg_inside_zip()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        var root = NewTempDirectory();
        var gamePath = NewTempDirectory();

        try
        {
            var dmgPath = CreateDmgWithAppBundle(root);
            var archivePath = Path.Combine(root, "Game-Mac.zip");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
                archive.CreateEntryFromFile(dmgPath, "Game.dmg");

            await GameInstallationService.InstallOrUpdateGameAsync(archivePath, gamePath, "Game-Mac.zip", "v1.0.0");

            AssertInstalledAppBundle(gamePath);
            Directory.EnumerateFiles(gamePath, "*.dmg", SearchOption.AllDirectories).Should().BeEmpty();
        }
        finally
        {
            TryDeleteDirectory(root);
            TryDeleteDirectory(gamePath);
        }
    }

    [Fact]
    public async Task InstallOrUpdateGameAsync_rejects_dmg_outside_macos()
    {
        if (OperatingSystem.IsMacOS())
            return;

        var root = NewTempDirectory();
        try
        {
            var dmgPath = Path.Combine(root, "Game.dmg");
            File.WriteAllText(dmgPath, "not a real image");

            var act = () => GameInstallationService.InstallOrUpdateGameAsync(
                dmgPath, Path.Combine(root, "game"), "Game.dmg", "v1.0.0");

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*only be installed on macOS*");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HasRecognizedInstallExtension_accepts_dmg()
    {
        GameInstallationService.HasRecognizedInstallExtension("Game-macOS.dmg").Should().BeTrue();
        GameInstallationService.HasRecognizedInstallExtension("Game-macOS.DMG").Should().BeTrue();
    }

    static void AssertInstalledAppBundle(string gamePath)
    {
        var app = Path.Combine(gamePath, "Game.app");
        File.ReadAllText(Path.Combine(app, "Contents", "MacOS", "Game")).Should().Be("game");
        File.GetUnixFileMode(Path.Combine(app, "Contents", "MacOS", "Game")).Should().HaveFlag(UnixFileMode.UserExecute);
        new DirectoryInfo(Path.Combine(app, "Contents", "Frameworks", "Engine.framework", "Versions", "Current"))
            .LinkTarget.Should().Be("A");
    }

    static string CreateDmgWithAppBundle(string root)
    {
        var staging = Path.Combine(root, "staging");
        var macOS = Path.Combine(staging, "Game.app", "Contents", "MacOS");
        var versionA = Path.Combine(staging, "Game.app", "Contents", "Frameworks", "Engine.framework", "Versions", "A");
        Directory.CreateDirectory(macOS);
        Directory.CreateDirectory(versionA);
        File.WriteAllText(Path.Combine(macOS, "Game"), "game");
        File.SetUnixFileMode(Path.Combine(macOS, "Game"),
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.WriteAllText(Path.Combine(versionA, "Engine"), "engine");
        Directory.CreateSymbolicLink(Path.Combine(versionA, "..", "Current"), "A");
        // Typical drag-to-install image: a shortcut to /Applications beside the app.
        Directory.CreateSymbolicLink(Path.Combine(staging, "Applications"), "/Applications");

        var dmgPath = Path.Combine(root, "Game.dmg");
        RunTool("hdiutil", "create", "-quiet", "-fs", "HFS+", "-volname", "Game", "-srcfolder", staging, "-format", "UDZO", dmgPath);
        return dmgPath;
    }

    static List<string> MountedImagePaths()
    {
        var output = RunTool("hdiutil", "info");
        return output.Split('\n')
            .Where(line => line.StartsWith("image-path", StringComparison.Ordinal))
            .ToList();
    }

    static string RunTool(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, $"{fileName} failed: {error}");
        return output;
    }

    static string NewTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    static void TryDeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, true);
    }

    readonly record struct ZipSpec(string Path, byte[] Content, int? ExternalAttributes = null);

    static byte[] CreateZip(IReadOnlyList<ZipSpec> entries)
    {
        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var spec in entries)
            {
                var entry = archive.CreateEntry(spec.Path, CompressionLevel.Optimal);
                if (spec.ExternalAttributes is int attributes)
                    entry.ExternalAttributes = attributes;

                using var entryStream = entry.Open();
                entryStream.Write(spec.Content);
            }
        }

        return zipStream.ToArray();
    }
}
