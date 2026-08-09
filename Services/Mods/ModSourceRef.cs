namespace QuiverLauncher.Services.Mods;

/// <summary>Parsed, provider-normalized reference to a remote mod catalog.</summary>
public sealed class ModSourceRef
{
    public required string ProviderId { get; init; }
    public required string SourceKey { get; init; }
    public required string DisplayLabel { get; init; }
    public required string SourceUrl { get; init; }
}
