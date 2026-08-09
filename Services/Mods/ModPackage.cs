namespace QuiverLauncher.Services.Mods;

public sealed class ModPackage
{
    public required string ProviderId { get; init; }
    public required string SourceKey { get; init; }
    public required string Id { get; init; }
    public required string Owner { get; init; }
    public required string Name { get; init; }
    public required string FullName { get; init; }
    public string Description { get; init; } = string.Empty;
    public string? IconUrl { get; init; }
    public string? PackagePageUrl { get; init; }
    public bool IsDeprecated { get; init; }

    /// <summary>True when the host marks the mod as content-rated / NSFW.</summary>
    public bool HasContentRating { get; init; }

    /// <summary>Total download count across versions (Thunderstore) or host downloads (GameBanana).</summary>
    public long DownloadCount { get; init; }

    /// <summary>Thunderstore rating score, or GameBanana like count for the card thumbs stat.</summary>
    public int RatingScore { get; init; }

    /// <summary>UTC unix seconds of last update, when known.</summary>
    public long? UpdatedAtUnix { get; init; }

    /// <summary>UTC unix seconds of first publish / date added, when known.</summary>
    public long? CreatedAtUnix { get; init; }

    public ModPackageVersion? LatestVersion { get; init; }

    /// <summary>Optional multi-file downloads (GameBanana). Empty for single-URL providers.</summary>
    public IReadOnlyList<ModDownloadFile> DownloadFiles { get; init; } = [];

    public string SourceDisplayLabel { get; init; } = string.Empty;
}
