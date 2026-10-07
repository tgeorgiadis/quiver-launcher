using System.Net.Http;
using System.Net.Http.Headers;
using Avalonia.Media.Imaging;

namespace QuiverLauncher.Services;

/// <summary>Only publishes decodable images; failed downloads remain retryable.</summary>
public static class LauncherIconCache
{
    public static bool IsValid(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var bitmap = new Bitmap(stream);
            return bitmap.PixelSize.Width > 0 && bitmap.PixelSize.Height > 0;
        }
        catch { return false; }
    }

    /// <summary>Reads only the start of the file. Start-up checks every cached cover, and decoding each one
    /// in full made memory use spike with a large library. Files only reach the cache after decoding in
    /// full, so this just rules out a missing, empty or non-image file.</summary>
    public static bool HasImageHeader(string path)
    {
        try
        {
            Span<byte> head = stackalloc byte[12];
            using var stream = File.OpenRead(path);
            var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            head = head[..read];
            return head.StartsWith((byte[])[0x89, (byte)'P', (byte)'N', (byte)'G'])
                || head.StartsWith((byte[])[0xFF, 0xD8, 0xFF])
                || head.StartsWith("GIF8"u8)
                || head.StartsWith("BM"u8)
                || head.StartsWith((byte[])[0, 0, 1, 0])
                || read == 12 && head.StartsWith("RIFF"u8) && head[8..].SequenceEqual("WEBP"u8);
        }
        catch { return false; }
    }

    public static async Task FetchAsync(HttpClient client, string url, string path, string? token, CancellationToken cancellation)
    {
        var uri = new Uri(url);
        // GitHub's file viewer returns HTML, not an image.
        if (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.Contains("/blob/"))
            uri = new Uri("https://raw.githubusercontent.com" + uri.AbsolutePath.Replace("/blob/", "/"));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("Quiver-Launcher/1.0");
        request.Headers.Accept.ParseAdd("image/webp,image/*;q=0.8");
        if (!string.IsNullOrWhiteSpace(token) && uri.Scheme == "https" &&
            (uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        using var response = await client.SendAsync(request, cancellation);
        response.EnsureSuccessStatusCode();
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, await response.Content.ReadAsByteArrayAsync(cancellation), cancellation);
            if (!IsValid(temporary)) throw new InvalidDataException("The icon response is not a supported image.");
            cancellation.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
