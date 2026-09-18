using System.Runtime.InteropServices;
using FluentAssertions;
using QuiverLauncher.Core.Services;

namespace QuiverLauncher.Tests;

public class FindExecutableCandidatesNeedsWineTests
{
    [Theory]
    [InlineData("game.exe")]
    [InlineData("game.EXE")]
    [InlineData("nested/game.EXE")]
    public void FindExecutableCandidates_windows_exe_only_sets_needsWine_on_linux(string name)
    {
        var gamePath = CreateTempDir();
        try
        {
            var executable = Path.GetFullPath(Path.Combine(gamePath, name));
            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            File.WriteAllBytes(executable, [1, 2, 3, 4]);

            var candidates = GameInstallationService.FindExecutableCandidates(
                gamePath,
                SearchOption.AllDirectories,
                null,
                out var needsWine, OSPlatform.Linux);

            needsWine.Should().BeTrue();
            candidates.Should().ContainSingle().Which.Should().Be(executable);
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

    [Theory]
    [InlineData("game", true)]
    [InlineData("game.bin", true)]
    [InlineData("game.x86", true)]
    [InlineData("game.v1.2", true)]
    [InlineData("libgame.so", false)]
    [InlineData("libgame.so.1", false)]
    [InlineData("version.txt", false)]
    [InlineData("save.bin", false)]
    public void Linux_native_detection_handles_suffixes_and_rejects_non_programs(string name, bool expected)
    {
        var root = CreateTempDir();
        try
        {
            var header = new byte[64];
            header[0] = 0x7f; header[1] = (byte)'E'; header[2] = (byte)'L'; header[3] = (byte)'F';
            header[4] = 2; header[5] = 1; header[6] = 1; header[16] = 3;
            if (name != "save.bin") header[24] = 1;
            var program = Path.Combine(root, name);
            File.WriteAllBytes(program, header);
            File.WriteAllText(Path.Combine(root, "README.md"), "documentation");
            var candidates = GameInstallationService.FindExecutableCandidates(root, SearchOption.AllDirectories,
                null, out _, OSPlatform.Linux);
            candidates.Contains(program).Should().Be(expected);
            candidates.Should().NotContain(p => p.EndsWith("README.md"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Linux_unreadable_child_does_not_hide_accessible_executable()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Requires Linux filesystem permissions.");
        var root = CreateTempDir();
        var blocked = Directory.CreateDirectory(Path.Combine(root, "blocked")).FullName;
        var originalMode = File.GetUnixFileMode(blocked);
        try
        {
            var program = Path.Combine(root, "game.EXE");
            File.WriteAllBytes(program, [0x4d, 0x5a]);
            File.SetUnixFileMode(blocked, UnixFileMode.None);
            try
            {
                Directory.GetFiles(blocked);
                Assert.Skip("Current user can bypass directory permissions.");
            }
            catch (UnauthorizedAccessException) { }
            var candidates = GameInstallationService.FindExecutableCandidates(root, SearchOption.AllDirectories,
                null, out var wine, OSPlatform.Linux);
            candidates.Should().ContainSingle().Which.Should().Be(program);
            wine.Should().BeTrue();
        }
        finally
        {
            File.SetUnixFileMode(blocked, originalMode);
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(".steam-compat-data", true)]
    [InlineData(".wine-prefix", true)]
    [InlineData("custom-prefix", true)]
    [InlineData(".steam-compat-data", false)]
    [InlineData(".wine-prefix", false)]
    [InlineData("custom-prefix", false)]
    public void Compatibility_prefixes_are_not_game_executables(string prefixName, bool hasGame)
    {
        var root = CreateTempDir();
        try
        {
            var prefix = Path.Combine(root, prefixName);
            Directory.CreateDirectory(Path.Combine(prefix, "dosdevices"));
            var windows = Directory.CreateDirectory(Path.Combine(prefix, "drive_c", "windows")).FullName;
            File.WriteAllBytes(Path.Combine(windows, "notepad.exe"), [0x4d, 0x5a]);
            var nested = Directory.CreateDirectory(Path.Combine(root, "game", "bin")).FullName;
            var executable = Path.Combine(nested, "AnimalCrossing.exe");
            if (hasGame) File.WriteAllBytes(executable, [0x4d, 0x5a]);
            var candidates = GameInstallationService.FindExecutableCandidates(root, SearchOption.AllDirectories,
                null, out var wine, OSPlatform.Linux);
            candidates.Should().Equal(hasGame ? new[] { executable } : Array.Empty<string>());
            wine.Should().Be(hasGame);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Linux_directory_links_cannot_escape_or_cycle_but_file_links_still_work()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Requires Linux symbolic links.");
        var root = CreateTempDir();
        var outside = CreateTempDir();
        try
        {
            var external = Path.Combine(outside, "outside.exe");
            File.WriteAllBytes(external, [0x4d, 0x5a]);
            Directory.CreateSymbolicLink(Path.Combine(root, "outside"), outside);
            Directory.CreateSymbolicLink(Path.Combine(root, "loop"), root);
            var game = Path.Combine(root, "game.exe");
            File.CreateSymbolicLink(game, external);
            var candidates = GameInstallationService.FindExecutableCandidates(root, SearchOption.AllDirectories,
                null, out var wine, OSPlatform.Linux);
            candidates.Should().Equal(game);
            wine.Should().BeTrue();
        }
        finally { Directory.Delete(root, true); Directory.Delete(outside, true); }
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "QuiverNeedsWine_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
