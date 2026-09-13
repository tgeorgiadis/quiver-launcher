namespace QuiverLauncher.Services;

public static class AndroidLauncherPackageValidation
{
    // Reuse the launcher's numeric comparison, then order preview labels when the core versions match.
    // The general library comparer deliberately ignores labels; self updates must handle beta -> stable.
    public static bool IsNewerVersion(string candidate, string installed)
    {
        const string versionPattern = @"^[vV]?\d+\.\d+\.\d+(?:\.\d+)?(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z.-]+)?$";
        if (!System.Text.RegularExpressions.Regex.IsMatch(candidate, versionPattern) ||
            !System.Text.RegularExpressions.Regex.IsMatch(installed, versionPattern)) return false;
        if (LauncherVersionService.IsNewerVersion(candidate, installed)) return true;
        if (LauncherVersionService.IsNewerVersion(installed, candidate)) return false;
        static string? Label(string value) => value.Split('+')[0].Split('-', 2).ElementAtOrDefault(1);
        var next = Label(candidate); var previous = Label(installed);
        if (previous == null) return false;
        if (next == null) return true;
        var left = next.Split('.'); var right = previous.Split('.');
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var leftNumber = System.Numerics.BigInteger.TryParse(left[i], out var a);
            var rightNumber = System.Numerics.BigInteger.TryParse(right[i], out var b);
            var comparison = leftNumber && rightNumber ? a.CompareTo(b) : leftNumber != rightNumber ? leftNumber ? -1 : 1 : StringComparer.Ordinal.Compare(left[i], right[i]);
            if (comparison != 0) return comparison > 0;
        }
        return left.Length > right.Length;
    }
    public static void ValidateIdentity(LauncherApkIdentity installed, LauncherApkIdentity candidate, string expectedVersion)
    {
        if (candidate.PackageName != installed.PackageName)
            throw new InvalidDataException("This APK is not a Quiver Launcher update.");
        if (candidate.VersionCode < installed.VersionCode ||
            !IsNewerVersion(candidate.VersionName, installed.VersionName) ||
            !LauncherVersionService.AreVersionsEquivalent(candidate.VersionName, expectedVersion))
            throw new InvalidDataException("The APK version does not match this update, or would downgrade Quiver.");
    }
    public static bool HasCompatibleSigningKeys(IEnumerable<string> installed, IEnumerable<string> candidate, IEnumerable<string>? candidateHistory = null)
    {
        var previous = installed.ToHashSet(StringComparer.Ordinal);
        var next = candidate.ToHashSet(StringComparer.Ordinal);
        if (previous.Count == 0 || next.Count == 0) return false;
        if (previous.SetEquals(next)) return true;
        // History is supplied only by Android's parsed SigningInfo; Android verifies the rotation at installation.
        return previous.Count == 1 && next.Count == 1 && previous.IsSubsetOf(candidateHistory ?? []);
    }
}
