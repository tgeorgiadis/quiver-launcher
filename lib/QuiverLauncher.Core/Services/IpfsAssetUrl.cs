namespace QuiverLauncher.Core.Services
{
    /// <summary>
    /// IPFS assets don't have an HTTP download URL like GitHub/GitLab assets do — they are
    /// content-addressed by CID and fetched through a local node's RPC API. To keep reusing
    /// <see cref="Models.GitHubAsset.browser_download_url"/> unchanged, IPFS assets store a small
    /// pseudo-URL of the form <c>ipfs://&lt;cid&gt;</c> there instead of a real HTTP URL. The same
    /// scheme is reused for catalog (apps.json) locations published on IPFS.
    /// </summary>
    public static class IpfsAssetUrl
    {
        public const string Scheme = "ipfs://";

        public static string Build(string cid) => $"{Scheme}{cid.Trim()}";

        public static bool TryGetCid(string? assetUrl, out string cid)
        {
            cid = string.Empty;
            if (string.IsNullOrWhiteSpace(assetUrl))
                return false;

            if (!assetUrl.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
                return false;

            cid = assetUrl[Scheme.Length..].Trim();
            return cid.Length > 0;
        }
    }
}
