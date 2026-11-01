using System.Text.RegularExpressions;

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

    /// <summary>A tag naming no version at all (nightly, latest-nightly, continuous): a release rebuilt under it.</summary>
    public static bool IsRollingTag(string? tag) => !string.IsNullOrWhiteSpace(tag) && !tag.Any(char.IsAsciiDigit);

    /// <summary>
    /// Whether version numbers tell two tags apart: both name a dotted version (1.2, v3.5.1-rc.1) and the numbers differ.
    /// Builds tagged by date or commit (KytyPS5-2026-10-19-e5f0ee7, bc9098d) or by name (EXODUS) can't be told apart so.
    /// </summary>
    public static bool CanCompareVersions(string? first, string? second) =>
        DottedVersion.IsMatch(first ?? "") && DottedVersion.IsMatch(second ?? "") &&
        NormalizeVersionString(first) != NormalizeVersionString(second);

    private static readonly Regex DottedVersion = new(@"\d+\.\d+", RegexOptions.CultureInvariant);

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
