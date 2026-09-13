using System.Collections.Concurrent;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace QuiverLauncher.Core.Services;

/// <summary>Public browsing evidence, never credentials or download authorization.</summary>
public static class PublishedPlatformCache
{
    private sealed record Stored(string Url, string? ETag, DateTimeOffset? Modified, PublishedPlatformDocument Document);
    private sealed class Session
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public readonly Dictionary<string, DateTimeOffset> Checked = [];
    }
    private static ConditionalWeakTable<HttpClient, Session> Sessions = new();
    private static readonly ConcurrentDictionary<string, Stored> Documents = new();
    private static readonly ConcurrentDictionary<string, CatalogPlatformEntry> Entries = new();
    private static readonly ConcurrentDictionary<string, string> Errors = new();
    private static string? _directory;
    public static string? Error(string? url) => url == null ? null : Errors.GetValueOrDefault(url);
    public static bool TryGet(string provider, string repository, string? preferred, out CatalogPlatformEntry? entry) =>
        Entries.TryGetValue(CatalogPlatformIndex.Key(provider, repository, preferred), out entry);

    public static void Initialize(string directory)
    {
        Documents.Clear(); Entries.Clear(); Errors.Clear(); Sessions = new();
        _directory = Path.Combine(directory, "published-platforms");
        if (!Directory.Exists(_directory)) return;
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            try
            {
                var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(file), PublishedPlatformDocument.JsonOptions);
                if (stored == null) continue;
                PublishedPlatformDocument.Parse(JsonSerializer.Serialize(stored.Document, PublishedPlatformDocument.JsonOptions));
                Apply(stored);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
        }
    }

    private static void Apply(Stored stored)
    {
        Documents[stored.Url] = stored;
        foreach (var item in stored.Document.Entries)
            Entries.AddOrUpdate(item.Key, item.Metadata, (_, old) => old.ValidatedAt > item.ValidatedAt ? old : item.Metadata);
    }

    public static async Task RefreshAsync(HttpClient client, string? url, bool force = false, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo))
        { Errors[url] = "The shared platform metadata URL is invalid."; return; }
        var session = Sessions.GetOrCreateValue(client);
        await session.Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!force && session.Checked.TryGetValue(url, out var checkedAt) && DateTimeOffset.UtcNow - checkedAt < TimeSpan.FromMinutes(15)) return;
            session.Checked[url] = DateTimeOffset.UtcNow;
            Documents.TryGetValue(url, out var cached);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (cached?.ETag != null) request.Headers.TryAddWithoutValidation("If-None-Match", cached.ETag);
            else if (cached?.Modified != null) request.Headers.IfModifiedSince = cached.Modified;
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotModified && cached != null) { Errors.TryRemove(url, out _); return; }
            response.EnsureSuccessStatusCode();
            // Bound both declared and streamed size before parsing untrusted remote JSON.
            const int maxBytes = 16 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > maxBytes) throw new JsonException();
            await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                if (output.Length + read > maxBytes) throw new JsonException();
                output.Write(buffer, 0, read);
            }
            var document = PublishedPlatformDocument.Parse(Encoding.UTF8.GetString(output.ToArray()));
            if (document.GeneratedAt > DateTimeOffset.UtcNow.AddMinutes(5)) throw new JsonException();
            var stored = new Stored(url, response.Headers.ETag?.ToString(), response.Content.Headers.LastModified, document);
            Apply(stored);
            if (_directory != null)
            {
                Directory.CreateDirectory(_directory);
                var path = Path.Combine(_directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ".json");
                var temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(stored, PublishedPlatformDocument.JsonOptions));
                File.Move(temp, path, true);
            }
            Errors.TryRemove(url, out _);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { session.Checked.Remove(url); throw; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or UnauthorizedAccessException or OperationCanceledException)
        { Errors[url] = "Shared platform metadata could not be refreshed. Previously verified platforms remain available."; }
        finally { session.Gate.Release(); }
    }
}
