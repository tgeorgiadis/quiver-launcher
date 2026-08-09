namespace QuiverLauncher.Services;

public sealed class ManualLauncherCheckResult
{
    public bool CheckSucceeded { get; init; }
    public string? ErrorMessage { get; init; }
    public string InstalledVersion { get; init; } = "";
    public bool LauncherUpdatePending { get; init; }
    public string? AvailableLauncherVersion { get; init; }
}
