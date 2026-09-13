using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

public sealed record GameShortcutTarget(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory);

/// <summary>Resolve shortcuts without launching Quiver, updating, or starting the game.</summary>
public static class GameShortcutLaunch
{
    public static async Task<GameShortcutTarget?> PrepareAsync(GameInfo game, string gamesFolder, AppSettings settings,
        Func<IReadOnlyList<string>, Task<string?>>? chooseExecutable = null, CancellationToken cancellationToken = default)
    {
        await game.CatalogPreparation.WaitAsync(cancellationToken);
        var gamePath = game.GetInstallPath(gamesFolder);
        if (!Directory.Exists(gamePath))
            throw new DirectoryNotFoundException($"Install {game.Name} before creating a shortcut. Its app folder was not found.");

        var executable = game.SelectedExecutable;
        if (!File.Exists(executable)) executable = await Task.Run(() => game.LoadSelectedExecutable(gamesFolder), cancellationToken);
        if (!File.Exists(executable))
        {
            var candidates = await Task.Run(() =>
            {
                var found = GameInstallationService.FindExecutableCandidates(gamePath, SearchOption.TopDirectoryOnly,
                    game.GetInstallationOptions(), out _);
                return found.Count > 0 ? found : GameInstallationService.FindExecutableCandidates(gamePath,
                    SearchOption.AllDirectories, game.GetInstallationOptions(), out _);
            }, cancellationToken);
            if (candidates.Count == 0)
                throw new FileNotFoundException($"No executable was found for {game.Name} in {gamePath}.");
            if (candidates.Count == 1)
                executable = candidates[0];
            else if (chooseExecutable != null)
                executable = await chooseExecutable(candidates);
            else
                throw new InvalidOperationException("Choose and save an executable in Quiver before creating this shortcut.");
            cancellationToken.ThrowIfCancellationRequested();
            if (executable == null) return null;
            if (!candidates.Contains(executable) || !File.Exists(executable))
                throw new FileNotFoundException("The selected executable is no longer available. Choose it again.");
        }

        executable = Path.GetFullPath(executable!);
        if (ShortcutHelper.IsAppImageMountPath(executable))
            throw new InvalidOperationException("Choose the installed executable, not a temporary AppImage mount path.");
        var target = BuildTarget(game, executable, gamePath, settings);
        // Persist even a previous in-memory choice before a deferred Steam worker starts.
        await File.WriteAllTextAsync(Path.Combine(gamePath, "selected_executable.txt"), executable, cancellationToken);
        game.SelectedExecutable = executable;
        if (OperatingSystem.IsLinux() && !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            File.SetUnixFileMode(executable, File.GetUnixFileMode(executable) | UnixFileMode.UserExecute);
        return target;
    }

    private static GameShortcutTarget BuildTarget(GameInfo game, string executable, string gamePath, AppSettings settings)
    {
        if (OperatingSystem.IsLinux() && executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            var runner = WindowsRunnerService.GetWindowsRunnerCommand(settings, executable, gamePath, game)
                ?? throw new InvalidOperationException("Configure Wine or Proton in this app’s Launch Options → Windows Runner before creating a shortcut.");
            return FromRunner(runner, gamePath);
        }
        return new(executable, [], Path.GetDirectoryName(executable) ?? gamePath);
    }

    internal static GameShortcutTarget FromRunner(WindowsRunnerCommandSpec runner, string gamePath) =>
        runner.EnvironmentVariables.Count == 0
            ? new(runner.FileName, runner.Arguments, gamePath)
            : new("/usr/bin/env", [.. runner.EnvironmentVariables.Select(pair => $"{pair.Key}={pair.Value}"), runner.FileName, .. runner.Arguments], gamePath);
}
