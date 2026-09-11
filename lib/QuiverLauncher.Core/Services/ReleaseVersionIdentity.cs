namespace QuiverLauncher.Core.Services;

public static class ReleaseVersionIdentity
{
    public static string NormalizeVersionString(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return "0.0.0";

        var normalized = StripBuildMetadata(version.Trim().TrimStart('v', 'V'));
        var labelAt = normalized.IndexOfAny(['-', ' ', '\t']);
        if (labelAt >= 0)
            normalized = normalized[..labelAt];

        var segments = new List<string>();
        foreach (var part in normalized.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var digits = TakeLeadingDigits(part);
            if (digits.Length == 0)
                break;
            segments.Add(digits);
        }

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
            return false;
        }
    }

    public static bool AreVersionsEquivalent(string? firstVersion, string? secondVersion)
    {
        if (string.IsNullOrWhiteSpace(firstVersion) || string.IsNullOrWhiteSpace(secondVersion))
            return false;

        var firstIdentity = VersionIdentity(firstVersion);
        var secondIdentity = VersionIdentity(secondVersion);
        if (firstIdentity.Equals(secondIdentity, StringComparison.OrdinalIgnoreCase))
            return true;

        if (HasVersionLabel(firstVersion) || HasVersionLabel(secondVersion))
            return false;

        try
        {
            return new Version(NormalizeVersionString(firstVersion))
                .Equals(new Version(NormalizeVersionString(secondVersion)));
        }
        catch
        {
            return false;
        }
    }

    public static bool LooksLikePrereleaseTag(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return false;

        return HasVersionLabel(version);
    }

    internal static string StripBuildMetadata(string version)
    {
        var plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }

    private static string VersionIdentity(string version)
    {
        return StripBuildMetadata(version.Trim().TrimStart('v', 'V'));
    }

    private static bool HasVersionLabel(string version)
    {
        var identity = VersionIdentity(version);
        return identity.IndexOfAny(['-', ' ', '\t']) >= 0;
    }

    private static string TakeLeadingDigits(string part)
    {
        var length = 0;
        while (length < part.Length && char.IsDigit(part[length]))
            length++;
        return length == 0 ? string.Empty : part[..length];
    }
}
