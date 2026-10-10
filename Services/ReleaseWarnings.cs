using System.Text;

namespace QuiverLauncher.Services;

/// <summary>
/// Asks before a catalog app installs a release Quiver hasn't verified, and asks twice for one it blocked; also before
/// a verified release whose file several antivirus engines flag, since the player's own antivirus may remove it.
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

    /// <summary>Asks before installing a verified release whose download several antivirus engines flag.</summary>
    public static Task<bool> ConfirmFlaggedAsync(string app, string version, ReleaseCheck check, bool update, Func<string, string, Task<bool>> ask) =>
        ask(Flagged(app, version, check, update), "Your antivirus may block this");

    internal static string Flagged(string app, string version, ReleaseCheck check, bool update) =>
        $"{check.ScanEngines ?? "Several engines"} on VirusTotal flag {(check.ScanFile is { } file ? file + ", the file Quiver downloads for" : "a file of")} {app} {version}." +
        "\n\nQuiver checked that this is the developer's own release, and game ports like this are often flagged by mistake. " +
        "Even so, your antivirus may block the download or remove files once it's installed, which would stop the app from working." +
        $"\n\n{(update ? "Update" : "Install")} anyway?";

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
        if (Scan(check) is { } scan) lines.Add("• VirusTotal: " + scan);
        if (lines.Count > 0) text.Append("\n\n").Append(string.Join("\n", lines));
    }

    // Point the player at the release Quiver did check.
    private static string Verified(string version, ReleaseCheck check) =>
        string.IsNullOrWhiteSpace(check.VerifiedVersion) ? "No version of this app is verified yet."
        : QuiverLauncher.Core.Services.ReleaseVersionIdentity.AreVersionsEquivalent(check.VerifiedVersion, version) ? ""
        : $"The verified version is {check.VerifiedVersion}.";

    // Only a detection is worth the space; a clean or pending scan says nothing the player must weigh.
    // Several engines are a warning in themselves: the player's antivirus may well agree with them.
    private static string? Scan(ReleaseCheck check) => check.ScanVerdict switch
    {
        "warning" => $"{check.ScanEngines ?? "an engine"} flag {check.ScanFile ?? "one of its files"} (often a false alarm).",
        "flagged" => $"{check.ScanEngines ?? "several engines"} flag {check.ScanFile ?? "one of its files"}. " +
            "Your antivirus may block or remove it.",
        _ => null,
    };
}
