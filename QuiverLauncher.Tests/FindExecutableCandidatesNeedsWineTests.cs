using System.Runtime.InteropServices;
using FluentAssertions;
using QuiverLauncher.Core.Services;

namespace QuiverLauncher.Tests;

public class FindExecutableCandidatesNeedsWineTests
{
    [Fact]
    public void FindExecutableCandidates_windows_exe_only_sets_needsWine_on_linux()
    {
        var gamePath = CreateTempDir();
        try
        {
            File.WriteAllBytes(Path.Combine(gamePath, "game.exe"), [1, 2, 3, 4]);

            var candidates = GameInstallationService.FindExecutableCandidates(
                gamePath,
                SearchOption.TopDirectoryOnly,
                null,
                out var needsWine, OSPlatform.Linux);

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
        var gamePath = CreateTempDir();
        try
        {
            File.WriteAllBytes(Path.Combine(gamePath, "game.x86_64"), new byte[2048]);
            File.WriteAllBytes(Path.Combine(gamePath, "game.exe"), [1, 2, 3, 4]);

            var candidates = GameInstallationService.FindExecutableCandidates(
                gamePath,
                SearchOption.TopDirectoryOnly,
                null,
                out var needsWine, OSPlatform.Linux);

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
        var gamePath = CreateTempDir();
        try
        {
            var candidates = GameInstallationService.FindExecutableCandidates(
                gamePath,
                SearchOption.TopDirectoryOnly,
                null,
                out var needsWine, OSPlatform.Linux);

            needsWine.Should().BeFalse();
            candidates.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(gamePath, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Triaevum_license_and_readme_do_not_hide_windows_executable_choices(bool nested)
    {
        var root = CreateTempDir();
        try
        {
            var license = Path.Combine(root, "LICENSE");
            File.WriteAllText(license, new string('L', 35000));
            File.WriteAllText(Path.Combine(root, "README"), new string('R', 4096));
            File.WriteAllText(Path.Combine(root, "COPYING"), new string('C', 4096));
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(license, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            var bin = nested ? Directory.CreateDirectory(Path.Combine(root, "TriAevum")).FullName : root;
            var game = Path.Combine(bin, "TriAevum.exe");
            var forge = Path.Combine(bin, "TriAevumForge.exe");
            File.WriteAllBytes(game, [0x4d, 0x5a]);
            File.WriteAllBytes(forge, [0x4d, 0x5a]);

            var candidates = GameInstallationService.FindExecutableCandidates(root, SearchOption.TopDirectoryOnly,
                null, out var wine, OSPlatform.Linux);
            if (nested)
            {
                candidates.Should().BeEmpty("documents at the root must not prevent scanning the wrapper folder");
                candidates = GameInstallationService.FindExecutableCandidates(root, SearchOption.AllDirectories,
                    null, out wine, OSPlatform.Linux);
            }
            candidates.Should().Equal(game, forge);
            wine.Should().BeTrue();
            GameInstallationService.IsValidSavedExecutable(license, OSPlatform.Linux).Should().BeFalse();
            GameInstallationService.IsValidSavedExecutable(game, OSPlatform.Linux).Should().BeTrue();
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(1, 1, 2, true, true)]
    [InlineData(2, 1, 3, true, true)]
    [InlineData(2, 2, 2, true, true)]
    [InlineData(2, 1, 3, false, false)]
    [InlineData(2, 1, 1, true, false)]
    public void Extensionless_elf_programs_are_detected_by_header_not_size(int elfClass, int byteOrder, int type,
        bool entryPoint, bool expected)
    {
        var root = CreateTempDir();
        try
        {
            var program = Path.Combine(root, "game");
            var header = new byte[64];
            header[0] = 0x7f; header[1] = (byte)'E'; header[2] = (byte)'L'; header[3] = (byte)'F';
            header[4] = (byte)elfClass; header[5] = (byte)byteOrder; header[6] = 1;
            header[byteOrder == 1 ? 16 : 17] = (byte)type;
            if (entryPoint) header[24] = 1;
            File.WriteAllBytes(program, header);
            File.WriteAllBytes(Path.Combine(root, "game.exe"), [0x4d, 0x5a]);
            var candidates = GameInstallationService.FindExecutableCandidates(root, SearchOption.TopDirectoryOnly,
                null, out var wine, OSPlatform.Linux);
            candidates.Should().ContainSingle().Which.Should().Be(expected ? program : Path.Combine(root, "game.exe"));
            wine.Should().Be(!expected);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("#!/bin/sh\nexit 0\n", true)]
    [InlineData("#! /usr/bin/env bash\nexit 0\n", true)]
    [InlineData("#!This is documentation\n", false)]
    [InlineData("\u007fELF", false)]
    [InlineData("", false)]
    public void Extensionless_scripts_require_a_shebang_and_malformed_files_are_ignored(string contents, bool expected)
    {
        var root = CreateTempDir();
        try
        {
            var program = Path.Combine(root, "launch");
            File.WriteAllText(program, contents);
            var candidates = GameInstallationService.FindExecutableCandidates(root, SearchOption.TopDirectoryOnly,
                null, out var wine, OSPlatform.Linux);
            candidates.Contains(program).Should().Be(expected);
            wine.Should().BeFalse();
        }
        finally { Directory.Delete(root, true); }
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "QuiverNeedsWine_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
