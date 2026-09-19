using System.Text;
using System.Text.Json;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public sealed class LauncherUpdateBackupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "quiver-update-backup-" + Guid.NewGuid().ToString("N"));
    public LauncherUpdateBackupTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Update_runs_only_after_both_exact_files_are_backed_up_and_old_snapshots_survive()
    {
        var apps = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("{\"apps\":[{\"name\":\"Saved\"}]}" )).ToArray();
        var settings = Encoding.Unicode.GetBytes("{\"theme\":\"dark\"}");
        File.WriteAllBytes(Path.Combine(_root, "apps.json"), apps);
        File.WriteAllBytes(Path.Combine(_root, "settings.json"), settings);
        var ran = false;
        await LauncherUpdateBackup.BeforeUpdateAsync(() =>
        {
            var snapshot = Directory.GetDirectories(Path.Combine(_root, "Backups", "updates")).Single();
            File.ReadAllBytes(Path.Combine(snapshot, "apps.json")).Should().Equal(apps);
            File.ReadAllBytes(Path.Combine(snapshot, "settings.json")).Should().Equal(settings);
            File.Exists(Path.Combine(snapshot, "manifest.json")).Should().BeTrue();
            ran = true;
            return Task.CompletedTask;
        }, _root);
        ran.Should().BeTrue();
        File.WriteAllText(Path.Combine(_root, "apps.json"), "{\"apps\":[]}");
        LauncherUpdateBackup.BeforeUpdate(() => { }, _root);
        var snapshots = Directory.GetDirectories(Path.Combine(_root, "Backups", "updates"));
        snapshots.Should().HaveCount(2);
        snapshots.Should().Contain(folder => File.ReadAllBytes(Path.Combine(folder, "apps.json")).SequenceEqual(apps));
        File.ReadAllBytes(Path.Combine(_root, "settings.json")).Should().Equal(settings);
    }

    [Fact]
    public async Task Backup_failure_blocks_download_and_apply_callbacks()
    {
        File.WriteAllText(Path.Combine(_root, "apps.json"), "original apps");
        File.WriteAllText(Path.Combine(_root, "settings.json"), "original settings");
        File.WriteAllText(Path.Combine(_root, "Backups"), "not a directory");
        var called = false;
        var download = () => LauncherUpdateBackup.BeforeUpdateAsync(() => { called = true; return Task.CompletedTask; }, _root);
        await download.Should().ThrowAsync<IOException>().WithMessage("*update stopped*");
        Action apply = () => LauncherUpdateBackup.BeforeUpdate(() => called = true, _root);
        apply.Should().Throw<IOException>();
        called.Should().BeFalse();
        File.ReadAllText(Path.Combine(_root, "apps.json")).Should().Be("original apps");
        File.ReadAllText(Path.Combine(_root, "settings.json")).Should().Be("original settings");
    }

    [Fact]
    public async Task Desktop_updater_blocks_both_staging_and_restart_when_backup_fails()
    {
        var previous = QuiverLauncherPaths.OverrideUserDataRoot;
        QuiverLauncherPaths.OverrideUserDataRoot = _root;
        try
        {
            File.WriteAllText(Path.Combine(_root, "Backups"), "not a directory");
            var updater = new VelopackUpdateService();
            // Backup must fail before Velopack is invoked, even in this unpackaged test host.
            var download = () => updater.DownloadUpdatesAsync(null!);
            await download.Should().ThrowAsync<IOException>().WithMessage("*update stopped*");
            Action apply = () => updater.ApplyUpdatesAndRestart(null!);
            apply.Should().Throw<IOException>().WithMessage("*update stopped*");
        }
        finally { QuiverLauncherPaths.OverrideUserDataRoot = previous; }
    }

    [Fact]
    public void Missing_new_profile_files_are_recorded_without_creating_empty_replacements()
    {
        var snapshot = LauncherUpdateBackup.Create(_root);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(snapshot, "manifest.json")));
        manifest.RootElement.GetProperty("files").GetProperty("apps.json").ValueKind.Should().Be(JsonValueKind.Null);
        manifest.RootElement.GetProperty("files").GetProperty("settings.json").ValueKind.Should().Be(JsonValueKind.Null);
        File.Exists(Path.Combine(_root, "apps.json")).Should().BeFalse();
        File.Exists(Path.Combine(_root, "settings.json")).Should().BeFalse();
    }

    [Fact]
    public void Update_backup_retention_bounds_count_and_size_while_keeping_newest()
    {
        var payload = new string('x', 3_000_000);
        string? newest = null;
        for (var i = 0; i < LauncherUpdateBackup.MaxBackupCount; i++)
        {
            File.WriteAllText(Path.Combine(_root, "apps.json"), payload + i);
            File.WriteAllText(Path.Combine(_root, "settings.json"), "settings" + i);
            newest = LauncherUpdateBackup.Create(_root);
        }
        var folders = Directory.GetDirectories(Path.Combine(_root, "Backups", "updates"));
        (folders.Length <= LauncherUpdateBackup.MaxBackupCount).Should().BeTrue();
        folders.Should().Contain(newest!);
        (folders.Sum(folder => Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length))
            <= LauncherUpdateBackup.MaxBackupBytes).Should().BeTrue();
    }

    [Theory]
    [InlineData("apps.json")]
    [InlineData("settings.json")]
    public void Linux_unreadable_source_blocks_update_and_preserves_originals(string name)
    {
        if (!OperatingSystem.IsLinux()) return;
        File.WriteAllText(Path.Combine(_root, "apps.json"), "original apps");
        File.WriteAllText(Path.Combine(_root, "settings.json"), "original settings");
        var path = Path.Combine(_root, name);
        var mode = File.GetUnixFileMode(path);
        var called = false;
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.None);
            Action apply = () => LauncherUpdateBackup.BeforeUpdate(() => called = true, _root);
            apply.Should().Throw<IOException>().WithMessage("*update stopped*");
            called.Should().BeFalse();
        }
        finally { File.SetUnixFileMode(path, mode); }
        File.ReadAllText(Path.Combine(_root, "apps.json")).Should().Be("original apps");
        File.ReadAllText(Path.Combine(_root, "settings.json")).Should().Be("original settings");
        LauncherUpdateBackup.BeforeUpdate(() => called = true, _root);
        called.Should().BeTrue();
    }

    [Theory]
    [InlineData("apps.json")]
    [InlineData("settings.json")]
    public void Locked_source_blocks_update_instead_of_saving_an_incomplete_pair(string lockedName)
    {
        if (!OperatingSystem.IsWindows()) return;
        File.WriteAllText(Path.Combine(_root, "apps.json"), "apps");
        File.WriteAllText(Path.Combine(_root, "settings.json"), "settings");
        using var locked = new FileStream(Path.Combine(_root, lockedName), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var called = false;
        Action apply = () => LauncherUpdateBackup.BeforeUpdate(() => called = true, _root);
        apply.Should().Throw<IOException>();
        called.Should().BeFalse();
        Directory.Exists(Path.Combine(_root, "Backups")).Should().BeFalse();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
