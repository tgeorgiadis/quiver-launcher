using System.Net;
using System.Text.Json;
using QuiverLauncher.Core.Models;

namespace QuiverLauncher.Core.Services
{
    /// <summary>One entry returned by the Kubo "ls" RPC call (a directory child).</summary>
    public readonly record struct IpfsLsEntry(string Name, string Hash, int Type);

    /// <summary>
    /// Reads "releases" for an app whose <c>repository</c> field holds an IPFS CID, using a local
    /// Kubo (go-ipfs) node's HTTP RPC API (see <see cref="IpfsSettings"/>, default
    /// <c>http://127.0.0.1:5001</c>). No network calls leave the machine other than to that node.
    ///
    /// Layout convention for the root CID:
    /// <list type="bullet">
    /// <item>If it is a single file, that file is the app's only asset (one synthetic release).</item>
    /// <item>If it is a directory whose children are files, all of them are one release's assets
    /// (one synthetic release, tagged with a short form of the CID).</item>
    /// <item>If it is a directory whose children are themselves directories, each child directory
    /// is treated as one release/version (named after the directory entry, e.g. "v1.2.0"), and the
    /// files directly inside it are that release's assets — mirroring GitHub/GitLab releases.</item>
    /// </list>
    /// Every asset's <see cref="GitHubAsset.browser_download_url"/> is stored as
    /// <c>ipfs://&lt;fileCid&gt;</c> (see <see cref="IpfsAssetUrl"/>); because IPFS is
    /// content-addressed, that CID alone is enough to fetch the file later, no path needed.
    /// </summary>
    public sealed class IpfsReleaseSource : IReleaseSource
    {
        // UnixFS "Type" values as returned by Kubo's /api/v0/ls.
        private const int UnixFsTypeDirectory = 1;
        private const int UnixFsTypeHamtShard = 5;

        public string Id => RepositorySourceIds.Ipfs;
        public string DisplayName => "IPFS (Local Node)";

        public async Task<GitHubReleaseFetchResult> FetchReleasesAsync(
            HttpClient httpClient,
            string repository,
            string? token = null,
            string? etag = null)
        {
            var cid = repository?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(cid))
            {
                return new GitHubReleaseFetchResult { StatusCode = HttpStatusCode.BadRequest };
            }

            IReadOnlyList<IpfsLsEntry> rootEntries;
            try
            {
                rootEntries = await LsAsync(httpClient, cid).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new HttpRequestException(
                    $"Could not reach the local IPFS node at {IpfsSettings.ApiBaseUrl} to read CID '{cid}'. " +
                    "Make sure a Kubo (go-ipfs) daemon is running and its API is reachable there. " + ex.Message,
                    ex);
            }

            if (rootEntries.Count == 0)
            {
                // Root CID has no directory children: treat it as a single file asset.
                var singleRelease = new GitHubRelease
                {
                    tag_name = ShortCid(cid),
                    prerelease = false,
                    assets =
                    [
                        new GitHubAsset { name = cid, browser_download_url = IpfsAssetUrl.Build(cid) }
                    ]
                };

                return new GitHubReleaseFetchResult
                {
                    StatusCode = HttpStatusCode.OK,
                    Releases = [singleRelease],
                    LatestTag = singleRelease.tag_name
                };
            }

            var childDirectories = rootEntries.Where(IsDirectory).ToList();

            if (childDirectories.Count == 0)
            {
                // Flat layout: every entry at the root is an asset of one synthetic release.
                var assets = rootEntries
                    .Select(entry => new GitHubAsset
                    {
                        name = entry.Name,
                        browser_download_url = IpfsAssetUrl.Build(entry.Hash)
                    })
                    .ToArray();

                if (assets.Length == 0)
                {
                    return new GitHubReleaseFetchResult { StatusCode = HttpStatusCode.OK, Releases = [] };
                }

                var release = new GitHubRelease
                {
                    tag_name = ShortCid(cid),
                    prerelease = false,
                    assets = assets
                };

                return new GitHubReleaseFetchResult
                {
                    StatusCode = HttpStatusCode.OK,
                    Releases = [release],
                    LatestTag = release.tag_name
                };
            }

            // Versioned layout: one release per subdirectory, newest name first.
            var releases = new List<GitHubRelease>();
            foreach (var versionDir in childDirectories.OrderByDescending(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                IReadOnlyList<IpfsLsEntry> versionEntries;
                try
                {
                    versionEntries = await LsAsync(httpClient, versionDir.Hash).ConfigureAwait(false);
                }
                catch (HttpRequestException)
                {
                    // Skip a version directory the node can't currently resolve (e.g. not pinned/available).
                    continue;
                }

                var assets = versionEntries
                    .Where(e => !IsDirectory(e))
                    .Select(entry => new GitHubAsset
                    {
                        name = entry.Name,
                        browser_download_url = IpfsAssetUrl.Build(entry.Hash)
                    })
                    .ToArray();

                if (assets.Length == 0)
                    continue;

                releases.Add(new GitHubRelease
                {
                    tag_name = versionDir.Name,
                    prerelease = false,
                    assets = assets
                });
            }

            return new GitHubReleaseFetchResult
            {
                StatusCode = HttpStatusCode.OK,
                Releases = releases,
                LatestTag = releases.Count > 0 ? releases[0].tag_name : null
            };
        }

        private static bool IsDirectory(IpfsLsEntry entry) =>
            entry.Type is UnixFsTypeDirectory or UnixFsTypeHamtShard;

        private static string ShortCid(string cid) => cid.Length <= 12 ? cid : cid[..12];

        internal static async Task<IReadOnlyList<IpfsLsEntry>> LsAsync(HttpClient httpClient, string cid)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, IpfsSettings.BuildLsUrl(cid));
            using var response = await httpClient.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return ParseLsResponse(json);
        }

        /// <summary>Parses a Kubo <c>/api/v0/ls</c> JSON response into its directory entries.</summary>
        public static IReadOnlyList<IpfsLsEntry> ParseLsResponse(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("Objects", out var objects) || objects.ValueKind != JsonValueKind.Array)
                return [];

            var entries = new List<IpfsLsEntry>();
            foreach (var obj in objects.EnumerateArray())
            {
                if (!obj.TryGetProperty("Links", out var links) || links.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var link in links.EnumerateArray())
                {
                    var name = link.TryGetProperty("Name", out var nameElement) ? nameElement.GetString() : null;
                    var hash = link.TryGetProperty("Hash", out var hashElement) ? hashElement.GetString() : null;
                    var type = link.TryGetProperty("Type", out var typeElement) &&
                               typeElement.ValueKind == JsonValueKind.Number
                        ? typeElement.GetInt32()
                        : -1;

                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(hash))
                        continue;

                    entries.Add(new IpfsLsEntry(name, hash, type));
                }
            }

            return entries;
        }
    }
}
