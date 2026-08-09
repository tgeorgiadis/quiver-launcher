using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuiverLauncher.Services.Mods.Providers.GameBanana;

internal static class GameBananaJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

internal sealed class GameBananaIndexResponse
{
    [JsonPropertyName("_aMetadata")]
    public GameBananaIndexMetadata? Metadata { get; set; }

    [JsonPropertyName("_aRecords")]
    public List<GameBananaIndexRecord> Records { get; set; } = [];
}

internal sealed class GameBananaIndexMetadata
{
    [JsonPropertyName("_nRecordCount")]
    public int RecordCount { get; set; }

    [JsonPropertyName("_bIsComplete")]
    public bool IsComplete { get; set; }

    [JsonPropertyName("_nPerpage")]
    public int PerPage { get; set; }
}

internal sealed class GameBananaIndexRecord
{
    [JsonPropertyName("_idRow")]
    public long IdRow { get; set; }

    [JsonPropertyName("_sModelName")]
    public string? ModelName { get; set; }

    [JsonPropertyName("_sName")]
    public string? Name { get; set; }

    [JsonPropertyName("_sProfileUrl")]
    public string? ProfileUrl { get; set; }

    [JsonPropertyName("_sPayType")]
    public string? PayType { get; set; }

    [JsonPropertyName("_sVersion")]
    public string? Version { get; set; }

    [JsonPropertyName("_bIsObsolete")]
    public bool IsObsolete { get; set; }

    [JsonPropertyName("_bHasContentRatings")]
    public bool HasContentRatings { get; set; }

    [JsonPropertyName("_bHasFiles")]
    public bool HasFiles { get; set; }

    [JsonPropertyName("_nLikeCount")]
    public int LikeCount { get; set; }

    [JsonPropertyName("_nViewCount")]
    public long ViewCount { get; set; }

    [JsonPropertyName("_nDownloadCount")]
    public long DownloadCount { get; set; }

    [JsonPropertyName("_tsDateAdded")]
    public long DateAddedUnix { get; set; }

    [JsonPropertyName("_tsDateModified")]
    public long DateModifiedUnix { get; set; }

    [JsonPropertyName("_aSubmitter")]
    public GameBananaSubmitter? Submitter { get; set; }

    [JsonPropertyName("_aPreviewContent")]
    public GameBananaPreviewContent? PreviewContent { get; set; }
}

internal sealed class GameBananaSubmitter
{
    [JsonPropertyName("_sName")]
    public string? Name { get; set; }
}

internal sealed class GameBananaPreviewContent
{
    [JsonPropertyName("screenshot")]
    public GameBananaScreenshot? Screenshot { get; set; }
}

internal sealed class GameBananaScreenshot
{
    [JsonPropertyName("_sBaseUrl")]
    public string? BaseUrl { get; set; }

    [JsonPropertyName("_sFile220")]
    public string? File220 { get; set; }

    [JsonPropertyName("_sFile220Sfw")]
    public string? File220Sfw { get; set; }

    [JsonPropertyName("_sFile530")]
    public string? File530 { get; set; }

    [JsonPropertyName("_sFile530Sfw")]
    public string? File530Sfw { get; set; }
}

internal sealed class GameBananaModDetail
{
    [JsonPropertyName("_idRow")]
    public long IdRow { get; set; }

    [JsonPropertyName("_sName")]
    public string? Name { get; set; }

    [JsonPropertyName("_sText")]
    public string? Text { get; set; }

    [JsonPropertyName("_sDescription")]
    public string? Description { get; set; }

    [JsonPropertyName("_sVersion")]
    public string? Version { get; set; }

    [JsonPropertyName("_sProfileUrl")]
    public string? ProfileUrl { get; set; }

    [JsonPropertyName("_bIsObsolete")]
    public bool IsObsolete { get; set; }

    [JsonPropertyName("_aSubmitter")]
    public GameBananaSubmitter? Submitter { get; set; }

    [JsonPropertyName("_aFiles")]
    public List<GameBananaFileDto> Files { get; set; } = [];
}

internal sealed class GameBananaFileDto
{
    [JsonPropertyName("_idRow")]
    public long IdRow { get; set; }

    [JsonPropertyName("_sFile")]
    public string? FileName { get; set; }

    [JsonPropertyName("_sDescription")]
    public string? Description { get; set; }

    [JsonPropertyName("_sDownloadUrl")]
    public string? DownloadUrl { get; set; }

    [JsonPropertyName("_nFilesize")]
    public long FileSize { get; set; }

    [JsonPropertyName("_sVersion")]
    public string? Version { get; set; }

    [JsonPropertyName("_bIsArchived")]
    public bool IsArchived { get; set; }
}

internal sealed class GameBananaUpdatesResponse
{
    [JsonPropertyName("_aRecords")]
    public List<GameBananaUpdateRecord> Records { get; set; } = [];
}

internal sealed class GameBananaUpdateRecord
{
    [JsonPropertyName("_sVersion")]
    public string? Version { get; set; }

    [JsonPropertyName("_sName")]
    public string? Name { get; set; }

    [JsonPropertyName("_sText")]
    public string? Text { get; set; }

    [JsonPropertyName("_aChangeLog")]
    public List<GameBananaChangeLogEntry>? ChangeLog { get; set; }
}

internal sealed class GameBananaChangeLogEntry
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("_sText")]
    public string? SText { get; set; }
}
