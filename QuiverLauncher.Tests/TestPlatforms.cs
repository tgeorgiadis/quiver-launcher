using QuiverLauncher.Core.Models;

namespace QuiverLauncher.Tests;

internal static class TestPlatforms
{
    /// <summary>
    /// Target platform for fixtures that ship a Windows payload (a <c>game.exe</c> in an unlabeled
    /// or Windows-labeled archive). Automatic selection accepts those on Windows and Linux, but
    /// macOS only picks a Windows download when the target platform is set to Windows.
    /// </summary>
    public static TargetOS ForWindowsPayload => OperatingSystem.IsMacOS() ? TargetOS.Windows : TargetOS.Auto;
}
