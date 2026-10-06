using System.Text;

namespace QuiverLauncher.Services;

/// <summary>
/// Asks before a catalog app installs a release Quiver hasn't verified, and asks twice for one it blocked.
/// Saying no (the default) keeps what is installed. Worded like quiverlauncher.com and Quiver Launcher 4.
/// </summary>
public static class ReleaseWarnings
{
    public static async Task<bool> ConfirmAsync(string app, string version, ReleaseCheck check, Func<string, string, Task<bool>> ask)
    {
        if (check.State == ReleaseCheckState.Blocked)
            return await ask(Blocked(app, version, check), "Install a blocked release?") &&
                await ask($"Quiver blocked {app} {version}. Install it anyway?\n\nChoose Yes only if you understand Quiver blocked this release.", "Install a blocked release");
        return await ask(Unverified(app, version, check), "Install before it's verified?");
    }

    internal static string Unverified(string app, string version, ReleaseCheck check)
    {
        var text = new StringBuilder($"Quiver hasn't verified {app} {version}. ");
        text.Append(check.Checksums.Count > 0
            ? "It installs the files Quiver saw when the release came out, and refuses any that changed since."
            : "Quiver hasn't checked this release's files, so it downloads them as the developer published them.");
        Details(text, check);
        if (check.VerifiedAt is { } at && at > DateTimeOffset.UtcNow)
        {
            var hours = Math.Max(1, (int)Math.Ceiling((at - DateTimeOffset.UtcNow).TotalHours));
            text.Append($"\n\nIf you wait, it's verified in about {hours} {(hours == 1 ? "hour" : "hours")} and offered as an update then.");
        }
        Verified(text, version, check);
        return text.Append($"\n\nInstall {version} anyway?").ToString();
    }

    internal static string Blocked(string app, string version, ReleaseCheck check)
    {
        var text = new StringBuilder($"Quiver blocked {app} {version}, so it's not meant to be installed. Quiver Launcher never updates to it. " +
            "Install it only if you know why you need this exact version.");
        Details(text, check);
        if (check.Checksums.Count > 0)
            text.Append("\n\nIt installs the files Quiver saw when the release came out, and refuses any that changed since.");
        Verified(text, version, check);
        return text.Append("\n\nInstall it anyway?").ToString();
    }

    private static void Details(StringBuilder text, ReleaseCheck check)
    {
        if (check.Reasons.Count > 0)
            text.Append("\n\n").Append(string.Join("\n", check.Reasons.Select(r => "• " + r)));
        if (Scan(check.ScanVerdict, check.ScanEngines) is { } scan)
            text.Append("\n\nVirusTotal: ").Append(scan);
    }

    // Point the player at the release Quiver did check.
    private static void Verified(StringBuilder text, string version, ReleaseCheck check)
    {
        if (string.IsNullOrWhiteSpace(check.VerifiedVersion))
            text.Append("\n\nNo release of this app is verified yet.");
        else if (!QuiverLauncher.Core.Services.ReleaseVersionIdentity.AreVersionsEquivalent(check.VerifiedVersion, version))
            text.Append($"\n\nThe verified release is {check.VerifiedVersion}. Quiver Launcher installs and updates to verified releases on its own; Change version also offers it.");
    }

    private static string? Scan(string? verdict, string? engines) => verdict switch
    {
        "clean" => "no antivirus engine flags its files.",
        "warning" => $"{engines ?? "an engine"} flag one of its files. A lone detection is often a false alarm.",
        "flagged" => $"{engines ?? "several engines"} flag one of its files.",
        "pending" => "still scanning its files.",
        "missing" => "couldn't scan every file.",
        _ => null,
    };
}
