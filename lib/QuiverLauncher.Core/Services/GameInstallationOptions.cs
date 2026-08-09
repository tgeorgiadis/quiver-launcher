namespace QuiverLauncher.Core.Services;

public sealed class GameInstallationOptions
{
    public static GameInstallationOptions Default { get; } = new();

    public IReadOnlyCollection<string> AdditionalMetadataFileNames { get; init; } = Array.Empty<string>();

    public Action<string>? Log { get; init; }
}
