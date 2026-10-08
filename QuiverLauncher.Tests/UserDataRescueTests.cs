using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public sealed class UserDataRescueTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "quiver-rescue", Guid.NewGuid().ToString("N"));

    public UserDataRescueTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void The_top_of_a_drive_stays_a_folder_of_its_own()
    {
        var driveRoot = Path.GetPathRoot(Path.GetTempPath())!;

        QuiverLauncherPaths.NormalizeDirectory(driveRoot).Should().Be(driveRoot);
        Path.Combine(QuiverLauncherPaths.NormalizeDirectory(driveRoot), "apps.json")
            .Should().Be(driveRoot + "apps.json");
    }

    [Fact]
    public void A_windows_drive_root_keeps_its_backslash()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows drive paths.");

        QuiverLauncherPaths.NormalizeDirectory("G:\\").Should().Be("G:\\");
        QuiverLauncherPaths.NormalizeDirectory("G:\\Quiver\\").Should().Be("G:\\Quiver");
    }

    [Fact]
    public void Other_folders_lose_their_trailing_separator()
    {
        var folder = Path.Combine(_root, "Quiver");

        QuiverLauncherPaths.NormalizeDirectory(folder + Path.DirectorySeparatorChar).Should().Be(folder);
    }

    [Fact]
    public void Moves_a_library_left_in_current_beside_it()
    {
        var current = Path.Combine(_root, "current");
        Write(current, "QuiverLauncher.exe", "app");
        Write(current, "apps.json", "library");
        Write(current, "settings.json", "settings");
        Write(current, Path.Combine("Apps", "Celeste", "Celeste.exe"), "game");
        Write(current, Path.Combine("Backups", "apps", "old.json"), "backup");
        // What startup creates before the library is found.
        Directory.CreateDirectory(Path.Combine(_root, "Apps"));
        Directory.CreateDirectory(Path.Combine(_root, "Cache"));
        Write(current, Path.Combine("Cache", "thumb.png"), "thumb");

        UserDataRescue.AtStartup(_root, current);

        File.ReadAllText(Path.Combine(_root, "apps.json")).Should().Be("library");
        File.ReadAllText(Path.Combine(_root, "settings.json")).Should().Be("settings");
        File.ReadAllText(Path.Combine(_root, "Apps", "Celeste", "Celeste.exe")).Should().Be("game");
        File.ReadAllText(Path.Combine(_root, "Backups", "apps", "old.json")).Should().Be("backup");
        File.ReadAllText(Path.Combine(_root, "Cache", "thumb.png")).Should().Be("thumb");
        Directory.Exists(Path.Combine(current, "Apps")).Should().BeFalse();
        File.Exists(Path.Combine(current, "apps.json")).Should().BeFalse();
        File.ReadAllText(Path.Combine(current, "QuiverLauncher.exe")).Should().Be("app");
    }

    [Fact]
    public void Rescues_the_library_from_the_copy_the_updater_is_about_to_delete()
    {
        var old = Path.Combine(_root, "packages", "VelopackTemp", "tmp_pJlx3wwppPcznHfo");
        Write(old, "QuiverLauncher.dll", "old app");
        Write(old, "apps.json", "library");
        Write(old, Path.Combine("Apps", "Celeste", "Celeste.exe"), "game");

        UserDataRescue.AfterUpdate(_root);

        File.ReadAllText(Path.Combine(_root, "apps.json")).Should().Be("library");
        File.ReadAllText(Path.Combine(_root, "Apps", "Celeste", "Celeste.exe")).Should().Be("game");
        File.Exists(Path.Combine(old, "QuiverLauncher.dll")).Should().BeTrue();
        File.Exists(Path.Combine(_root, "QuiverLauncher.dll")).Should().BeFalse();
    }

    [Fact]
    public void Never_replaces_data_already_beside_current()
    {
        var current = Path.Combine(_root, "current");
        Write(_root, "apps.json", "live library");
        Write(_root, Path.Combine("Apps", "Celeste", "Celeste.exe"), "live game");
        Write(current, "apps.json", "stale library");
        Write(current, Path.Combine("Apps", "Celeste", "Celeste.exe"), "stale game");
        Write(current, Path.Combine("Apps", "Hollow Knight", "hollow_knight.exe"), "other game");

        UserDataRescue.AtStartup(_root, current);

        File.ReadAllText(Path.Combine(_root, "apps.json")).Should().Be("live library");
        File.ReadAllText(Path.Combine(_root, "Apps", "Celeste", "Celeste.exe")).Should().Be("live game");
        File.ReadAllText(Path.Combine(_root, "Apps", "Hollow Knight", "hollow_knight.exe")).Should().Be("other game");
        File.ReadAllText(Path.Combine(current, "apps.json")).Should().Be("stale library");
    }

    [Fact]
    public void The_newest_library_wins_when_several_old_copies_are_left()
    {
        var temp = Path.Combine(_root, "packages", "VelopackTemp");
        Write(Path.Combine(temp, "tmp_older"), "apps.json", "older");
        Write(Path.Combine(temp, "tmp_newer"), "apps.json", "newer");
        File.SetLastWriteTimeUtc(Path.Combine(temp, "tmp_older", "apps.json"), DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(Path.Combine(temp, "tmp_newer", "apps.json"), DateTime.UtcNow.AddDays(-1));

        UserDataRescue.AfterUpdate(_root);

        File.ReadAllText(Path.Combine(_root, "apps.json")).Should().Be("newer");
    }

    [Fact]
    public void Leaves_a_normal_install_alone()
    {
        var current = Path.Combine(_root, "current");
        Write(current, "QuiverLauncher.exe", "app");
        Write(_root, "apps.json", "library");

        UserDataRescue.AtStartup(_root, current);
        UserDataRescue.AtStartup(_root, _root);
        UserDataRescue.AfterUpdate(_root);

        Directory.GetFileSystemEntries(_root).Select(Path.GetFileName)
            .Should().BeEquivalentTo("current", "apps.json");
    }

    private static void Write(string folder, string relativePath, string content)
    {
        var path = Path.Combine(folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
