using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QuiverLauncher.Services;

/// <summary>Coordinates host focus, music, and input ownership while a launched app is running.</summary>
public sealed class LauncherForegroundController : IDisposable
{
    private readonly LauncherSession _session;
    private readonly LauncherMusicService _music;
    private readonly Func<InputService?> _input;
    private readonly Func<AppSettings> _settings;
    private readonly Func<bool> _isHostActive;
    private readonly Action _dismissTextInput;
    private readonly Action _restoreFocus;
    private readonly Func<Task> _refreshApps;
    private readonly Action<string, string> _log;
    private readonly Func<Action, Task> _dispatch;
    private readonly Func<Process, CancellationToken, Task> _waitForExit;
    private CancellationTokenSource? _processWait;
    private CancellationTokenSource? _reclaim;
    private bool _musicPaused;
    private bool _disposed;
    public bool LaunchedAppOwnsInput { get; private set; }

    public LauncherForegroundController(LauncherSession session, LauncherMusicService music,
        Func<InputService?> input, Func<AppSettings> settings, Func<bool> isHostActive,
        Action dismissTextInput, Action restoreFocus, Func<Task> refreshApps,
        Action<string, string> log, Func<Action, Task> dispatch,
        Func<Process, CancellationToken, Task>? waitForExit = null)
    {
        _session = session;
        _music = music;
        _input = input;
        _settings = settings;
        _isHostActive = isHostActive;
        _dismissTextInput = dismissTextInput;
        _restoreFocus = restoreFocus;
        _refreshApps = refreshApps;
        _log = log;
        _dispatch = dispatch;
        _waitForExit = waitForExit ?? WaitForGameExitAsync;
    }

    public void Activated()
    {
        if (_disposed || _session.IsClosed) return;
        RestoreInput();
        _ = _session.RunAsync(_refreshApps);
#if WINDOWS
        _ = _session.RunAsync(() => _music.FadeAsync(_music.Volume, 500));
#else
        if (_musicPaused)
        {
            _musicPaused = false;
            _ = _session.RunAsync(() => _music.FadeAsync(_music.Volume, 500));
        }
#endif
    }

    public void Deactivated()
    {
        if (_disposed || _session.IsClosed) return;
        var input = _input();
        input?.SetWindowActive(false);
        if (input?.ShouldKeepPollingWhenDeactivated() != true) input?.SetGamepadEnabled(false);
#if WINDOWS
        _ = _session.RunAsync(() => _music.FadeAsync(0, 500));
#else
        if (_music.IsPlaying)
        {
            _musicPaused = true;
            _ = _session.RunAsync(() => _music.FadeAsync(0, 500));
        }
#endif
    }

    public void StopMusic()
    {
        _musicPaused = false;
        _music.Stop();
    }

    public void GameStarted(Process? process)
    {
        if (_disposed || _session.IsClosed) return;
        _processWait?.Cancel();
        _reclaim?.Cancel();
        _dismissTextInput();
        LaunchedAppOwnsInput = true;
        _input()?.SetWindowActive(false);
        _input()?.SetGamepadEnabled(false);
        if (process == null)
        {
            if (_isHostActive()) RestoreInput();
            return;
        }
        var wait = CancellationTokenSource.CreateLinkedTokenSource(_session.Token);
        _processWait = wait;
        _ = _session.RunAsync(async () =>
        {
            try
            {
                try { await _waitForExit(process, wait.Token); }
                catch (OperationCanceledException) when (wait.IsCancellationRequested) { return; }
                catch (Exception) { /* The app may already have exited. */ }
                await _dispatch(() =>
                {
                    if (_disposed || wait.IsCancellationRequested || !ReferenceEquals(_processWait, wait)) return;
                    _processWait = null;
                    LaunchedAppOwnsInput = false;
                    if (_isHostActive()) RestoreInput();
                });
            }
            finally
            {
                if (ReferenceEquals(_processWait, wait)) _processWait = null;
                wait.Dispose();
            }
        });
    }

    private void RestoreInput()
    {
        _processWait?.Cancel();
        _processWait = null;
        LaunchedAppOwnsInput = false;
        var input = _input();
        input?.SetWindowActive(true);
        if (_settings().EnableGamepadInput) input?.SetGamepadEnabled(true);
        ReclaimGamepads();
        _restoreFocus();
    }

    private void ReclaimGamepads()
    {
        var input = _input();
        if (input == null || !SteamDeckEnvironment.IsGamingMode()) return;
        _reclaim?.Cancel();
        var reclaim = CancellationTokenSource.CreateLinkedTokenSource(_session.Token);
        _reclaim = reclaim;
        input.ReclaimGamepads(false);
        _log("reclaim", "scan");
        _ = _session.RunAsync(async () =>
        {
            using (reclaim)
            {
                try
                {
                    if (input.ConnectedGamepadCount > 0) return;
                    foreach (var reinitialize in new[] { false, true })
                    {
                        await Task.Delay(250, reclaim.Token);
                        if (reclaim.IsCancellationRequested || _disposed) return;
                        input.ReclaimGamepads(reinitialize);
                        _log("reclaim", reinitialize ? "reinit" : "retry");
                        if (input.ConnectedGamepadCount > 0) return;
                    }
                }
                catch (OperationCanceledException) when (reclaim.IsCancellationRequested) { }
                finally { if (ReferenceEquals(_reclaim, reclaim)) _reclaim = null; }
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _processWait?.Cancel();
        _processWait = null;
        _reclaim?.Cancel();
        _reclaim = null;
    }

    private static async Task WaitForGameExitAsync(Process process, CancellationToken token)
    {
        int? group = null;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var current = await ReadProcessGroupAsync(Environment.ProcessId, token);
            var launched = await ReadProcessGroupAsync(process.Id, token);
            if (launched != current) group = launched;
        }
        await process.WaitForExitAsync(token);
        if (group.HasValue)
            while (!string.IsNullOrWhiteSpace(await ReadPsAsync($"-o pid= -g {group.Value}", token)))
                await Task.Delay(1000, token);
    }

    private static async Task<int?> ReadProcessGroupAsync(int processId, CancellationToken token) =>
        int.TryParse((await ReadPsAsync($"-o pgid= -p {processId}", token))?.Trim(), out var group) ? group : null;

    private static async Task<string?> ReadPsAsync(string arguments, CancellationToken token)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "ps", Arguments = arguments, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            });
            if (process == null) return null;
            var output = await process.StandardOutput.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);
            return process.ExitCode == 0 ? output : null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return null; }
    }
}
