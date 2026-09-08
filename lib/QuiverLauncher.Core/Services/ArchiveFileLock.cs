namespace QuiverLauncher.Core.Services;

internal static class ArchiveFileLock
{
    internal const int SharingViolation = 32;
    internal const int LockViolation = 33;

    internal static bool IsSharingOrLockViolation(Exception ex)
    {
        if (ex is not IOException io)
            return false;

        var code = io.HResult & 0xFFFF;
        if (code == SharingViolation || code == LockViolation)
            return true;

        return io.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
               || io.Message.Contains("lock violation", StringComparison.OrdinalIgnoreCase);
    }

    internal static string FormatMessage(string path)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(name))
            name = path;

        return $"Could not open '{name}' because it is still locked by another process. " +
               "This often happens when Windows Defender or Search is scanning a newly downloaded archive. " +
               "Wait a moment and try again.";
    }

    internal static async Task<FileStream> OpenReadAsync(
        string path,
        int maxAttempts = 8,
        int initialDelayMs = 200,
        Func<int, Task>? delayAsync = null)
    {
        if (maxAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));

        IOException? last = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
            }
            catch (IOException ex) when (IsSharingOrLockViolation(ex))
            {
                last = ex;
                if (attempt == maxAttempts)
                    break;

                if (delayAsync != null)
                    await delayAsync(attempt).ConfigureAwait(false);
                else
                    await Task.Delay(initialDelayMs * attempt).ConfigureAwait(false);
            }
        }

        throw new IOException(FormatMessage(path), last);
    }
}
