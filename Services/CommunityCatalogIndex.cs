using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuiverLauncher.Services;

public sealed class CommunityCatalogIndex
{
    public int Version { get; set; }

    public List<CommunityCatalogListEntry> Lists { get; set; } = [];

    public static CommunityCatalogIndex? TryParse(string json)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };
            return JsonSerializer.Deserialize<CommunityCatalogIndex>(json, options);
        }
        catch
        {
            return null;
        }
    }
}

public sealed class CommunityCatalogListEntry
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public string Location { get; set; } = "";

    [JsonPropertyName("remoteLocation")]
    public string? RemoteLocation { get; set; }

    public string? ListVersion { get; set; }
}

public sealed class CommunityCatalogSyncResult
{
    public bool IndexLoaded { get; init; }

    public bool UsedRemoteIndex { get; init; }

    public string? LastError { get; init; }

    public int AddedSourceCount { get; init; }

    public int UpdatedSourceCount { get; init; }

    public List<string> AddedSourceNames { get; init; } = [];
}
