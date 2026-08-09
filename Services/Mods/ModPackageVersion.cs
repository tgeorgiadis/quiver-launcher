namespace QuiverLauncher.Services.Mods;

public sealed class ModPackageVersion
{
    public required string Version { get; init; }
    public required string DownloadUrl { get; init; }
    public long FileSize { get; init; }
    public IReadOnlyList<string> Dependencies { get; init; } = [];
}
