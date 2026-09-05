namespace QuiverLauncher.Services;

using QuiverLauncher.Core.Services;

public interface ICatalogLocationReader
{
    Task<string> ReadAsync(HttpClient httpClient, string location, CancellationToken cancellationToken = default);
}

public sealed class CatalogLocationReader : ICatalogLocationReader
{
    public static ICatalogLocationReader Default { get; } = new CatalogLocationReader();

    public async Task<string> ReadAsync(HttpClient httpClient, string location, CancellationToken cancellationToken = default)
    {
        if (IpfsAssetUrl.TryGetCid(location, out var cid))
        {
            // apps.json (or a catalog list file) published on IPFS: fetch its raw bytes from the
            // local Kubo node's "cat" RPC endpoint (POST), same mechanism as IPFS game assets.
            using var request = new HttpRequestMessage(HttpMethod.Post, IpfsSettings.BuildCatUrl(cid));
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        if (AppCatalogService.IsRemoteLocation(location))
        {
            var response = await httpClient.GetAsync(location, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        var resolvedPath = AppCatalogService.ResolveLocalPath(location);
        if (!File.Exists(resolvedPath))
            throw new FileNotFoundException($"Catalog file not found: {location}");

        return await File.ReadAllTextAsync(resolvedPath, cancellationToken).ConfigureAwait(false);
    }
}
