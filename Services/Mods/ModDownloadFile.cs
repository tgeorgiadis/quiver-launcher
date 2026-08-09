namespace QuiverLauncher.Services.Mods;

public sealed class ModDownloadFile
{
    public required string Id { get; init; }
    public required string FileName { get; init; }
    public string Description { get; init; } = string.Empty;
    public required string DownloadUrl { get; init; }
    public long FileSize { get; init; }
    public string Version { get; init; } = string.Empty;
}
