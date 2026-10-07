namespace QuiverLauncher.Core.Services;

public static class ReleaseVersionIdentity
{
    public static string NormalizeVersionString(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return "0.0.0";

        var normalized = StripWordPrefix(StripBuildMetadata(version.Trim().TrimStart('v', 'V')));
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

        // Numeric ordering deliberately extracts a core from labeled tags. That
        // lossy normalization must never establish release identity: unknown tags
        // can both normalize to 0.0.0, and beta suffixes can disappear.
        return TryParseNumericIdentity(firstIdentity, out var first) &&
            TryParseNumericIdentity(secondIdentity, out var second) && first == second;
    }

    public static bool LooksLikePrereleaseTag(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return false;

        return HasVersionLabel(version);
    }

    /// <summary>"Version1.0.4" and "r23" compare by their numbers; "release-1.0" keeps its label.</summary>
    private static string StripWordPrefix(string version)
    {
        var letters = 0;
        while (letters < version.Length && char.IsAsciiLetter(version[letters]))
            letters++;
        return letters > 0 && letters < version.Length && char.IsAsciiDigit(version[letters])
            ? version[letters..]
            : version;
    }

    internal static string StripBuildMetadata(string version)
    {
        var plus = version.IndexOf('+');
        return plus >= 0 ? version[..plus] : version;
    }

    private static string VersionIdentity(string version)
    {
        var identity = version.Trim();
        if (identity.Length > 1 && identity[0] is 'v' or 'V' && char.IsAsciiDigit(identity[1]))
            identity = identity[1..];
        return StripBuildMetadata(identity);
    }

    private static bool TryParseNumericIdentity(string identity, out Version? version)
    {
        version = null;
        var parts = identity.Split('.');
        if (parts.Length > 4 || parts.Any(part => part.Length == 0 || !part.All(char.IsAsciiDigit)))
            return false;
        return Version.TryParse(string.Join('.', parts.Concat(Enumerable.Repeat("0", Math.Max(0, 3 - parts.Length)))), out version);
    }

    private static bool IsPlainNumericVersion(string identity)
    {
        var parts = identity.Split('.');
        return parts.Length > 0 && parts.All(part => part.Length > 0 && part.All(char.IsAsciiDigit));
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
