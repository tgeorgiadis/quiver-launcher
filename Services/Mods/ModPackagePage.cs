namespace QuiverLauncher.Services.Mods;

public sealed class ModPackagePage
{
    public IReadOnlyList<ModPackage> Packages { get; init; } = [];

    /// <summary>Null when there are no further pages.</summary>
    public string? NextPageToken { get; init; }

    public int? TotalCount { get; init; }
}
