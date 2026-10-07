using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace QuiverLauncher.Services;

/// <summary>Tracks the real host process group, rather than the sandbox's proxy PID.</summary>
internal static class HostGameSession
{
    private static readonly ConditionalWeakTable<Process, Task> Sessions = new();

    internal static Process? Start(ProcessStartInfo info)
    {
        if (!OperatingSystem.IsLinux() || !HostProcessEnvironment.IsSandboxed())
            return Process.Start(info);
        return StartTracked(info, Path.Combine(QuiverLauncherPaths.CacheDirectory, "HostProcesses"));
    }

    internal static Process StartTracked(ProcessStartInfo info, string directory)
    {
        Directory.CreateDirectory(directory);
        var pidFile = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".pid");
        var command = info.FileName;
        var arguments = info.ArgumentList.ToArray();
        // setsid creates a dedicated host session/group. The shell records its
        // host PID before exec, so child groups are never confused with Quiver.
        info.FileName = "setsid";
        info.ArgumentList.Clear();
        foreach (var argument in new[] { "--wait", "sh", "-c", "umask 077; printf '%s' \"$$\" > \"$1\" || exit 1; shift; exec \"$@\"", "quiver-host-game", pidFile, command }.Concat(arguments))
            info.ArgumentList.Add(argument);
        HostProcessEnvironment.RouteToHostIfSandboxed(info);
        var process = Process.Start(info) ?? throw new InvalidOperationException("Could not launch the host game.");
        Sessions.Add(process, DrainAsync(process, pidFile));
        return process;
    }

    internal static Task? ExitTask(Process process) => Sessions.TryGetValue(process, out var task) ? task : null;

    private static async Task DrainAsync(Process process, string pidFile)
    {
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            if (!File.Exists(pidFile) || !int.TryParse(await File.ReadAllTextAsync(pidFile).ConfigureAwait(false), out var group) || group <= 1)
                return;
            while (await HasLiveGroupAsync(group).ConfigureAwait(false))
                await Task.Delay(500).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Host game exit tracking failed: {ex.Message}");
        }
        finally
        {
            try { File.Delete(pidFile); }
            catch (Exception ex) { Debug.WriteLine($"Host game tracking cleanup failed: {ex.Message}"); }
        }
    }

    private static async Task<bool> HasLiveGroupAsync(int group)
    {
        var info = new ProcessStartInfo("ps") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-o", "stat=", "-g", group.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            info.ArgumentList.Add(arg);
        HostProcessEnvironment.Sanitize(info);
        HostProcessEnvironment.RouteToHostIfSandboxed(info);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not query the host game process group.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { process.Kill(); throw; }
        var text = await output.ConfigureAwait(false);
        var stderr = await error.ConfigureAwait(false);
        if (process.ExitCode is not (0 or 1)) throw new InvalidOperationException(stderr);
        // Reparented zombies can remain visible after the game has fully exited.
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Any(state => !state.TrimStart().StartsWith('Z'));
    }
}
