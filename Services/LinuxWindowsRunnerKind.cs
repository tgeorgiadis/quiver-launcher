using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

public enum LinuxWindowsRunnerKind
{
    Auto = 0,
    Wine = 1,
    Proton = 2,
    Custom = 3,
}

public sealed class LinuxWindowsRunnerConfig
{
    public LinuxWindowsRunnerKind Kind { get; set; } = LinuxWindowsRunnerKind.Auto;
    public string? PrefixPath { get; set; }
    public string? ProtonPath { get; set; }
    public string? CustomLaunchCommand { get; set; }

    public static LinuxWindowsRunnerConfig FromGame(GameInfo? game)
    {
        if (game == null)
            return new LinuxWindowsRunnerConfig();

        return new LinuxWindowsRunnerConfig
        {
            Kind = WindowsRunnerService.ParseRunnerKind(game.LinuxRunner),
            PrefixPath = game.LinuxPrefixPath,
            ProtonPath = game.LinuxProtonPath,
            CustomLaunchCommand = game.LinuxCustomLaunchCommand,
        };
    }

    public void ApplyTo(GameInfo game)
    {
        ArgumentNullException.ThrowIfNull(game);
        game.LinuxRunner = WindowsRunnerService.FormatRunnerKind(Kind);
        game.LinuxPrefixPath = string.IsNullOrWhiteSpace(PrefixPath) ? null : PrefixPath.Trim();
        game.LinuxProtonPath = Kind == LinuxWindowsRunnerKind.Proton && !string.IsNullOrWhiteSpace(ProtonPath)
            ? ProtonPath.Trim()
            : null;
        game.LinuxCustomLaunchCommand = Kind == LinuxWindowsRunnerKind.Custom && !string.IsNullOrWhiteSpace(CustomLaunchCommand)
            ? CustomLaunchCommand.Trim()
            : null;
    }
}
