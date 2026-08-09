namespace QuiverLauncher.Services.Mods;

/// <summary>
/// Downloads mod archives to a temp file so large packages are not buffered in a MemoryStream
/// (which fails near 2 GB with "Stream was too long").
/// </summary>
public static class ModArchiveDownload
{
    const int BufferSize = 81920;

    public static async Task<FileStream> DownloadToTempFileAsync(
        HttpClient httpClient,
        string url,
        long totalHint = 0,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Download URL is required.", nameof(url));

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? totalHint;
        return await CopyContentToTempFileAsync(response.Content, total, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<FileStream> CopyContentToTempFileAsync(
        HttpContent content,
        long totalHint = 0,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var total = content.Headers.ContentLength ?? totalHint;
        var tempDir = Path.Combine(Path.GetTempPath(), "QuiverMods");
        Directory.CreateDirectory(tempDir);
        var tempPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}.mod");

        try
        {
            await using (var writeStream = new FileStream(
                               tempPath,
                               FileMode.CreateNew,
                               FileAccess.Write,
                               FileShare.None,
                               BufferSize,
                               FileOptions.Asynchronous))
            await using (var contentStream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            {
                var buffer = new byte[BufferSize];
                long readTotal = 0;
                int read;
                while ((read = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                           .ConfigureAwait(false)) > 0)
                {
                    await writeStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    readTotal += read;
                    if (progress != null && total > 0)
                        progress.Report(Math.Clamp(readTotal / (double)total, 0, 1));
                }
            }

            progress?.Report(1);

            return new FileStream(
                tempPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Best-effort cleanup of a partial download.
            }

            throw;
        }
    }
}
