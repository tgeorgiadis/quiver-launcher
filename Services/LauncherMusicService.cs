using System.Diagnostics;
using System.Runtime.InteropServices;
#if WINDOWS
using NAudio.Wave;
#endif

namespace QuiverLauncher.Services;

/// <summary>Owns audio devices, player processes, and fades for one launcher session.</summary>
public sealed class LauncherMusicService : IDisposable
{
    private readonly bool _enabled;
    private readonly Action<Action> _dispatch;
    private bool _disposed;
    private int _playbackGeneration;
    private CancellationTokenSource? _fadeTaskCts;
#if WINDOWS
    private IWavePlayer? _waveOut;
    private AudioFileReader? _audioFileReader;
#endif
    private Process? _musicProcess;
    private float _volume = 0.2f;
    public string Path { get; set; } = string.Empty;
    public bool IsPlaying => _musicProcess is { HasExited: false };

    public LauncherMusicService(Action<Action> dispatch, bool enabled = true)
    {
        _dispatch = dispatch;
        _enabled = enabled;
    }

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = value;
#if WINDOWS
            if (_audioFileReader != null) _audioFileReader.Volume = value;
#else
            if (!string.IsNullOrEmpty(Path) && File.Exists(Path)) Play(Path);
#endif
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
        public void Play(string path)
        {
            try
            {
                if (_disposed || !_enabled || !File.Exists(path))
                    return;

                Stop();
                Path = path;

                // Use runtime detection
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    PlayMusicWindows(path);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    PlayMusicLinux(path);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    PlayMusicMac(path);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to play launcher music: {ex.Message}");
            }
        }

        private void PlayMusicWindows(string path)
        {
            #if WINDOWS
            try
            {
                var reader = new AudioFileReader(path);
                _audioFileReader = reader;
                reader.Volume = Volume;

                var output = new WaveOutEvent();
                _waveOut = output;
                output.Init(reader);
                var generation = _playbackGeneration;

                // Enable looping
                output.PlaybackStopped += (sender, args) =>
                {
                    _dispatch(() =>
                    {
                        if (_disposed || generation != _playbackGeneration || !ReferenceEquals(_waveOut, output)) return;
                        reader.Position = 0;
                        output.Play();
                    });
                };

                _waveOut.Play();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"NAudio playback failed: {ex.Message}");
            }
        #else
            Debug.WriteLine("Windows audio playback not available on this platform");
        #endif
        }

        private void PlayMusicLinux(string path)
        {
            string[] players = { "ffplay", "mpv", "cvlc", "mplayer" };

            foreach (var player in players)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = player,
                        Arguments = player switch
                        {
                            "ffplay" => $"-nodisp -autoexit -loop 0 -volume {(int)(Volume * 100)} \"{path}\"",
                            "mpv" => $"--no-video --loop=inf --volume={Volume * 100} \"{path}\"",
                            "cvlc" => $"--no-video --loop --volume {(int)(Volume * 512)} \"{path}\"",
                            "mplayer" => $"-loop 0 -volume {(int)(Volume * 100)} \"{path}\"",
                            _ => $"\"{path}\""
                        },
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    _musicProcess = Process.Start(psi);
                    if (_musicProcess != null)
                    {
                        _musicProcess.EnableRaisingEvents = true;
                        Debug.WriteLine($"Playing music with {player}");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to start {player}: {ex.Message}");
                    continue;
                }
            }

            Debug.WriteLine("No suitable audio player found on Linux. Install one of: ffplay, mpv, vlc, mplayer");
        }

        private void PlayMusicMac(string path)
        {
            try
            {
                // afplay volume is 0-255 (0-1 range needs to be converted)
                var volumeValue = Volume * 255f;

                var psi = new ProcessStartInfo
                {
                    FileName = "afplay",
                    Arguments = $"-v {volumeValue} \"{path}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                var generation = _playbackGeneration;
                _musicProcess = Process.Start(psi);

                if (_musicProcess != null)
                {
                    _musicProcess.EnableRaisingEvents = true;
                    _musicProcess.Exited += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(Path) && File.Exists(Path))
                        {
                            try
                            {
                                _dispatch(() =>
                                {
                                    if (!_disposed && generation == _playbackGeneration && !string.IsNullOrEmpty(Path))
                                    {
                                        PlayMusicMac(Path);
                                    }
                                });
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Failed to restart music: {ex.Message}");
                            }
                        }
                    };

                    Debug.WriteLine($"Playing music with afplay at volume {volumeValue}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"afplay failed: {ex.Message}");
            }
        }

        public void Stop()
        {
            _playbackGeneration++;
            _fadeTaskCts?.Cancel();
            _fadeTaskCts?.Dispose();
            _fadeTaskCts = null;
            try
            {
                #if WINDOWS
                if (_waveOut != null)
                {
                    var output = _waveOut;
                    _waveOut = null;
                    output.Stop();
                    output.Dispose();
                }

                if (_audioFileReader != null)
                {
                    _audioFileReader.Dispose();
                    _audioFileReader = null;
                }
                #endif

                if (_musicProcess != null)
                {
                    try
                    {
                        if (!_musicProcess.HasExited)
                        {
                            _musicProcess.Kill();
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // Process already exited, ignore
                    }

                    _musicProcess.Dispose();
                    _musicProcess = null;
                }


            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to stop launcher music: {ex.Message}");
            }
        }

        public async Task FadeAsync(float targetVolume, int durationMs)
        {
            if (_disposed) return;
        #if WINDOWS
            if (_audioFileReader == null)
                return;

        // Cancel any ongoing fade
        _fadeTaskCts?.Cancel();
        _fadeTaskCts?.Dispose();
        _fadeTaskCts = new CancellationTokenSource();
        var token = _fadeTaskCts.Token;

        try
        {
            float currentVolume = _audioFileReader.Volume;
            float targetVol = targetVolume;

            if (Math.Abs(currentVolume - targetVol) < 0.001f)
                return;

            int steps = 20;
            int stepDelay = durationMs / steps;
            float volumeStep = (targetVol - currentVolume) / steps;

            for (int i = 0; i < steps; i++)
            {
                if (token.IsCancellationRequested || _audioFileReader == null)
                    return;

                currentVolume += volumeStep;
                _audioFileReader.Volume = Math.Clamp(currentVolume, 0f, 1f);

                await Task.Delay(stepDelay, token);
            }

                if (_audioFileReader != null && !token.IsCancellationRequested)
                {
                    _audioFileReader.Volume = targetVol;
                }
            }
            catch (OperationCanceledException)
            {
                // Fade was cancelled
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during music fade: {ex.Message}");
            }
            #else
                if (targetVolume < 0.01f)
                {
                    Stop();
                }
                else if (targetVolume > 0.01f)
                {
                    if (_musicProcess == null || _musicProcess.HasExited)
                    {
                        if (!string.IsNullOrEmpty(Path) && File.Exists(Path))
                        {
                            Play(Path);
                            Debug.WriteLine("Music resumed");
                        }
                    }
                }

                await Task.CompletedTask;
            #endif
        }

}
