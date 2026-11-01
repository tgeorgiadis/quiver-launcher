namespace QuiverLauncher.Core.Models
{
    public class GitHubRelease
    {
        public string tag_name { get; set; } = string.Empty;
        public GitHubAsset[] assets { get; set; } = [];
        public bool prerelease { get; set; }
    }

    public class GitHubAsset
    {
        public string name { get; set; } = string.Empty;
        public string browser_download_url { get; set; } = string.Empty;
        /// <summary>GitHub's "sha256:" checksum of the file, when it publishes one.</summary>
        public string? digest { get; set; }
        /// <summary>When GitHub last had this file uploaded: a rolling release's new build replaces its files.</summary>
        public DateTimeOffset? updated_at { get; set; }
    }
}
