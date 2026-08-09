using System.Reflection;

namespace QuiverLauncher.Services;

public static class LauncherVersionService
{
    public static string NormalizeVersionString(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return "0.0.0";

        var normalized = version.Trim().TrimStart('v', 'V');
        var plus = normalized.IndexOf('+');
        if (plus >= 0)
            normalized = normalized[..plus];

        var segments = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries).ToList();

        while (segments.Count < 3)
            segments.Add("0");

        return string.Join(".", segments.Take(4));
    }

    public static bool IsNewerVersion(string candidateVersion, string baselineVersion)
    {
        try
        {
            var candidate = new Version(NormalizeVersionString(candidateVersion));
            var baseline = new Version(NormalizeVersionString(baselineVersion));
            return candidate.CompareTo(baseline) > 0;
        }
        catch
        {
            return !candidateVersion.TrimStart('v', 'V').Equals(
                baselineVersion.TrimStart('v', 'V'),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    public static bool AreVersionsEquivalent(string? firstVersion, string? secondVersion)
    {
        if (string.IsNullOrWhiteSpace(firstVersion) || string.IsNullOrWhiteSpace(secondVersion))
            return false;

        try
        {
            return new Version(NormalizeVersionString(firstVersion))
                .Equals(new Version(NormalizeVersionString(secondVersion)));
        }
        catch
        {
            return firstVersion.TrimStart('v', 'V').Trim()
                .Equals(secondVersion.TrimStart('v', 'V').Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

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
