namespace QuiverLauncher.Core.Services;

public sealed class GameInstallationOptions
{
    public static GameInstallationOptions Default { get; } = new();

    public IReadOnlyCollection<string> AdditionalMetadataFileNames { get; init; } = Array.Empty<string>();

    public Action<string>? Log { get; init; }

    /// <summary>
    /// Optional 0–1 progress for archive extraction (after the download has finished).
    /// </summary>
    public IProgress<double>? ExtractProgress { get; init; }
}
