using System.Security.Cryptography;

namespace QuiverLauncher.Services;

/// <summary>
/// Fail-closed storage for the user's library. Backups are immutable and deduplicated by
/// content; old snapshots are bounded by count and size.
/// </summary>
internal sealed class LibraryFileStore(string path, Action<byte[]> validate)
{
    public const int MaxBackupCount = 50;
    public const long MaxBackupBytes = 25L * 1024 * 1024;
    public string BackupDirectory { get; } = Path.Combine(Path.GetDirectoryName(path)!, "Backups", "apps");
    private bool _hasSeenLibrary;

    public async Task<byte[]?> ReadAsync(string legacyPath)
    {
        using var lease = await AcquireAsync().ConfigureAwait(false);
        var bytes = await ReadCurrentAsync().ConfigureAwait(false);
        if (bytes != null)
        {
            validate(bytes);
            await BackupAsync(bytes).ConfigureAwait(false);
            await PruneBackupsAsync().ConfigureAwait(false);
            return bytes;
        }

        // Only migrate on a genuinely new library. Never substitute a legacy copy
        // when a previously saved apps.json has disappeared.
        try { bytes = await File.ReadAllBytesAsync(legacyPath).ConfigureAwait(false); }
        catch (FileNotFoundException) { return null; }
        validate(bytes);
        await BackupAsync(bytes).ConfigureAwait(false);
        await WriteAtomicAsync(path, bytes, replace: false).ConfigureAwait(false);
        await PruneBackupsAsync().ConfigureAwait(false);
        _hasSeenLibrary = true;
        return bytes;
    }

    public async Task SaveAsync(byte[] bytes)
    {
        validate(bytes);
        using var lease = await AcquireAsync().ConfigureAwait(false);
        var previous = await ReadCurrentAsync().ConfigureAwait(false);
        if (previous != null)
        {
            // Even callers that did not load first cannot overwrite a damaged file.
            validate(previous);
            await BackupAsync(previous).ConfigureAwait(false);
            if (previous.AsSpan().SequenceEqual(bytes))
            {
                await PruneBackupsAsync().ConfigureAwait(false);
                return;
            }
        }
        else
        {
            // A direct save must not bypass validation or preservation of legacy data.
            var legacyPath = Path.Combine(Path.GetDirectoryName(path)!, "games.json");
            byte[]? legacy = null;
            try { legacy = await File.ReadAllBytesAsync(legacyPath).ConfigureAwait(false); }
            catch (FileNotFoundException) { }
            if (legacy != null)
            {
                validate(legacy);
                await BackupAsync(legacy).ConfigureAwait(false);
            }
        }

        // Commit the backup first. If backup creation or disk flushing fails, leave
        // the original untouched. Back up the first save as well as replacements.
        await BackupAsync(bytes).ConfigureAwait(false);
        await WriteAtomicAsync(path, bytes, replace: previous != null).ConfigureAwait(false);
        _hasSeenLibrary = true;
        await PruneBackupsAsync().ConfigureAwait(false);
    }

    private async Task<byte[]?> ReadCurrentAsync()
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            _hasSeenLibrary = true;
            return bytes;
        }
        catch (FileNotFoundException)
        {
            // File.Exists hides access errors. Only an actual FileNotFoundException
            // can mean a new library; persisted backups distinguish it after restart.
            if (_hasSeenLibrary || HasRecoveryFiles())
                throw new IOException($"The library file is missing: {path}. Quiver will not replace it. Close Quiver and restore apps.json from {BackupDirectory}.");
            return null;
        }
    }

    private bool HasRecoveryFiles()
    {
        try { return Directory.EnumerateFileSystemEntries(BackupDirectory).Any(); }
        catch (DirectoryNotFoundException) { return false; }
        // Permission and I/O errors must propagate, not masquerade as a new library.
    }

    private async Task BackupAsync(byte[] bytes)
    {
        Directory.CreateDirectory(BackupDirectory);
        var backup = Path.Combine(BackupDirectory, Convert.ToHexString(SHA256.HashData(bytes)) + ".json");
        try
        {
            var existing = await File.ReadAllBytesAsync(backup).ConfigureAwait(false);
            if (!existing.AsSpan().SequenceEqual(bytes))
                throw new IOException($"A library backup is damaged: {backup}. The library has not been overwritten.");
            return;
        }
        catch (FileNotFoundException) { }
        await WriteAtomicAsync(backup, bytes, replace: false).ConfigureAwait(false);
    }

    private Task PruneBackupsAsync()
    {
        try
        {
            var files = new DirectoryInfo(BackupDirectory).EnumerateFiles("*.json")
                .OrderBy(file => file.LastWriteTimeUtc)
                .ThenBy(file => file.Name, StringComparer.Ordinal)
                .ToList();
            var total = files.Sum(file => file.Length);
            var newest = files.LastOrDefault();
            foreach (var file in files.ToList())
            {
                if (file == newest || (files.Count <= MaxBackupCount && total <= MaxBackupBytes)) break;
                try
                {
                    var size = file.Length;
                    file.Delete();
                    files.Remove(file);
                    total -= size;
                }
                catch (IOException) { break; }
                catch (UnauthorizedAccessException) { break; }
            }
        }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return Task.CompletedTask;
    }

    private async Task<FileStream> AcquireAsync()
    {
        // Shared by all service instances/processes. Do not delete the lock file:
        // deleting it can let another process lock a different file at the same path.
        for (var attempt = 0; ; attempt++)
        {
            try { return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 50) { await Task.Delay(100).ConfigureAwait(false); }
        }
    }

    private static async Task WriteAtomicAsync(string destination, byte[] bytes, bool replace)
    {
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                65536, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(bytes).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            if (replace) File.Replace(temporary, destination, null);
            else File.Move(temporary, destination); // Never overwrite a file that appeared concurrently.
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
