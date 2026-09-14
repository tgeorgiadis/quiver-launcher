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

            if (FlatpakService.HasReceipt(gamePath))
                return await LaunchFlatpakAsync(game, gamePath);

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

            // A mixed release can contain both .exe files and shell launchers.
            // Runner selection belongs to the chosen file, not the candidate list.
            needsWine = OperatingSystem.IsLinux() && executablePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
            if (needsWine && !WindowsRunnerService.IsWindowsRunnerAvailable(settings, game))
            {
                await GameDialogService.ShowMessageBoxAsync(
                    "The selected Windows executable needs a Linux Windows-runner, but none is configured or detected.\n\n" +
                    "Install Wine/Proton, or open this app’s menu (⋯) → Launch Options → Windows Runner to pick a runner or custom command.",
                    "Windows Runner Not Found");
                return false;
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                !executablePath.EndsWith(".app") &&
                !needsWine)
            {
                await MakeExecutableAsync(executablePath);
            }

            var processEnvBefore = LaunchDebugReport.SnapshotProcessEnvironment();
            var gameName = game.Name ?? game.FolderName;
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

                HostProcessEnvironment.SanitizeThenApply(startInfo, runnerCommand.EnvironmentVariables);
            }
            else
            {
                startInfo.FileName = executablePath;
                startInfo.WorkingDirectory = Path.GetDirectoryName(executablePath) ?? gamePath;
                startInfo.UseShellExecute = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
                if (!startInfo.UseShellExecute)
                    HostProcessEnvironment.Sanitize(startInfo);
            }

            var startInfoEnvAfter = startInfo.UseShellExecute
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : LaunchDebugReport.SnapshotStartInfoEnvironment(startInfo);
            var selectedExecutableFile = TryReadSelectedExecutableFile(gamePath);

            game.UpdateLastPlayedTime(RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && executablePath.EndsWith(".app")
                ? gamePath
                : (Path.GetDirectoryName(executablePath) ?? gamePath));

            var gameProcess = Process.Start(startInfo);
            if (gameProcess == null)
            {
                WriteLaunchReport(
                    gameName,
                    gamePath,
                    executablePath,
                    executables,
                    selectedExecutableFile,
                    startInfo,
                    processEnvBefore,
                    startInfoEnvAfter,
                    pid: null,
                    liveProcEnviron: null);
                await GameDialogService.ShowMessageBoxAsync(
                    $"Failed to start {game.Name}. The operating system did not create a process.",
                    "Launch Error");
                return false;
            }

            var liveProcEnviron = OperatingSystem.IsLinux()
                ? LaunchDebugReport.TryReadProcEnviron(gameProcess.Id)
                : null;

            WriteLaunchReport(
                gameName,
                gamePath,
                executablePath,
                executables,
                selectedExecutableFile,
                startInfo,
                processEnvBefore,
                startInfoEnvAfter,
                gameProcess.Id,
                liveProcEnviron);

            ScheduleLaunchExitFollowUp(
                gameName,
                gamePath,
                executablePath,
                executables,
                selectedExecutableFile,
                startInfo,
                processEnvBefore,
                startInfoEnvAfter,
                gameProcess,
                liveProcEnviron);

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

    private static async Task<bool> LaunchFlatpakAsync(GameInfo game, string gamePath)
    {
        if (!OperatingSystem.IsLinux() || OperatingSystem.IsAndroid())
            throw new PlatformNotSupportedException("Flatpak apps can only be launched on Linux desktops.");
        // Resume on the caller's UI context before changing bound state or raising
        // launch/library events. Flatpak's process and metadata work remains async.
        var state = await FlatpakService.Current.GetStateAsync(gamePath);
        if (state?.Installed != true)
            throw new InvalidOperationException("This Flatpak app is no longer installed. Install it again from Quiver.");
        game.IsFlatpak = true;
        var startInfo = FlatpakService.StartInfo(FlatpakService.LaunchArguments(state.Receipt), capture: false);
        var before = LaunchDebugReport.SnapshotProcessEnvironment();
        var after = LaunchDebugReport.SnapshotStartInfoEnvironment(startInfo);
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not launch Flatpak.");
        game.UpdateLastPlayedTime(gamePath);
        var name = game.Name ?? game.FolderName ?? state.Receipt.ApplicationId;
        WriteLaunchReport(name, gamePath, state.Receipt.Reference, [], null, startInfo, before, after,
            process.Id, LaunchDebugReport.TryReadProcEnviron(process.Id));
        ScheduleLaunchExitFollowUp(name, gamePath, state.Receipt.Reference, [], null, startInfo,
            before, after, process, null);
        game.RaiseGameProcessStarted(process);
        if (game.GameManager != null && Avalonia.Application.Current != null)
            game.GameManager.OnPropertyChanged(nameof(GameManager.Games));
        return true;
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

    static string? TryReadSelectedExecutableFile(string gamePath)
    {
        try
        {
            var path = Path.Combine(gamePath, "selected_executable.txt");
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    static string BuildLaunchReport(
        string gameName,
        string gamePath,
        string chosenExecutable,
        IReadOnlyList<string> candidates,
        string? selectedExecutableFile,
        ProcessStartInfo startInfo,
        IReadOnlyDictionary<string, string> processEnvBefore,
        IReadOnlyDictionary<string, string> startInfoEnvAfter,
        int? pid,
        IReadOnlyDictionary<string, string>? liveProcEnviron,
        bool? hasExited = null,
        int? exitCode = null) =>
        LaunchDebugReport.Build(
            DateTimeOffset.UtcNow,
            gameName,
            gamePath,
            chosenExecutable,
            candidates,
            selectedExecutableFile,
            startInfo,
            processEnvBefore,
            startInfoEnvAfter,
            pid,
            liveProcEnviron,
            hasExited,
            exitCode);

    static void WriteLaunchReport(
        string gameName,
        string gamePath,
        string chosenExecutable,
        IReadOnlyList<string> candidates,
        string? selectedExecutableFile,
        ProcessStartInfo startInfo,
        IReadOnlyDictionary<string, string> processEnvBefore,
        IReadOnlyDictionary<string, string> startInfoEnvAfter,
        int? pid,
        IReadOnlyDictionary<string, string>? liveProcEnviron,
        bool? hasExited = null,
        int? exitCode = null)
    {
        var report = BuildLaunchReport(
            gameName,
            gamePath,
            chosenExecutable,
            candidates,
            selectedExecutableFile,
            startInfo,
            processEnvBefore,
            startInfoEnvAfter,
            pid,
            liveProcEnviron,
            hasExited,
            exitCode);
        LaunchDebugReport.TryWrite(gamePath, report);
    }

    static void ScheduleLaunchExitFollowUp(
        string gameName,
        string gamePath,
        string chosenExecutable,
        IReadOnlyList<string> candidates,
        string? selectedExecutableFile,
        ProcessStartInfo startInfo,
        IReadOnlyDictionary<string, string> processEnvBefore,
        IReadOnlyDictionary<string, string> startInfoEnvAfter,
        Process gameProcess,
        IReadOnlyDictionary<string, string>? liveProcEnviron)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2000);
                gameProcess.Refresh();
                var hasExited = gameProcess.HasExited;
                int? exitCode = null;
                if (hasExited)
                {
                    try
                    {
                        exitCode = gameProcess.ExitCode;
                    }
                    catch
                    {
                    }
                }

                var report = BuildLaunchReport(
                    gameName,
                    gamePath,
                    chosenExecutable,
                    candidates,
                    selectedExecutableFile,
                    startInfo,
                    processEnvBefore,
                    startInfoEnvAfter,
                    gameProcess.Id,
                    liveProcEnviron,
                    hasExited,
                    exitCode);
                LaunchDebugReport.TryRewriteGameFolder(gamePath, report);
                LaunchDebugReport.TryAppendUserData(
                    hasExited
                        ? $"[{DateTimeOffset.UtcNow:O}] pid={gameProcess.Id} HasExited=true ExitCode={exitCode}{Environment.NewLine}"
                        : $"[{DateTimeOffset.UtcNow:O}] pid={gameProcess.Id} HasExited=false{Environment.NewLine}");
            }
            catch
            {
            }
        });
    }
}
