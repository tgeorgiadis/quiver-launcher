namespace QuiverLauncher.Services.Mods;

/// <summary>Options for paged listing / remote search requests.</summary>
public sealed class ModListOptions
{
    public bool IncludeNsfw { get; init; }

    /// <summary>UI sort tag (e.g. TopRated). Providers map remote sorts to API params.</summary>
    public string SortMode { get; init; } = ModListSorter.InstalledFirst;

    public static ModListOptions Default { get; } = new();
}
