using System.Net;
using System.Text.Json;
using QuiverLauncher.Core.Models;

namespace QuiverLauncher.Core.Services;

/// <summary>Codeberg's Forgejo API uses GitHub compatible release and asset fields.</summary>
public sealed class CodebergReleaseSource : IReleaseSource
{
    public const string ApiBaseUrl = "https://codeberg.org/api/v1";
    public string Id => RepositorySourceIds.Codeberg;
    public string DisplayName => "Codeberg";

    public async Task<GitHubReleaseFetchResult> FetchReleasesAsync(HttpClient httpClient, string repository,
        string? token = null, string? etag = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repository) || !TrySplitRepository(repository, out var owner, out var name))
            return new() { StatusCode = HttpStatusCode.BadRequest };

        var endpoint = new Uri($"{ApiBaseUrl}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}/releases");
        return await ReleaseRequestCoordinator.For(httpClient).FetchAsync(httpClient, endpoint, "codeberg", token,
            MapReleasesFromJson, cancellationToken).ConfigureAwait(false);
    }

    public static List<GitHubRelease> MapReleasesFromJson(string json) =>
        JsonSerializer.Deserialize<List<GitHubRelease>>(json) ?? [];

    private static bool TrySplitRepository(string repository, out string owner, out string name)
    {
        var parts = repository.Trim().Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        owner = parts.ElementAtOrDefault(0) ?? string.Empty;
        name = parts.ElementAtOrDefault(1) ?? string.Empty;
        return parts.Length == 2 && owner.Length > 0 && name.Length > 0;
    }
}
