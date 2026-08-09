using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuiverLauncher.Services.Mods;

public sealed class InstalledModRecord
{
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    [JsonPropertyName("sourceKey")]
    public string SourceKey { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("owner")]
    public string Owner { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>Selected GameBanana (or similar) file id when the package had multiple downloads.</summary>
    [JsonPropertyName("downloadFileId")]
    public string? DownloadFileId { get; set; }

    [JsonPropertyName("downloadFileName")]
    public string? DownloadFileName { get; set; }

    [JsonPropertyName("files")]
    public List<string> Files { get; set; } = [];
}

public sealed class InstalledModsDocument
{
    [JsonPropertyName("mods")]
    public List<InstalledModRecord> Mods { get; set; } = [];
}

public sealed class InstalledModsStore
{
    public const string SidecarFileName = ".quiver-mods.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
    };

    public static string GetSidecarPath(string installRoot) =>
        Path.Combine(installRoot, SidecarFileName);

    public InstalledModsDocument Load(string installRoot)
    {
        var path = GetSidecarPath(installRoot);
        if (!File.Exists(path))
            return new InstalledModsDocument();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<InstalledModsDocument>(json, JsonOptions) ?? new InstalledModsDocument();
        }
        catch
        {
            return new InstalledModsDocument();
        }
    }

    public void Save(string installRoot, InstalledModsDocument document)
    {
        Directory.CreateDirectory(installRoot);
        var path = GetSidecarPath(installRoot);
        var json = JsonSerializer.Serialize(document, JsonOptions);
        File.WriteAllText(path, json);
    }

    public InstalledModRecord? Find(
        InstalledModsDocument document,
        string provider,
        string id)
    {
        return document.Mods.FirstOrDefault(m =>
            string.Equals(m.Provider, provider, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public InstalledModRecord? FindByFullName(
        InstalledModsDocument document,
        string provider,
        string sourceKey,
        string fullName)
    {
        return document.Mods.FirstOrDefault(m =>
            string.Equals(m.Provider, provider, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.FullName, fullName, StringComparison.OrdinalIgnoreCase));
    }
}
