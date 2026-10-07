using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace QuiverLauncher.Services;

/// <summary>
/// Anonymous usage data and errors, sent to the same PostHog project as quiverlauncher.com and the v4
/// launcher. Off until the player says yes (Settings → General → Usage data, or the prompt once after
/// starting). Events carry a random install id and what this copy of the launcher is (version, OS),
/// never a name, and nothing that looks like a file or folder or the computer's user name (<see cref="Scrub"/>).
///
/// Without a project token nothing is sent and neither the prompt nor the setting shows. Release builds
/// get it from the POSTHOG_PROJECT_TOKEN variable at build time; QUIVER_POSTHOG_PROJECT_TOKEN and
/// QUIVER_POSTHOG_HOST override it when running a build by hand.
/// </summary>
public sealed class Telemetry : IDisposable
{
    /// <summary>PostHog's US cloud, where the website's and the v4 launcher's events go too.</summary>
    public const string DefaultHost = "https://us.i.posthog.com";
    /// <summary>Tells this launcher's events apart from the website's ("website") and v4's ("launcher").</summary>
    public const string AppName = "launcher-csharp";
    const int MaxQueued = 500;
    const int BatchSize = 50;
    const int MaxExceptionsPerSession = 25;
    static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(30);

    public static Telemetry Current { get; set; } = new(BuildToken(), Environment.GetEnvironmentVariable("QUIVER_POSTHOG_HOST"));

    readonly string? _token;
    readonly string _host;
    readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;
    readonly object _gate = new();
    readonly List<JsonObject> _pending = [];
    readonly HashSet<string> _seenExceptions = [];
    readonly SemaphoreSlim _flushGate = new(1, 1);
    readonly string _sessionId = Guid.CreateVersion7().ToString();
    readonly Timer _timer;
    bool _enabled;
    string? _installId;
    JsonObject _context = new();
    string? _appsDirectory;
    string? _dataDirectory;
    Dictionary<string, object?>? _startup;
    bool _startupSent;
    int _exceptions;

    static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    public Telemetry(string? token, string? host = null,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? send = null)
    {
        _token = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
        _host = string.IsNullOrWhiteSpace(host) ? DefaultHost : host.Trim().TrimEnd('/');
        _send = send ?? ((request, token) => SharedClient.SendAsync(request, token));
        _timer = new Timer(_ => _ = FlushAsync(), null, Timeout.Infinite, Timeout.Infinite);
        _context = BuildContext();
    }

    /// <summary>Whether this build can send anything: it has a project token (and a test token never reaches PostHog itself).</summary>
    public bool Available => _token != null && !(_token.StartsWith("phc_test", StringComparison.OrdinalIgnoreCase) && IsPostHogCloud(_host));

    /// <summary>Sending: available, and the player said yes.</summary>
    public bool Enabled
    {
        get { lock (_gate) return Available && _enabled && _installId != null; }
    }

    /// <summary>Follows the player's choice. Turning it off drops whatever hasn't been sent yet.</summary>
    public void Configure(bool enabled, string? installId)
    {
        bool start;
        lock (_gate)
        {
            _enabled = enabled && !string.IsNullOrWhiteSpace(installId);
            _installId = _enabled ? installId : null;
            if (!_enabled)
                _pending.Clear();
            start = Enabled;
        }
        _timer.Change(start ? FlushInterval : Timeout.InfiniteTimeSpan, start ? FlushInterval : Timeout.InfiniteTimeSpan);
        if (start)
            SendStartupIfNeeded();
    }

    /// <summary>The folders apps install to and the launcher keeps its data in, taken out of anything sent like the home folder is.</summary>
    public void SetFolders(string? appsDirectory, string? dataDirectory)
    {
        lock (_gate)
        {
            _appsDirectory = string.IsNullOrWhiteSpace(appsDirectory) ? null : appsDirectory.TrimEnd('/', '\\');
            _dataDirectory = string.IsNullOrWhiteSpace(dataDirectory) ? null : dataDirectory.TrimEnd('/', '\\');
        }
    }

    /// <summary>The start of this session, sent now or as soon as the player says yes.</summary>
    public void TrackStartup(IReadOnlyDictionary<string, object?> properties)
    {
        lock (_gate) _startup = new(properties);
        SendStartupIfNeeded();
    }

    void SendStartupIfNeeded()
    {
        Dictionary<string, object?>? startup;
        lock (_gate)
        {
            if (_startupSent || _startup == null || !Enabled) return;
            _startupSent = true;
            startup = _startup;
        }
        Track("launcher_started", startup);
    }

    /// <summary>Records a product event; does nothing while it's off.</summary>
    public void Track(string eventName, IReadOnlyDictionary<string, object?>? properties = null)
    {
        if (!Enabled) return;
        var props = new JsonObject();
        if (properties != null)
            foreach (var (key, value) in properties)
                props[key] = ToNode(value);
        Enqueue(eventName, props);
    }

    /// <summary>Reports an error to PostHog's error tracking; does nothing while it's off.</summary>
    public void CaptureException(Exception exception, bool handled, string source, IReadOnlyDictionary<string, object?>? properties = null)
    {
        if (!Enabled) return;
        var list = new JsonArray();
        foreach (var ex in Chain(exception))
            list.Add(DescribeException(ex, handled));
        var top = list[0]!;
        // The same error over and over (a failing timer, say) is sent once a session.
        var key = $"{top["type"]}|{top["value"]}";
        lock (_gate)
        {
            if (_exceptions >= MaxExceptionsPerSession || !_seenExceptions.Add(key)) return;
            _exceptions++;
        }
        var props = new JsonObject
        {
            ["$exception_list"] = list,
            ["$exception_level"] = handled ? "error" : "fatal",
            ["source"] = source,
            ["handled"] = handled,
        };
        if (properties != null)
            foreach (var (k, value) in properties)
                props[k] = ToNode(value);
        Enqueue("$exception", props);
    }

    void Enqueue(string eventName, JsonObject properties)
    {
        var flushNow = false;
        lock (_gate)
        {
            if (!Enabled) return;
            foreach (var (key, value) in _context)
                properties[key] = value?.DeepClone();
            properties["distinct_id"] = _installId;
            properties["$session_id"] = _sessionId;
            properties["$process_person_profile"] = false;
            ScrubNode(properties);
            _pending.Add(new JsonObject
            {
                ["event"] = eventName,
                ["uuid"] = Guid.CreateVersion7().ToString(),
                ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
                ["properties"] = properties,
            });
            if (_pending.Count > MaxQueued)
                _pending.RemoveRange(0, _pending.Count - MaxQueued);
            flushNow = _pending.Count >= BatchSize || eventName == "$exception";
        }
        if (flushNow)
            _ = FlushAsync();
    }

    /// <summary>Sends what's waiting. Events that couldn't be sent (offline) wait for the next try.</summary>
    public async Task FlushAsync(CancellationToken token = default)
    {
        if (!await _flushGate.WaitAsync(0, token).ConfigureAwait(false) &&
            !await _flushGate.WaitAsync(TimeSpan.FromSeconds(20), token).ConfigureAwait(false))
            return;
        try
        {
            while (true)
            {
                List<JsonObject> batch;
                lock (_gate)
                {
                    if (_pending.Count == 0 || !Enabled) return;
                    batch = _pending.Take(BatchSize).ToList();
                }
                var body = new JsonObject
                {
                    ["api_key"] = _token,
                    ["batch"] = new JsonArray([.. batch.Select(e => (JsonNode)e.DeepClone())]),
                };
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{_host}/batch/")
                {
                    Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
                };
                using var response = await _send(request, token).ConfigureAwait(false);
                // A rejected batch (4xx) would be rejected again: drop it. Otherwise keep it for later.
                if (!response.IsSuccessStatusCode && (int)response.StatusCode >= 500)
                    return;
                lock (_gate)
                    foreach (var sent in batch)
                        _pending.Remove(sent);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Usage data not sent: {ex.Message}");
        }
        finally
        {
            _flushGate.Release();
        }
    }

    /// <summary>Last chance before the process ends (closing, or a crash): sends what's waiting, for a few seconds at most.</summary>
    public void FlushBeforeExit(TimeSpan? timeout = null)
    {
        if (!Enabled) return;
        try
        {
            using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(3));
            Task.Run(() => FlushAsync(cts.Token)).Wait(timeout ?? TimeSpan.FromSeconds(3));
        }
        catch
        {
            // Best effort only.
        }
    }

    /// <summary>Errors nobody caught, on any thread, reported before the launcher closes.</summary>
    public static void RegisterCrashHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is not Exception ex) return;
            Current.CaptureException(ex, handled: false, "unhandled");
            Current.FlushBeforeExit();
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
            Current.CaptureException(e.Exception, handled: false, "unobserved_task");
    }

    /// <summary>An app as usage data names it: a catalog app by its slug, one the player added only by where it's from.</summary>
    public static Dictionary<string, object?> AppRef(QuiverLauncher.Models.GameInfo game)
    {
        var slug = string.IsNullOrWhiteSpace(game.CatalogSlug) ? null : game.CatalogSlug;
        return new()
        {
            ["slug"] = game.IsManuallyManaged ? null : slug,
            ["source"] = game.IsManuallyManaged ? "local" : slug != null ? "catalog" : "custom",
        };
    }

    /// <summary>A short, safe reason for a failure: the message without paths or names, at most 120 characters.</summary>
    public static string ReasonOf(Exception exception) => ReasonOf(exception.Message);

    public static string ReasonOf(string message) => Truncate(Current.Scrub(message), 120);

    static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];

    static IEnumerable<Exception> Chain(Exception exception)
    {
        var seen = 0;
        var current = exception;
        while (current != null && seen++ < 5)
        {
            yield return current;
            current = current is AggregateException { InnerExceptions.Count: 1 } aggregate
                ? aggregate.InnerExceptions[0]
                : current.InnerException;
        }
    }

    static JsonObject DescribeException(Exception ex, bool handled)
    {
        var frames = new JsonArray();
        var trace = new StackTrace(ex, fNeedFileInfo: true);
        // PostHog, like Sentry, lists the oldest call first and the one that threw last.
        foreach (var frame in (trace.GetFrames() ?? []).Reverse().TakeLast(60))
        {
            var method = frame.GetMethod();
            var type = method?.DeclaringType;
            var node = new JsonObject
            {
                ["platform"] = "custom",
                ["lang"] = "csharp",
                ["function"] = method == null ? "?" : $"{type?.FullName ?? "?"}.{method.Name}",
                ["module"] = type?.Namespace,
                ["resolved"] = true,
                ["in_app"] = type?.Namespace?.StartsWith("QuiverLauncher", StringComparison.Ordinal) == true,
            };
            var file = frame.GetFileName();
            if (!string.IsNullOrEmpty(file))
            {
                // The build machine's folders are of no use; the file's own name is.
                node["filename"] = Path.GetFileName(file.Replace('\\', '/'));
                node["lineno"] = frame.GetFileLineNumber();
            }
            frames.Add(node);
        }
        return new JsonObject
        {
            ["type"] = ex.GetType().FullName ?? ex.GetType().Name,
            ["value"] = ex.Message,
            ["mechanism"] = new JsonObject { ["handled"] = handled, ["synthetic"] = false },
            ["stacktrace"] = new JsonObject { ["type"] = "raw", ["frames"] = frames },
        };
    }

    static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        JsonNode node => node.DeepClone(),
        string s => s,
        bool b => b,
        int i => i,
        long l => l,
        double d => d,
        float f => f,
        decimal m => m,
        Enum e => e.ToString(),
        DateTimeOffset t => t.ToString("O"),
        DateTime t => t.ToUniversalTime().ToString("O"),
        IEnumerable<string> items => new JsonArray([.. items.Select(item => (JsonNode?)item)]),
        _ => value.ToString(),
    };

    void ScrubNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (obj[key] is JsonValue value && value.TryGetValue<string>(out var text))
                        obj[key] = Scrub(text);
                    else
                        ScrubNode(obj[key]);
                }
                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue value && value.TryGetValue<string>(out var text))
                        array[i] = Scrub(text);
                    else
                        ScrubNode(array[i]);
                }
                break;
        }
    }

    const string PathMark = "<path>";
    /// <summary>Paths as Windows, Linux, macOS and Android write them, wherever they appear in a string (as in the v4 launcher).</summary>
    static readonly Regex[] Paths =
    [
        // file:///C:/Users/… and file:///home/…
        new(@"\bfile:\/\/[^\s""'<>]*", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // C:\Users\… or D:/Games/… (not the "p:/" in "http://")
        new(@"(?<![A-Za-z0-9])[A-Za-z]:[\\/][^\s""'<>|*?]*", RegexOptions.Compiled),
        // \\server\share\…
        new(@"\\\\[^\s""'<>|*?\\]+\\[^\s""'<>|*?]*", RegexOptions.Compiled),
        // ~/… and /home/…, /Users/…, /tmp/…, /data/… and the like (not the path of a web address)
        new(@"(?<![\w.:/-])(?:~|/(?:home|Users|root|tmp|var|private|mnt|media|opt|run|Volumes|usr|etc|srv|data|storage|sdcard|snap|nix|app|Applications|Library|proc|dev))(?:/[^\s""'<>|,;)\]]*)?(?=$|[\s""'<>|,;)\]])", RegexOptions.Compiled),
        // What's left of a Windows path with spaces in it ("C:\Program Files\…"): anything with a backslash.
        new(@"[^\s""'<>|]*\\[^\s""'<>|]*", RegexOptions.Compiled),
    ];
    static readonly Regex RepeatedPaths = new("(<path>)+", RegexOptions.Compiled);

    /// <summary>Takes paths, the home and apps folders, and the computer's user name out of a string.</summary>
    public string Scrub(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var output = text;
        string? appsDirectory, dataDirectory;
        lock (_gate) (appsDirectory, dataDirectory) = (_appsDirectory, _dataDirectory);
        foreach (var folder in new[] { appsDirectory, dataDirectory, HomeDirectory })
            if (folder is { Length: > 2 })
                output = output.Replace(folder, PathMark, StringComparison.OrdinalIgnoreCase);
        foreach (var pattern in Paths)
            output = pattern.Replace(output, PathMark);
        output = RepeatedPaths.Replace(output, PathMark);
        var user = UserName;
        if (user is { Length: >= 2 })
            output = Regex.Replace(output, $@"(?<![\p{{L}}\p{{N}}_-]){Regex.Escape(user)}(?![\p{{L}}\p{{N}}_-])", "<user>",
                RegexOptions.IgnoreCase);
        return output;
    }

    static readonly string? HomeDirectory = SafeRead(() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    static readonly string? UserName = SafeRead(() => Environment.UserName);

    static T? SafeRead<T>(Func<T> read)
    {
        try { return read(); }
        catch { return default; }
    }

    /// <summary>What every event says about this copy of the launcher.</summary>
    static JsonObject BuildContext()
    {
        var os = OperatingSystem.IsAndroid() ? "android"
            : OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsMacOS() ? "macos"
            : OperatingSystem.IsLinux() ? "linux"
            : "other";
        var version = SafeRead(() => LauncherVersionService.ReadInstalledVersion()) ?? "unknown";
        return new JsonObject
        {
            ["app"] = AppName,
            ["launcher_version"] = version,
            ["os"] = os,
            ["os_version"] = SafeRead(() => Environment.OSVersion.Version.ToString()),
            ["arch"] = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
            ["flatpak"] = SafeRead(() => HostProcessEnvironment.IsSandboxed()),
            ["steam_deck"] = SafeRead(() => SteamDeckEnvironment.IsGamingMode() || SteamDeckEnvironment.IsDesktopMode()),
            ["$os"] = os switch { "windows" => "Windows", "macos" => "Mac OS X", "linux" => "Linux", "android" => "Android", _ => os },
            ["$lib"] = "quiver-launcher-csharp",
            ["$lib_version"] = version,
        };
    }

    static bool IsPostHogCloud(string host) =>
        Uri.TryCreate(host, UriKind.Absolute, out var uri) &&
        (uri.Host.Equals("posthog.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".posthog.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>The project token: set when running by hand, else built in by a release build.</summary>
    static string? BuildToken()
    {
        var runtime = Environment.GetEnvironmentVariable("QUIVER_POSTHOG_PROJECT_TOKEN");
        if (!string.IsNullOrWhiteSpace(runtime)) return runtime;
        return typeof(Telemetry).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "PostHogProjectToken")?.Value;
    }

    public void Dispose()
    {
        _timer.Dispose();
        _flushGate.Dispose();
    }
}
