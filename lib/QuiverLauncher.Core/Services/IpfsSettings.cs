namespace QuiverLauncher.Core.Services
{
    /// <summary>
    /// Configuration for talking to a local Kubo (go-ipfs) node's HTTP RPC API.
    /// Defaults to the standard local API address (<c>http://127.0.0.1:5001</c>).
    /// </summary>
    public static class IpfsSettings
    {
        public const string DefaultApiBaseUrl = "http://127.0.0.1:5001";

        /// <summary>
        /// Environment variable checked when no explicit <see cref="ApiBaseUrl"/> has been set in-process.
        /// Lets users override the node address without touching settings.json.
        /// </summary>
        public const string ApiUrlEnvironmentVariable = "QUIVERLAUNCHER_IPFS_API_URL";

        private static string? _apiBaseUrl;

        /// <summary>
        /// Base URL of the local Kubo RPC API (no trailing slash), e.g. <c>http://127.0.0.1:5001</c>.
        /// Set explicitly (e.g. from AppSettings.IpfsApiUrl) to override the environment variable/default.
        /// </summary>
        public static string ApiBaseUrl
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_apiBaseUrl))
                    return _apiBaseUrl!;

                var fromEnvironment = Environment.GetEnvironmentVariable(ApiUrlEnvironmentVariable);
                return string.IsNullOrWhiteSpace(fromEnvironment)
                    ? DefaultApiBaseUrl
                    : fromEnvironment.Trim().TrimEnd('/');
            }
            set => _apiBaseUrl = string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('/');
        }

        public static string BuildApiUrl(string path) => $"{ApiBaseUrl}{path}";

        /// <summary>Kubo RPC "ls" endpoint: lists the direct children of a CID (directory listing).</summary>
        public static string BuildLsUrl(string cid) =>
            BuildApiUrl($"/api/v0/ls?arg={Uri.EscapeDataString(cid)}");

        /// <summary>Kubo RPC "cat" endpoint: streams the raw bytes of a file CID.</summary>
        public static string BuildCatUrl(string cid) =>
            BuildApiUrl($"/api/v0/cat?arg={Uri.EscapeDataString(cid)}");
    }
}
