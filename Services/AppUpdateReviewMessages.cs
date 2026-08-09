using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

public static class AppUpdateReviewMessages
{
    public static string FormatQuiverOnlyUpdateMessage(string? launcherVersion)
    {
        var version = FormatLauncherVersion(launcherVersion);
        return $"Quiver Launcher update {version} is available.\n\nUpdate Quiver Launcher now?";
    }

    public static string FormatCombinedUpdatesMessage(
        string? launcherVersion,
        IReadOnlyList<GameInfo> games)
    {
        var version = FormatLauncherVersion(launcherVersion);
        var ordered = games
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var appHeader = ordered.Count == 1
            ? "1 app update needs review:"
            : $"{ordered.Count} app updates need review:";

        var appLines = ordered.Select(FormatGameUpdateLine);
        return
            $"Quiver Launcher update {version} is available.\n\n" +
            appHeader + "\n\n" +
            string.Join('\n', appLines) +
            "\n\nWhat would you like to update?";
    }

    public static string FormatPendingAppUpdatesMessage(
        IReadOnlyList<GameInfo> games,
        bool includeOpenPrompt)
    {
        var ordered = games
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var header = ordered.Count == 1
            ? "1 app update needs review:"
            : $"{ordered.Count} app updates need review:";

        var lines = ordered.Select(FormatGameUpdateLine);
        var body = header + "\n\n" + string.Join('\n', lines);

        if (!includeOpenPrompt)
            return body;

        var prompt = ordered.Count == 1
            ? "Review this update now?"
            : "Review these updates now?";
        return body + "\n\n" + prompt;
    }

    public static string FormatAutoUpdatedSummary(int updatedCount)
    {
        if (updatedCount <= 0)
            return string.Empty;

        return updatedCount == 1
            ? "1 app was updated automatically."
            : $"{updatedCount} apps were updated automatically.";
    }

    internal static string FormatGameUpdateLine(GameInfo game)
    {
        var name = string.IsNullOrWhiteSpace(game.Name) ? "Unknown app" : game.Name.Trim();
        var installed = string.IsNullOrWhiteSpace(game.InstalledVersion) ? "?" : game.InstalledVersion.Trim();
        var latest = string.IsNullOrWhiteSpace(game.LatestVersion) ? "?" : game.LatestVersion.Trim();
        return $"• {name} ({installed} → {latest})";
    }

    private static string FormatLauncherVersion(string? launcherVersion)
    {
        if (string.IsNullOrWhiteSpace(launcherVersion))
            return "unknown";

        var trimmed = launcherVersion.Trim();
        return trimmed.StartsWith('v') || trimmed.StartsWith('V')
            ? trimmed
            : $"v{trimmed}";
    }
}
