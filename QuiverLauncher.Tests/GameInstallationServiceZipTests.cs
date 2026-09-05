using System.IO.Compression;
using System.Text;
using FluentAssertions;
using QuiverLauncher.Core.Services;

namespace QuiverLauncher.Tests;

public class GameInstallationServiceZipTests
{
    private const int UnixExecutableAttributes = unchecked((int)0x81ED0000);

    [Fact]
    public async Task InstallOrUpdateGameAsync_keeps_nested_executable_and_unix_mode()
    {
        var archivePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            var launcher = Encoding.UTF8.GetBytes(new string('L', 2048));
            var biosTool = Encoding.UTF8.GetBytes(new string('B', 2048));
            File.WriteAllBytes(archivePath, CreateZip(
            [
                new ZipSpec("Yu_Gi_Oh_Forbidden_Memories_Recompiled", launcher, UnixExecutableAttributes),
                new ZipSpec("psxrecomp/recompiler/build/psxrecomp-bios", biosTool, UnixExecutableAttributes),
            ]));

            await GameInstallationService.InstallOrUpdateGameAsync(
                archivePath,
                gamePath,
                "ygofm-0.5.3-linux-x64.zip",
                "v0.5.3");

            var nested = Path.Combine(gamePath, "psxrecomp", "recompiler", "build", "psxrecomp-bios");
            File.ReadAllBytes(nested).Should().Equal(biosTool);
            File.Exists(Path.Combine(gamePath, "Yu_Gi_Oh_Forbidden_Memories_Recompiled")).Should().BeTrue();

            if (!OperatingSystem.IsWindows())
            {
                File.GetUnixFileMode(nested).Should().HaveFlag(UnixFileMode.UserExecute);
            }
        }
        finally
        {
            TryDelete(archivePath, gamePath);
        }
    }

    [Fact]
    public void ExtractZipManaged_turns_backslash_entry_names_into_directories()
    {
        var archivePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
        var extractPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            var bios = Encoding.UTF8.GetBytes("openbios");
            var mod = Encoding.UTF8.GetBytes("mod");
            File.WriteAllBytes(archivePath, CreateZip(
            [
                new ZipSpec(@"bios\openbios.bin", bios),
                new ZipSpec(@"mods\preloaded\readme.txt", mod),
            ], normalizeSlashes: false));

            GameInstallationService.ExtractZipManaged(archivePath, extractPath);

            File.ReadAllBytes(Path.Combine(extractPath, "bios", "openbios.bin")).Should().Equal(bios);
            File.ReadAllBytes(Path.Combine(extractPath, "mods", "preloaded", "readme.txt")).Should().Equal(mod);
            Directory.GetFiles(extractPath, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Should().NotContain(name => name.Contains('\\'));
        }
        finally
        {
            TryDelete(archivePath, extractPath);
        }
    }

    [Fact]
    public async Task InstallOrUpdateGameAsync_keeps_empty_directory_after_single_folder_unwrap()
    {
        var archivePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            var binary = Encoding.UTF8.GetBytes(new string('E', 2048));
            File.WriteAllBytes(archivePath, CreateZip(
            [
                new ZipSpec("Game-1.2.3/game.exe", binary),
                new ZipSpec("Game-1.2.3/empty/", []),
            ]));

            await GameInstallationService.InstallOrUpdateGameAsync(
                archivePath,
                gamePath,
                "Game-1.2.3.zip",
                "v1.2.3");

            File.ReadAllBytes(Path.Combine(gamePath, "game.exe")).Should().Equal(binary);
            Directory.Exists(Path.Combine(gamePath, "empty")).Should().BeTrue();
            Directory.Exists(Path.Combine(gamePath, "Game-1.2.3")).Should().BeFalse();
        }
        finally
        {
            TryDelete(archivePath, gamePath);
        }
    }

    [Fact]
    public void MoveDirectoryContents_moves_top_level_folders_including_empty_ones()
    {
        var source = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var dest = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(Path.Combine(source, "mods", "empty"));
            File.WriteAllText(Path.Combine(source, "game.toml"), "title = \"demo\"");

            GameInstallationService.MoveDirectoryContents(source, dest);

            File.ReadAllText(Path.Combine(dest, "game.toml")).Should().Be("title = \"demo\"");
            Directory.Exists(Path.Combine(dest, "mods", "empty")).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(source))
                Directory.Delete(source, true);
            if (Directory.Exists(dest))
                Directory.Delete(dest, true);
        }
    }

    static void TryDelete(string archivePath, string directoryPath)
    {
        if (File.Exists(archivePath))
            File.Delete(archivePath);
        if (Directory.Exists(directoryPath))
            Directory.Delete(directoryPath, true);
    }

    readonly record struct ZipSpec(string Path, byte[] Content, int? ExternalAttributes = null);

    static byte[] CreateZip(IReadOnlyList<ZipSpec> entries, bool normalizeSlashes = true)
    {
        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var spec in entries)
            {
                var name = normalizeSlashes ? spec.Path.Replace('\\', '/') : spec.Path;
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                if (spec.ExternalAttributes is int attributes)
                    entry.ExternalAttributes = attributes;

                if (spec.Content.Length == 0 && name.EndsWith('/'))
                    continue;

                using var entryStream = entry.Open();
                entryStream.Write(spec.Content);
            }
        }

        return zipStream.ToArray();
    }
}
