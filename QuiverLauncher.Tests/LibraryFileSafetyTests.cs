using System.Text;
using System.Text.Json;
using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public sealed class LibraryFileSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "quiver-library-safety-" + Guid.NewGuid().ToString("N"));
    private readonly AppCatalogService _service;
    private string LibraryPath => Path.Combine(_root, "apps.json");
    private const string Original = "{\"apps\":[{\"name\":\"Original\",\"repository\":\"owner/original\",\"folderName\":\"Original\"}]}";

    public LibraryFileSafetyTests() => _service = new(dataDirectory: _root);

    [Theory]
    [InlineData("")]
    [InlineData("{\"apps\":[")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"apps\":{}}")]
    [InlineData("{\"apps\":[null]}")]
    [InlineData("{\"apps\":[{\"name\":\"Valid\",\"repository\":\"owner/valid\"},{\"name\":42}]}")]
    [InlineData("{\"apps\":[{\"name\":\"App\",\"tags\":[42]}]}")]
    [InlineData("{\"apps\":[{\"name\":\"App\",\"filesToAdd\":{}}]}")]
    [InlineData("{\"apps\":[{\"name\":\"App\",\"autoUpdate\":\"yes\"}]}")]
    [InlineData("{\"apps\":[{\"name\":\"App\",\"mods\":{\"sources\":[{}]}}]}")]
    public async Task Invalid_library_is_never_returned_as_empty_or_overwritten(string damaged)
    {
        await File.WriteAllTextAsync(LibraryPath, damaged);
        foreach (var read in new Func<Task>[] {
            () => _service.LoadLocalAppsAsync(),
            () => _service.LoadLocalAppsForMutationAsync(),
            () => _service.ValidateAndFixLocalAppsJsonAsync(),
            () => _service.SaveLocalAppsAsync([]),
            () => _service.ExportLocalAppsToFileAsync(LibraryPath, []) })
        {
            await read.Should().ThrowAsync<JsonException>();
            (await File.ReadAllTextAsync(LibraryPath)).Should().Be(damaged);
        }
        Action syncSave = () => _service.SaveLocalApps([]);
        syncSave.Should().Throw<JsonException>();
        Directory.Exists(_service.LibraryBackupDirectory).Should().BeFalse();
    }

    [Fact]
    public async Task Startup_preserves_exact_original_bytes_and_creates_a_recovery_copy()
    {
        // Preserve formatting, BOM, legacy structure, unknown fields, and duplicates.
        var json = "{ \"custom\": [{\"name\":\"A\",\"repository\":\"owner/a\"},{\"name\":\"A\",\"repository\":\"owner/a\"}], \"futureField\": 42 }";
        await File.WriteAllTextAsync(LibraryPath, json, new UTF8Encoding(true));
        var original = await File.ReadAllBytesAsync(LibraryPath);
        var timestamp = File.GetLastWriteTimeUtc(LibraryPath);
        await _service.ValidateAndFixLocalAppsJsonAsync();
        await new AppCatalogService(dataDirectory: _root).ValidateAndFixLocalAppsJsonAsync();
        (await File.ReadAllBytesAsync(LibraryPath)).Should().Equal(original);
        File.GetLastWriteTimeUtc(LibraryPath).Should().Be(timestamp);
        (await _service.LoadLocalAppsAsync()).Should().ContainSingle(); // Existing display deduplication; original bytes remain intact.
        var backup = Directory.GetFiles(_service.LibraryBackupDirectory, "*.json").Should().ContainSingle().Subject;
        (await File.ReadAllBytesAsync(backup)).Should().Equal(original);
    }

    [Fact]
    public async Task Every_changed_save_keeps_previous_and_new_snapshots_even_after_empty_saves()
    {
        await File.WriteAllTextAsync(LibraryPath, Original);
        await _service.SaveLocalAppsAsync([new GameInfo { Name = "Replacement", Repository = "owner/replacement" }]);
        var replacement = await File.ReadAllBytesAsync(LibraryPath);
        _service.SaveLocalApps([]); // Deliberate removal of the final app remains supported.
        for (var i = 0; i < 12; i++) await _service.SaveLocalAppsAsync([]);
        var backups = Directory.GetFiles(_service.LibraryBackupDirectory, "*.json");
        backups.Should().HaveCount(3);
        backups.Select(File.ReadAllText).Should().Contain(Original);
        backups.Should().Contain(file => File.ReadAllBytes(file).SequenceEqual(replacement));
        (await _service.LoadLocalAppsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task First_save_is_backed_up_and_can_be_restored_after_corruption()
    {
        await _service.SaveLocalAppsAsync([new GameInfo { Name = "First", Repository = "owner/first" }]);
        var backup = Directory.GetFiles(_service.LibraryBackupDirectory, "*.json").Single();
        var good = await File.ReadAllBytesAsync(LibraryPath);
        (await File.ReadAllBytesAsync(backup)).Should().Equal(good);
        await File.WriteAllTextAsync(LibraryPath, "{broken");
        var read = () => new AppCatalogService(dataDirectory: _root).LoadLocalAppsAsync();
        await read.Should().ThrowAsync<JsonException>().WithMessage("*No library data has been overwritten*Backups*apps*");
        (await File.ReadAllTextAsync(LibraryPath)).Should().Be("{broken");
        (await File.ReadAllBytesAsync(backup)).Should().Equal(good);
        File.Copy(backup, LibraryPath, overwrite: true); // User restores while Quiver is closed.
        (await _service.LoadLocalAppsAsync()).Single().Repository.Should().Be("owner/first");
    }

    [Fact]
    public async Task Missing_existing_library_is_not_recreated_even_after_restart_or_with_legacy_file()
    {
        await File.WriteAllTextAsync(LibraryPath, Original);
        await _service.LoadLocalAppsAsync();
        await File.WriteAllTextAsync(Path.Combine(_root, "games.json"), "{\"apps\":[]}");
        File.Delete(LibraryPath);
        var restarted = new AppCatalogService(dataDirectory: _root);
        var read = () => restarted.LoadLocalAppsAsync();
        var save = () => restarted.SaveLocalAppsAsync([]);
        await read.Should().ThrowAsync<IOException>().WithMessage("*restore apps.json*");
        await save.Should().ThrowAsync<IOException>();
        File.Exists(LibraryPath).Should().BeFalse();
        Directory.GetFiles(_service.LibraryBackupDirectory, "*.json").Select(File.ReadAllText).Should().Contain(Original);
    }

    [Fact]
    public async Task Fresh_library_can_load_and_save_without_read_side_effects()
    {
        (await _service.LoadLocalAppsAsync()).Should().BeEmpty();
        File.Exists(LibraryPath).Should().BeFalse();
        await _service.SaveLocalAppsAsync([new GameInfo { Name = "New", Repository = "owner/new" }]);
        (await _service.LoadLocalAppsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Library_backup_retention_bounds_count_and_size_while_keeping_newest()
    {
        await _service.SaveLocalAppsAsync([new GameInfo { Name = "Current", Repository = "owner/current" }]);
        var newest = Directory.GetFiles(_service.LibraryBackupDirectory, "*.json").Single();
        for (var i = 0; i < LibraryFileStore.MaxBackupCount + 5; i++)
        {
            var bytes = Encoding.UTF8.GetBytes($"{{\"apps\":[{{\"name\":\"Backup {i}\"}}]}} ");
            var path = Path.Combine(_service.LibraryBackupDirectory, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) + "-" + i + ".json");
            await File.WriteAllBytesAsync(path, bytes);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-i - 1));
        }
        await _service.LoadLocalAppsAsync();
        var backups = Directory.GetFiles(_service.LibraryBackupDirectory, "*.json");
        (backups.Length <= LibraryFileStore.MaxBackupCount).Should().BeTrue();
        backups.Should().Contain(newest);
        (backups.Sum(path => new FileInfo(path).Length) <= LibraryFileStore.MaxBackupBytes).Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_legacy_library_blocks_migration_and_direct_saves(bool directSave)
    {
        var legacy = Path.Combine(_root, "games.json");
        await File.WriteAllTextAsync(legacy, "{\"custom\":[{\"name\":42}]}");
        Func<Task> operation = directSave ? () => _service.SaveLocalAppsAsync([]) : () => _service.LoadLocalAppsAsync();
        await operation.Should().ThrowAsync<JsonException>();
        File.Exists(LibraryPath).Should().BeFalse();
        (await File.ReadAllTextAsync(legacy)).Should().Be("{\"custom\":[{\"name\":42}]}");
    }

    [Fact]
    public async Task Migration_preserves_legacy_bytes_in_both_library_and_backup()
    {
        var legacy = Path.Combine(_root, "games.json");
        var bytes = Encoding.UTF8.GetBytes(Original.Replace("apps", "custom"));
        await File.WriteAllBytesAsync(legacy, bytes);
        (await _service.LoadLocalAppsAsync()).Should().ContainSingle();
        (await File.ReadAllBytesAsync(LibraryPath)).Should().Equal(bytes);
        (await File.ReadAllBytesAsync(legacy)).Should().Equal(bytes);
        (await File.ReadAllBytesAsync(Directory.GetFiles(_service.LibraryBackupDirectory, "*.json").Single())).Should().Equal(bytes);
    }

    [Fact]
    public async Task Backup_failure_prevents_overwrite()
    {
        await File.WriteAllTextAsync(LibraryPath, Original);
        await File.WriteAllTextAsync(Path.Combine(_root, "Backups"), "blocks backup directory creation");
        var save = () => _service.SaveLocalAppsAsync([]);
        await save.Should().ThrowAsync<IOException>();
        (await File.ReadAllTextAsync(LibraryPath)).Should().Be(Original);
    }

    [Fact]
    public async Task Damaged_backup_is_not_overwritten_and_blocks_save()
    {
        await File.WriteAllTextAsync(LibraryPath, Original);
        await _service.LoadLocalAppsAsync();
        var backup = Directory.GetFiles(_service.LibraryBackupDirectory, "*.json").Single();
        await File.WriteAllTextAsync(backup, "{damaged backup");
        var save = () => _service.SaveLocalAppsAsync([]);
        await save.Should().ThrowAsync<IOException>();
        (await File.ReadAllTextAsync(LibraryPath)).Should().Be(Original);
        (await File.ReadAllTextAsync(backup)).Should().Be("{damaged backup");
    }

    [Fact]
    public async Task Windows_read_failure_preserves_library_and_retry_succeeds()
    {
        if (!OperatingSystem.IsWindows()) return;
        await File.WriteAllTextAsync(LibraryPath, Original);
        using (var locked = new FileStream(LibraryPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var startup = () => _service.ValidateAndFixLocalAppsJsonAsync();
            var save = () => _service.SaveLocalAppsAsync([]);
            await startup.Should().ThrowAsync<IOException>();
            await save.Should().ThrowAsync<IOException>();
        }
        (await File.ReadAllTextAsync(LibraryPath)).Should().Be(Original);
        (await _service.LoadLocalAppsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Linux_permission_failures_never_replace_library_and_allow_retry()
    {
        if (!OperatingSystem.IsLinux()) return;
        await File.WriteAllTextAsync(LibraryPath, Original);
        var mode = File.GetUnixFileMode(LibraryPath);
        try
        {
            File.SetUnixFileMode(LibraryPath, UnixFileMode.None);
            var read = () => _service.ValidateAndFixLocalAppsJsonAsync();
            var save = () => _service.SaveLocalAppsAsync([]);
            await read.Should().ThrowAsync<UnauthorizedAccessException>();
            await save.Should().ThrowAsync<UnauthorizedAccessException>();
        }
        finally { File.SetUnixFileMode(LibraryPath, mode); }
        (await File.ReadAllTextAsync(LibraryPath)).Should().Be(Original);
        (await _service.LoadLocalAppsAsync()).Should().ContainSingle();

        var backupMode = File.GetUnixFileMode(_service.LibraryBackupDirectory);
        try
        {
            File.SetUnixFileMode(_service.LibraryBackupDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            var save = () => _service.SaveLocalAppsAsync([]);
            await save.Should().ThrowAsync<UnauthorizedAccessException>();
            (await File.ReadAllTextAsync(LibraryPath)).Should().Be(Original);
        }
        finally { File.SetUnixFileMode(_service.LibraryBackupDirectory, backupMode); }
        await _service.SaveLocalAppsAsync([]);
        Directory.GetFiles(_service.LibraryBackupDirectory, "*.json").Select(File.ReadAllText).Should().Contain(Original);
    }

    [Fact]
    public async Task Non_file_library_path_is_not_treated_as_a_new_library()
    {
        Directory.CreateDirectory(LibraryPath);
        var read = () => _service.LoadLocalAppsAsync();
        var save = () => _service.SaveLocalAppsAsync([]);
        await read.Should().ThrowAsync<UnauthorizedAccessException>();
        await save.Should().ThrowAsync<UnauthorizedAccessException>();
        Directory.Exists(_service.LibraryBackupDirectory).Should().BeFalse();
    }

    [Fact]
    public async Task Concurrent_service_saves_produce_complete_files_and_keep_every_snapshot()
    {
        await File.WriteAllTextAsync(LibraryPath, Original);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            new AppCatalogService(dataDirectory: _root).SaveLocalAppsAsync(
                [new GameInfo { Name = "App " + i, Repository = "owner/app" + i }])));
        (await _service.LoadLocalAppsAsync()).Should().ContainSingle();
        var backups = Directory.GetFiles(_service.LibraryBackupDirectory, "*.json");
        backups.Should().HaveCount(9);
        foreach (var backup in backups)
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(backup));
            document.RootElement.GetProperty("apps").GetArrayLength().Should().Be(1);
        }
        Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
    }

    public void Dispose()
    {
        // The test owns this randomly named directory under the OS temp root.
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
