using System.Text;
using System.Text.RegularExpressions;

namespace QuiverLauncher.Services;

public enum LauncherUpdateFailureReason
{
    Unknown,
    /// <summary>Windows refused the updater permission to move Quiver's files.</summary>
    AccessDenied,
    /// <summary>Another program had Quiver's files open.</summary>
    FilesInUse,
}

/// <summary>
/// Notices a Quiver update that was downloaded but not installed. When Velopack's updater can't
/// replace Quiver's files it gives up, reopens the old version and only writes the reason to its
/// log, so without this the player is offered the same update every time Quiver starts.
/// </summary>
public static partial class LauncherUpdateFailure
{
    // Update.exe sets this whenever it starts Quiver after applying an update, whether or not the
    // update went in. VelopackApp.Run() clears it, so it has to be read before Run().
    private const string UpdaterRestartVariable = "VELOPACK_RESTART";
    private const int LogTailBytes = 256 * 1024;

    public static bool RestartedByUpdater { get; private set; }

    public static void CaptureUpdaterRestart() =>
        RestartedByUpdater = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(UpdaterRestartVariable));

    /// <summary>
    /// Reopened by the updater while a downloaded release newer than this one is still waiting: the
    /// update was not installed. After a good update the downloaded release is the one running.
    /// </summary>
    public static bool UpdateWasNotInstalled(bool restartedByUpdater, string? pendingVersion) =>
        restartedByUpdater && !string.IsNullOrWhiteSpace(pendingVersion);

    /// <summary>Where Velopack's updater writes its log on Windows.</summary>
    public static string DefaultLogPath(string? appId) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "velopack",
        $"velopack_{(string.IsNullOrWhiteSpace(appId) ? QuiverLauncherPaths.AppName : appId)}.log");

    public static LauncherUpdateFailureReason ReadReason(string logPath)
    {
        try
        {
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > LogTailBytes)
                stream.Seek(-LogTailBytes, SeekOrigin.End);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return ParseReason(reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return LauncherUpdateFailureReason.Unknown;
        }
    }

    /// <summary>
    /// The reason the last update attempt in the updater's log failed. Windows error text is in the
    /// player's language, so this reads the error codes: 5 is access denied, 32 and 33 are files
    /// another program has open.
    /// </summary>
    public static LauncherUpdateFailureReason ParseReason(string log)
    {
        var start = log.LastIndexOf("Command: Apply", StringComparison.Ordinal);
        var attempt = start >= 0 ? log[start..] : log;
        var codes = OsErrorCode().Matches(attempt);
        if (codes.Count == 0)
            return LauncherUpdateFailureReason.Unknown;
        return codes[^1].Groups[1].Value switch
        {
            "5" => LauncherUpdateFailureReason.AccessDenied,
            "32" or "33" => LauncherUpdateFailureReason.FilesInUse,
            _ => LauncherUpdateFailureReason.Unknown,
        };
    }

    [GeneratedRegex(@"Os \{ code: (\d+),")]
    private static partial Regex OsErrorCode();

    public const string Title = "Quiver Launcher wasn't updated";

    public static string FormatMessage(
        string pendingVersion,
        string? currentVersion,
        string? installFolder,
        LauncherUpdateFailureReason reason,
        string? logPath)
    {
        var folder = string.IsNullOrWhiteSpace(installFolder) ? "Quiver's folder" : installFolder.TrimEnd('\\', '/');
        var drive = DriveRootName(installFolder);
        var where = drive != null ? $"drive {drive}" : folder;
        var still = string.IsNullOrWhiteSpace(currentVersion) ? "" : $" You're still on {currentVersion}.";
        var message = new StringBuilder();

        message.Append(reason switch
        {
            LauncherUpdateFailureReason.AccessDenied =>
                $"Quiver Launcher {pendingVersion} was downloaded, but Windows didn't allow Quiver to replace its own files in {where}.{still}",
            LauncherUpdateFailureReason.FilesInUse =>
                $"Quiver Launcher {pendingVersion} was downloaded, but another program was using Quiver's files in {where}, so it couldn't be installed.{still}",
            _ =>
                $"Quiver Launcher {pendingVersion} was downloaded, but it couldn't be installed in {where}.{still}",
        });
        message.Append("\n\n");

        if (reason == LauncherUpdateFailureReason.FilesInUse)
        {
            message.Append("Close any games started from Quiver and anything else open in that folder, or restart your PC, then open Quiver again to install the update.");
        }
        else
        {
            if (drive != null)
                message.Append($"Quiver is installed at the top of drive {drive}, where Windows often blocks changes.\n\n");
            else if (IsInProgramFiles(installFolder))
                message.Append("Quiver is in Program Files, where changes need administrator access.\n\n");

            message.Append("To fix it, either:\n");
            message.Append(drive != null
                ? $"• Quit Quiver, make a folder such as {drive}Quiver, and move everything Quiver put at the top of the drive into it. Then open Quiver from there.\n"
                : "• Quit Quiver and move the whole Quiver folder, with everything in it, somewhere you have full access, such as C:\\Games\\Quiver. Then open Quiver from there.\n");
            message.Append("• Or right-click QuiverLauncher.exe, choose Run as administrator, and install the update once.");
        }

        if (!string.IsNullOrWhiteSpace(logPath))
            message.Append($"\n\nThe updater's log is at {logPath}.");
        return message.ToString();
    }

    /// <summary>"G:\" when Quiver sits directly at the top of a drive, otherwise null.</summary>
    internal static string? DriveRootName(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return null;
        var trimmed = folder.Trim().TrimEnd('\\', '/');
        return trimmed.Length == 2 && char.IsAsciiLetter(trimmed[0]) && trimmed[1] == ':'
            ? char.ToUpperInvariant(trimmed[0]) + ":\\"
            : null;
    }

    private static bool IsInProgramFiles(string? folder) =>
        !string.IsNullOrWhiteSpace(folder) &&
        folder.Replace('/', '\\').Contains("\\Program Files", StringComparison.OrdinalIgnoreCase);
}
