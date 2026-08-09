using System.Diagnostics;
using System.Runtime.InteropServices;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

public static class GameLaunchService
{
    public static async Task<bool> LaunchAsync(GameInfo game, string gamesFolder)
    {
        if (string.IsNullOrEmpty(game.FolderName))
        {
            await GameDialogService.ShowMessageBoxAsync("Cannot launch game: folder name is not configured.", "Configuration Error");
            return false;
        }

        try
        {
            var gamePath = game.GetInstallPath(gamesFolder);

            if (!Directory.Exists(gamePath))
            {
                await GameDialogService.ShowMessageBoxAsync($"App directory not found: {gamePath}", "Directory Not Found");
                return false;
            }

            GameInstallationService.EnsureExecutableAtRoot(gamePath, game.GetInstallationOptions());

            var executables = GameInstallationService.FindExecutableCandidates(
                gamePath,
                SearchOption.TopDirectoryOnly,
                game.GetInstallationOptions(),
                out var needsWine);

            if (executables.Count == 0)
            {
                executables = GameInstallationService.FindExecutableCandidates(
                    gamePath,
                    SearchOption.AllDirectories,
                    game.GetInstallationOptions(),
                    out needsWine);
            }

            if (executables.Count == 0)
            {
                await GameDialogService.ShowMessageBoxAsync(
                    $"No executable found for {game.Name} in:\n{gamePath}\n\nThe game may not have installed correctly.",
                    "Executable Not Found");
                return false;
            }

            var settings = AppSettings.Load();

            if (needsWine && !WindowsRunnerService.IsWindowsRunnerAvailable(settings, game))
            {
                await GameDialogService.ShowMessageBoxAsync(
                    "Only a Windows executable was found, but no Linux Windows-runner is configured or detected.\n\n" +
                    "Install Wine/Proton, or open this app’s menu (⋯) → Launch Options → Windows Runner to pick a runner or custom command.",
                    "Windows Runner Not Found");
                return false;
            }

            game.AvailableExecutables = executables;

            if (string.IsNullOrEmpty(game.SelectedExecutable))
                game.SelectedExecutable = game.LoadSelectedExecutable(gamesFolder);

            if (executables.Count > 1 &&
                (string.IsNullOrEmpty(game.SelectedExecutable) || !executables.Contains(game.SelectedExecutable)))
            {
                game.SelectedExecutable = null;
                game.NotifyMultipleExecutablesChanged();
                return false;
            }

            var executablePath = !string.IsNullOrEmpty(game.SelectedExecutable) &&
                                 executables.Contains(game.SelectedExecutable)
                ? game.SelectedExecutable
                : executables[0];

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                !executablePath.EndsWith(".app") &&
                !needsWine)
            {
                await MakeExecutableAsync(executablePath);
            }

            var startInfo = new ProcessStartInfo();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && executablePath.EndsWith(".app"))
            {
                startInfo.FileName = "open";
                startInfo.Arguments = $"\"{executablePath}\"";
                startInfo.UseShellExecute = false;
                startInfo.WorkingDirectory = gamePath;
            }
            else if (needsWine && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var runnerCommand = WindowsRunnerService.GetWindowsRunnerCommand(settings, executablePath, gamePath, game);
                if (runnerCommand == null)
                {
                    await GameDialogService.ShowMessageBoxAsync(
                        "A Linux Windows-runner was detected earlier but is no longer available.",
                        "Windows Runner Error");
                    return false;
                }

                startInfo.UseShellExecute = false;
                startInfo.WorkingDirectory = gamePath;
                startInfo.FileName = runnerCommand.FileName;

                foreach (var argument in runnerCommand.Arguments)
                    startInfo.ArgumentList.Add(argument);

                foreach (var variable in runnerCommand.EnvironmentVariables)
                    startInfo.Environment[variable.Key] = variable.Value;
            }
            else
            {
                startInfo.FileName = executablePath;
                startInfo.WorkingDirectory = Path.GetDirectoryName(executablePath) ?? gamePath;
                startInfo.UseShellExecute = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            }

            game.UpdateLastPlayedTime(RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && executablePath.EndsWith(".app")
                ? gamePath
                : (Path.GetDirectoryName(executablePath) ?? gamePath));

            var gameProcess = Process.Start(startInfo);
            if (gameProcess == null)
            {
                await GameDialogService.ShowMessageBoxAsync(
                    $"Failed to start {game.Name}. The operating system did not create a process.",
                    "Launch Error");
                return false;
            }

            game.RaiseGameProcessStarted(gameProcess);

            if (game.GameManager != null && Avalonia.Application.Current != null)
                game.GameManager.OnPropertyChanged(nameof(GameManager.Games));

            return true;
        }
        catch (Exception ex)
        {
            if (Avalonia.Application.Current != null)
                await GameDialogService.ShowMessageBoxAsync($"Error launching {game.Name}: {ex.Message}", "Launch Error");

            return false;
        }
    }

    private static async Task MakeExecutableAsync(string executablePath)
    {
        var chmodProcess = new ProcessStartInfo
        {
            FileName = "chmod",
            Arguments = $"+x \"{executablePath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(chmodProcess);
        if (process != null)
            await process.WaitForExitAsync();
    }
}
