using System.Runtime.InteropServices;
using FluentAssertions;
using QuiverLauncher.Core.Services;

namespace QuiverLauncher.Tests;

public class FindExecutableCandidatesNeedsWineTests
{
    [Fact]
    public void FindExecutableCandidates_windows_exe_only_sets_needsWine_on_linux()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        var gamePath = CreateTempDir();
        try
        {
            File.WriteAllBytes(Path.Combine(gamePath, "game.exe"), [1, 2, 3, 4]);

            var candidates = GameInstallationService.FindExecutableCandidates(
                gamePath,
                SearchOption.TopDirectoryOnly,
                null,
                out var needsWine);

            needsWine.Should().BeTrue();
            candidates.Should().ContainSingle(p => p.EndsWith("game.exe", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(gamePath, true);
        }
    }

    [Fact]
    public void FindExecutableCandidates_native_linux_binary_clears_needsWine_even_with_exe()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        var gamePath = CreateTempDir();
        try
        {
            File.WriteAllBytes(Path.Combine(gamePath, "game.x86_64"), new byte[2048]);
            File.WriteAllBytes(Path.Combine(gamePath, "game.exe"), [1, 2, 3, 4]);

            var candidates = GameInstallationService.FindExecutableCandidates(
                gamePath,
                SearchOption.TopDirectoryOnly,
                null,
                out var needsWine);

            needsWine.Should().BeFalse();
            candidates.Should().Contain(p => p.EndsWith("game.x86_64", StringComparison.OrdinalIgnoreCase));
            candidates.Should().NotContain(p => p.EndsWith("game.exe", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(gamePath, true);
        }
    }

    [Fact]
    public void FindExecutableCandidates_empty_dir_does_not_need_wine()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        var gamePath = CreateTempDir();
        try
        {
            var candidates = GameInstallationService.FindExecutableCandidates(
                gamePath,
                SearchOption.TopDirectoryOnly,
                null,
                out var needsWine);

            needsWine.Should().BeFalse();
            candidates.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(gamePath, true);
        }
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "QuiverNeedsWine_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
