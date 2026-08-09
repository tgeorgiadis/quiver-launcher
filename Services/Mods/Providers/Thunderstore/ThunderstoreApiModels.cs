using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuiverLauncher.Services.Mods.Providers.Thunderstore;

public sealed class ThunderstorePackageDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("owner")]
    public string Owner { get; set; } = string.Empty;

    [JsonPropertyName("package_url")]
    public string? PackageUrl { get; set; }

    [JsonPropertyName("uuid4")]
    public string Uuid4 { get; set; } = string.Empty;

    [JsonPropertyName("is_deprecated")]
    public bool IsDeprecated { get; set; }

    [JsonPropertyName("has_nsfw_content")]
    public bool HasNsfwContent { get; set; }

    [JsonPropertyName("rating_score")]
    public int RatingScore { get; set; }

    [JsonPropertyName("date_updated")]
    public DateTimeOffset? DateUpdated { get; set; }

    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; set; }

    [JsonPropertyName("versions")]
    public List<ThunderstorePackageVersionDto> Versions { get; set; } = [];
}

public sealed class ThunderstorePackageVersionDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("version_number")]
    public string VersionNumber { get; set; } = string.Empty;

    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; set; } = [];

    [JsonPropertyName("download_url")]
    public string DownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("downloads")]
    public long Downloads { get; set; }

    [JsonPropertyName("date_created")]
    public DateTimeOffset? DateCreated { get; set; }

    [JsonPropertyName("file_size")]
    public long FileSize { get; set; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;
}

internal static class ThunderstoreJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
