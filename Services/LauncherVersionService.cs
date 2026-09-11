using System.Reflection;

namespace QuiverLauncher.Services;

public static class LauncherVersionService
{
    public static string NormalizeVersionString(string? version) => Core.Services.ReleaseVersionIdentity.NormalizeVersionString(version);
    public static bool IsNewerVersion(string candidateVersion, string baselineVersion) => Core.Services.ReleaseVersionIdentity.IsNewerVersion(candidateVersion, baselineVersion);
    public static bool AreVersionsEquivalent(string? firstVersion, string? secondVersion) => Core.Services.ReleaseVersionIdentity.AreVersionsEquivalent(firstVersion, secondVersion);
    public static bool LooksLikePrereleaseTag(string? version) => Core.Services.ReleaseVersionIdentity.LooksLikePrereleaseTag(version);

    /// <summary>
    /// Launcher version from the running assembly (InformationalVersion, then AssemblyVersion).
    /// Velopack callers should prefer <c>VelopackLocator</c> and use this only as fallback.
    /// <paramref name="baseDirectory"/> is ignored (kept for call-site compatibility).
    /// </summary>
    public static string ReadInstalledVersion(string? baseDirectory = null)
    {
        _ = baseDirectory;

        try
        {
            var assembly = typeof(LauncherVersionService).Assembly;
            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informational))
                return StripBuildMetadata(informational.Trim());

            var version = assembly.GetName().Version;
            if (version != null)
                return $"{version.Major}.{version.Minor}.{version.Build}";

            return "Version information not found";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading version: {ex.Message}");
            return "Version loading failed";
        }
    }

    internal static string StripBuildMetadata(string version)
    {
        var plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }

}
