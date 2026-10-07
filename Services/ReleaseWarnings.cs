using System.Text;

namespace QuiverLauncher.Services;

/// <summary>
/// Asks before a catalog app installs a release Quiver hasn't verified, and asks twice for one it blocked.
/// Saying no (the default) keeps what is installed. Kept short: what happened, why, and what is verified instead.
/// </summary>
public static class ReleaseWarnings
{
    public static async Task<bool> ConfirmAsync(string app, string version, ReleaseCheck check, Func<string, string, Task<bool>> ask)
    {
        if (check.State == ReleaseCheckState.Blocked)
            return await ask(Blocked(app, version, check), "Blocked release") &&
                await ask($"Are you sure? Quiver blocked {app} {version}.", "Install a blocked release?");
        return await ask(Unverified(app, version, check), "Not verified yet");
    }

    internal static string Unverified(string app, string version, ReleaseCheck check)
    {
        var text = new StringBuilder($"Quiver hasn't verified {app} {version} yet.");
        Details(text, check);
        var after = new List<string>();
        if (check.VerifiedAt is { } at && at > DateTimeOffset.UtcNow)
        {
            var hours = Math.Max(1, (int)Math.Ceiling((at - DateTimeOffset.UtcNow).TotalHours));
            after.Add($"It should be verified in about {hours} {(hours == 1 ? "hour" : "hours")}.");
        }
        after.Add(Verified(version, check));
        return text.Append("\n\n").Append(string.Join("\n", after.Where(s => s.Length > 0)))
            .Append("\n\nInstall it anyway?").ToString();
    }

    internal static string Blocked(string app, string version, ReleaseCheck check)
    {
        var text = new StringBuilder($"Quiver blocked {app} {version}, so it shouldn't be installed.");
        Details(text, check);
        if (Verified(version, check) is { Length: > 0 } verified) text.Append("\n\n").Append(verified);
        return text.Append("\n\nInstall it anyway?").ToString();
    }

    private static void Details(StringBuilder text, ReleaseCheck check)
    {
        var lines = check.Reasons.Select(r => "• " + r).ToList();
        if (Scan(check.ScanVerdict, check.ScanEngines) is { } scan) lines.Add("• VirusTotal: " + scan);
        if (lines.Count > 0) text.Append("\n\n").Append(string.Join("\n", lines));
    }

    // Point the player at the release Quiver did check.
    private static string Verified(string version, ReleaseCheck check) =>
        string.IsNullOrWhiteSpace(check.VerifiedVersion) ? "No version of this app is verified yet."
        : QuiverLauncher.Core.Services.ReleaseVersionIdentity.AreVersionsEquivalent(check.VerifiedVersion, version) ? ""
        : $"The verified version is {check.VerifiedVersion}.";

    // Only a detection is worth the space; a clean or pending scan says nothing the player must weigh.
    private static string? Scan(string? verdict, string? engines) => verdict switch
    {
        "warning" => $"{engines ?? "an engine"} flag one of its files (often a false alarm).",
        "flagged" => $"{engines ?? "several engines"} flag one of its files.",
        _ => null,
    };
}
