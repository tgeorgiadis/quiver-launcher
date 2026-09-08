using FluentAssertions;
using QuiverLauncher.Core.Services;
using SharpCompress.Common;
using SharpCompress.Writers;
using System.Text;

namespace QuiverLauncher.Tests;

public class GameInstallationServiceSevenZipTests
{
    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    [Fact]
    public async Task InstallOrUpdateGameAsync_extracts_7z_asset()
    {
        var archivePath = FixturePath("sample-release-flat.7z");
        var downloadPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.7z");
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            File.Copy(archivePath, downloadPath);

            await GameInstallationService.InstallOrUpdateGameAsync(
                downloadPath,
                gamePath,
                "game-win.7z",
                "v1.2.3");

            (await File.ReadAllTextAsync(Path.Combine(gamePath, "game.exe"))).Should().Be("flat-binary");
            (await File.ReadAllTextAsync(Path.Combine(gamePath, "readme.txt"))).Should().Be("flat-readme");
            (await File.ReadAllTextAsync(Path.Combine(gamePath, "version.txt"))).Trim().Should().Be("v1.2.3");
        }
        finally
        {
            if (File.Exists(downloadPath))
                File.Delete(downloadPath);
            if (Directory.Exists(gamePath))
                Directory.Delete(gamePath, true);
        }
    }

    [Fact]
    public async Task InstallOrUpdateGameAsync_strips_single_root_directory_from_7z()
    {
        var archivePath = FixturePath("sample-release-wrapped.7z");
        var downloadPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.7z");
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            File.Copy(archivePath, downloadPath);

            await GameInstallationService.InstallOrUpdateGameAsync(
                downloadPath,
                gamePath,
                "AppRoot-release.7z",
                "v2.0.0");

            (await File.ReadAllTextAsync(Path.Combine(gamePath, "game.exe"))).Should().Be("wrapped-binary");
            (await File.ReadAllTextAsync(Path.Combine(gamePath, "readme.txt"))).Should().Be("wrapped-readme");
            Directory.Exists(Path.Combine(gamePath, "AppRoot")).Should().BeFalse();
            (await File.ReadAllTextAsync(Path.Combine(gamePath, "version.txt"))).Trim().Should().Be("v2.0.0");
        }
        finally
        {
            if (File.Exists(downloadPath))
                File.Delete(downloadPath);
            if (Directory.Exists(gamePath))
                Directory.Delete(gamePath, true);
        }
    }

    [Fact]
    public async Task InstallOrUpdateGameAsync_extracts_generated_solid_7z_and_reports_progress()
    {
        var downloadPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.7z");
        var gamePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var progress = new List<double>();

        try
        {
            WriteSolidSevenZip(downloadPath, new Dictionary<string, string>
            {
                ["game.exe"] = "solid-binary-" + new string('A', 4096),
                ["data/level1.bin"] = new string('B', 8192),
                ["data/level2.bin"] = new string('C', 8192),
                ["readme.txt"] = "solid-readme",
            });

            await GameInstallationService.InstallOrUpdateGameAsync(
                downloadPath,
                gamePath,
                "game-win.7z",
                "v3.0.0",
                new GameInstallationOptions
                {
                    ExtractProgress = new Progress<double>(p => progress.Add(p)),
                });

            (await File.ReadAllTextAsync(Path.Combine(gamePath, "game.exe"))).Should().StartWith("solid-binary-");
            (await File.ReadAllTextAsync(Path.Combine(gamePath, "data", "level1.bin"))).Should().Be(new string('B', 8192));
            (await File.ReadAllTextAsync(Path.Combine(gamePath, "data", "level2.bin"))).Should().Be(new string('C', 8192));
            (await File.ReadAllTextAsync(Path.Combine(gamePath, "readme.txt"))).Should().Be("solid-readme");
            (await File.ReadAllTextAsync(Path.Combine(gamePath, "version.txt"))).Trim().Should().Be("v3.0.0");
            progress.Should().NotBeEmpty();
            progress.Last().Should().Be(1);
            progress.Should().OnlyContain(p => p >= 0 && p <= 1);
        }
        finally
        {
            if (File.Exists(downloadPath))
                File.Delete(downloadPath);
            if (Directory.Exists(gamePath))
                Directory.Delete(gamePath, true);
        }
    }

    internal static void WriteSolidSevenZip(string path, IReadOnlyDictionary<string, string> files)
    {
        using var stream = File.Create(path);
        using var writer = WriterFactory.OpenWriter(
            stream,
            ArchiveType.SevenZip,
            new WriterOptions(CompressionType.LZMA));
        foreach (var (name, content) in files)
        {
            using var source = new MemoryStream(Encoding.UTF8.GetBytes(content));
            writer.Write(name, source, DateTime.UtcNow);
        }
    }
}

