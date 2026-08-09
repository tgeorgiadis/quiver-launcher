using System.Text.Json.Serialization;

namespace QuiverLauncher.Services.Mods.Providers.Thunderstore;

public sealed class ThunderstoreListingPageDto
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("next")]
    public string? Next { get; set; }

    [JsonPropertyName("previous")]
    public string? Previous { get; set; }

    [JsonPropertyName("results")]
    public List<ThunderstoreListingPackageDto> Results { get; set; } = [];
}

public sealed class ThunderstoreListingPackageDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("namespace")]
    public string Namespace { get; set; } = string.Empty;

    [JsonPropertyName("community_identifier")]
    public string? CommunityIdentifier { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("download_count")]
    public long DownloadCount { get; set; }

    [JsonPropertyName("icon_url")]
    public string? IconUrl { get; set; }

    [JsonPropertyName("is_deprecated")]
    public bool IsDeprecated { get; set; }

    [JsonPropertyName("is_nsfw")]
    public bool IsNsfw { get; set; }

    [JsonPropertyName("is_pinned")]
    public bool IsPinned { get; set; }

    [JsonPropertyName("rating_count")]
    public int RatingCount { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("last_updated")]
    public DateTimeOffset? LastUpdated { get; set; }

    [JsonPropertyName("datetime_created")]
    public DateTimeOffset? DateTimeCreated { get; set; }
}

public sealed class ThunderstoreCommunityFiltersResponseDto
{
    [JsonPropertyName("sections")]
    public List<ThunderstoreCommunityFilterDto> Sections { get; set; } = [];
}

public sealed class ThunderstoreCommunityFilterDto
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("priority")]
    public int Priority { get; set; }
}

public sealed class ThunderstoreExperimentalPackageDto
{
    [JsonPropertyName("namespace")]
    public string Namespace { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("owner")]
    public string Owner { get; set; } = string.Empty;

    [JsonPropertyName("package_url")]
    public string? PackageUrl { get; set; }

    [JsonPropertyName("is_deprecated")]
    public bool IsDeprecated { get; set; }

    [JsonPropertyName("latest")]
    public ThunderstoreExperimentalPackageVersionDto? Latest { get; set; }
}

public sealed class ThunderstoreExperimentalPackageVersionDto
{
    [JsonPropertyName("version_number")]
    public string VersionNumber { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; set; } = [];

    [JsonPropertyName("download_url")]
    public string DownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("downloads")]
    public long Downloads { get; set; }

    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; set; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;
}
