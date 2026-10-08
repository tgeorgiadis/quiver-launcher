using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class LauncherUpdateFailureTests
{
    // Trimmed from a player's log: Quiver at the top of drive G:, where moving current was refused.
    private const string AccessDeniedLog = """
        [update:19152] [12:08:23] [INFO] Command: Apply
        [update:19152] [12:08:23] [INFO] Applying package 3.5.0-rc.4 to current: 3.4.5
        [update:19152] [12:08:26] [INFO] Backing up current dir to "G:\\packages\\VelopackTemp\\tmp_pJlx3wwppPcznHfo"
        [update:19152] [12:08:26] [WARN] Retrying operation in 1000ms... (error was: Some(Os { code: 5, kind: PermissionDenied, message: "Access is denied." }))
        [update:19152] [12:08:35] [WARN] Retrying operation in 1000ms... (error was: Some(Os { code: 5, kind: PermissionDenied, message: "Access is denied." }))
        [update:19152] [12:08:37] [ERROR] Apply error: Error applying package: Unable to start the update, because one or more running processes prevented it. Try again later, or if the issue persists, restart your computer.
        [lib-csharp:28792] [12:08:37] [Information] Pre-condition failed, we will not restart to apply updates. (restarted: True, autoApply: True)
        """;

    [Theory]
    [InlineData(true, "3.5.0-rc.4", true)]
    [InlineData(true, null, false)]
    [InlineData(true, "", false)]
    [InlineData(false, "3.5.0-rc.4", false)]
    public void An_update_failed_only_when_reopened_by_the_updater_with_a_newer_release_waiting(
        bool restartedByUpdater, string? pendingVersion, bool expected)
    {
        LauncherUpdateFailure.UpdateWasNotInstalled(restartedByUpdater, pendingVersion).Should().Be(expected);
    }

    [Fact]
    public void Reads_access_denied_from_the_updater_log()
    {
        LauncherUpdateFailure.ParseReason(AccessDeniedLog).Should().Be(LauncherUpdateFailureReason.AccessDenied);
    }

    [Fact]
    public void Reads_the_error_code_not_the_translated_message()
    {
        const string log = """
            [INFO] Command: Apply
            [WARN] Retrying operation in 1000ms... (error was: Some(Os { code: 5, kind: PermissionDenied, message: "Accesso negato." }))
            """;

        LauncherUpdateFailure.ParseReason(log).Should().Be(LauncherUpdateFailureReason.AccessDenied);
    }

    [Fact]
    public void Only_the_last_update_attempt_counts()
    {
        var log = AccessDeniedLog + """
            [INFO] Command: Apply
            [WARN] Retrying operation in 1000ms... (error was: Some(Os { code: 32, kind: Uncategorized, message: "The process cannot access the file because it is being used by another process." }))
            """;

        LauncherUpdateFailure.ParseReason(log).Should().Be(LauncherUpdateFailureReason.FilesInUse);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[INFO] Command: Apply\n[ERROR] Apply error: Elevated process has exited with ERROR: 1.")]
    public void Unknown_when_the_log_names_no_windows_error(string log)
    {
        LauncherUpdateFailure.ParseReason(log).Should().Be(LauncherUpdateFailureReason.Unknown);
    }

    [Fact]
    public void A_missing_log_is_unknown()
    {
        LauncherUpdateFailure.ReadReason(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "velopack.log"))
            .Should().Be(LauncherUpdateFailureReason.Unknown);
    }

    [Fact]
    public void Reads_the_reason_from_the_log_file()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".log");
        try
        {
            File.WriteAllText(path, AccessDeniedLog);
            LauncherUpdateFailure.ReadReason(path).Should().Be(LauncherUpdateFailureReason.AccessDenied);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("G:\\", "G:\\")]
    [InlineData("g:", "G:\\")]
    [InlineData("G:\\Quiver", null)]
    [InlineData("C:\\Users\\player\\Quiver\\", null)]
    [InlineData(null, null)]
    public void Knows_when_Quiver_is_at_the_top_of_a_drive(string? folder, string? expected)
    {
        LauncherUpdateFailure.DriveRootName(folder).Should().Be(expected);
    }

    [Fact]
    public void Access_denied_at_the_top_of_a_drive_says_why_and_how_to_fix_it()
    {
        var message = LauncherUpdateFailure.FormatMessage(
            "3.5.0-rc.4", "3.4.5", "G:\\", LauncherUpdateFailureReason.AccessDenied,
            "C:\\Users\\player\\AppData\\Local\\velopack\\velopack_QuiverLauncher.log");

        message.Should().Contain("Windows didn't allow Quiver to replace its own files in drive G:\\");
        message.Should().Contain("You're still on 3.4.5.");
        message.Should().Contain("installed at the top of drive G:\\");
        message.Should().Contain("G:\\Quiver");
        message.Should().Contain("Run as administrator");
        message.Should().Contain("velopack_QuiverLauncher.log");
    }

    [Fact]
    public void Program_Files_needs_administrator_access()
    {
        var message = LauncherUpdateFailure.FormatMessage(
            "3.5.0", "3.4.5", "C:\\Program Files\\Quiver", LauncherUpdateFailureReason.AccessDenied, null);

        message.Should().Contain("in C:\\Program Files\\Quiver");
        message.Should().Contain("Program Files, where changes need administrator access");
        message.Should().Contain("C:\\Games\\Quiver");
    }

    [Fact]
    public void Files_in_use_asks_to_close_other_programs()
    {
        var message = LauncherUpdateFailure.FormatMessage(
            "3.5.0", "3.4.5", "D:\\Games\\Quiver", LauncherUpdateFailureReason.FilesInUse, null);

        message.Should().Contain("another program was using Quiver's files in D:\\Games\\Quiver");
        message.Should().Contain("restart your PC");
        message.Should().NotContain("Run as administrator");
    }
}
