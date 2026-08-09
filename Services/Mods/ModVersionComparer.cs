using QuiverLauncher.Services;

namespace QuiverLauncher.Services.Mods;

public static class ModVersionComparer
{
    /// <summary>
    /// Returns true when <paramref name="latest"/> is newer than <paramref name="installed"/>.
    /// Falls back to ordinal compare when not semver-like.
    /// </summary>
    public static bool IsUpdateAvailable(string? installed, string? latest)
    {
        if (string.IsNullOrWhiteSpace(latest))
            return false;
        if (string.IsNullOrWhiteSpace(installed))
            return true;

        if (LauncherVersionService.AreVersionsEquivalent(installed, latest))
            return false;

        if (TryCompareSemVer(installed, latest, out var cmp))
            return cmp < 0;

        return !string.Equals(installed.Trim(), latest.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryCompareSemVer(string a, string b, out int comparison)
    {
        comparison = 0;
        if (!TryParseSemVer(a, out var aParts) || !TryParseSemVer(b, out var bParts))
            return false;

        var len = Math.Max(aParts.Length, bParts.Length);
        for (var i = 0; i < len; i++)
        {
            var av = i < aParts.Length ? aParts[i] : 0;
            var bv = i < bParts.Length ? bParts[i] : 0;
            if (av == bv)
                continue;
            comparison = av.CompareTo(bv);
            return true;
        }

        comparison = 0;
        return true;
    }

    private static bool TryParseSemVer(string value, out int[] parts)
    {
        parts = [];
        var trimmed = value.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
            trimmed = trimmed[1..];

        var segments = trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
            return false;

        var parsed = new int[segments.Length];
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            var digits = new string(segment.TakeWhile(char.IsDigit).ToArray());
            if (digits.Length == 0 || !int.TryParse(digits, out parsed[i]))
                return false;
        }

        parts = parsed;
        return true;
    }
}
