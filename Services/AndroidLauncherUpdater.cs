using System.ComponentModel;
using System.Net;
using System.Text.Json;

namespace QuiverLauncher.Services;

public enum AndroidLauncherUpdateState { Idle, Checking, Available, Downloading, Ready, AwaitingInstallation, Failed }
public sealed record LauncherApkIdentity(string PackageName, long VersionCode, string VersionName);
public sealed record AndroidLauncherRelease(string Version, string DownloadUrl, string NotesUrl, long Size);

public interface IAndroidLauncherInstaller
{
    LauncherApkIdentity Installed { get; }
    Task<LauncherApkIdentity> ValidateAsync(string path, string expectedVersion);
    // Returns after handoff/cancellation. Success is reconciled against Installed on resume/startup.
    Task InstallAsync(string path);
}

public sealed class AndroidLauncherUpdateCache
{
    public DateTimeOffset? LastCheck { get; set; }
    public bool LastCheckSucceeded { get; set; }
    public DateTimeOffset? RetryAt { get; set; }
    public string? ETag { get; set; }
    public bool IncludePreview { get; set; }
    public AndroidLauncherRelease? Release { get; set; }
    public string? DismissedVersion { get; set; }
    public string? StagedVersion { get; set; }
    public LauncherApkIdentity? StagedIdentity { get; set; }
    public bool InstallationHandedOff { get; set; }
}

/// <summary>Process-owned Android self updater. UI/activity lifetimes never own its downloads.</summary>
public sealed class AndroidLauncherUpdater : INotifyPropertyChanged, IDisposable
{
    public static AndroidLauncherUpdater? Current { get; set; }
    private readonly HttpClient _http;
    private readonly string _directory;
    private readonly Func<AppSettings> _settings;
    private readonly Func<DateTimeOffset> _now;
    private readonly Action<Action> _dispatch;
    private readonly Action _backupUserData;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _saveGate = new();
    private CancellationTokenSource? _download;
    private readonly CancellationTokenSource _lifetime = new();
    private AndroidLauncherUpdateCache _cache;
    public IAndroidLauncherInstaller Installer { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public AndroidLauncherUpdateState State { get; private set; }
    public string? Error { get; private set; }
    public bool LastFailureWasCheck { get; private set; }
    public double Progress { get; private set; }
    public bool HasUpdate => _cache.Release is { } release && AndroidLauncherPackageValidation.IsNewerVersion(release.Version, Installer.Installed.VersionName);
    public bool IsBusy => State is AndroidLauncherUpdateState.Checking or AndroidLauncherUpdateState.Downloading or AndroidLauncherUpdateState.AwaitingInstallation;
    public bool IsDownloading => State == AndroidLauncherUpdateState.Downloading;
    public bool CanUpdate => HasUpdate && !IsBusy;
    public bool CanCheck => !IsBusy;
    public bool HasError => !string.IsNullOrEmpty(Error);
    public bool ShowBanner => HasUpdate && (_cache.DismissedVersion != _cache.Release?.Version || IsDownloading || State == AndroidLauncherUpdateState.AwaitingInstallation);
    public string InstalledText => $"Installed version: {Installer.Installed.VersionName}";
    public string AvailableText => HasUpdate ? $"Quiver {_cache.Release!.Version} is available" : !_cache.LastCheckSucceeded || HasError ? "Check for Quiver updates" : "Quiver is up to date";
    public string? AvailableVersion => HasUpdate ? _cache.Release?.Version : null;
    public string LastCheckedText => _cache.LastCheck is {} date ? $"Last checked {date.ToLocalTime():g}" : "Not checked yet";
    public string ActionText => HasStagedApk ? "Install update" : "Update";
    public string StatusText => State switch
    {
        AndroidLauncherUpdateState.Checking => "Checking for updates…",
        AndroidLauncherUpdateState.Downloading => $"Downloading update… {Progress:0}%",
        AndroidLauncherUpdateState.AwaitingInstallation => "Finish in Android’s installer. Quiver may close; reopen it after updating.",
        AndroidLauncherUpdateState.Ready => "Update downloaded. Quiver may close during installation; reopen it afterward.",
        _ => ""
    };
    public string? NotesUrl => _cache.Release?.NotesUrl;
    public bool HasStatus => StatusText.Length > 0;
    public bool HasNotes => NotesUrl != null;
    private string CachePath => Path.Combine(_directory, "update.json");
    private string ApkPath => Path.Combine(_directory, "quiver-update.apk");
    private string PartialPath => ApkPath + ".partial";
    private bool HasStagedApk => _cache.StagedVersion == _cache.Release?.Version && _cache.StagedIdentity != null && File.Exists(ApkPath);

    public AndroidLauncherUpdater(HttpClient http, string directory, IAndroidLauncherInstaller installer,
        Func<AppSettings> settings, Action<Action>? dispatch = null, Func<DateTimeOffset>? now = null,
        Action? backupUserData = null)
    {
        _backupUserData = backupUserData ?? (() => LauncherUpdateBackup.Create());
        _http = http; _directory = directory; Installer = installer; _settings = settings;
        _dispatch = dispatch ?? (action => action()); _now = now ?? (() => DateTimeOffset.UtcNow);
        Directory.CreateDirectory(directory);
        try { _cache = JsonSerializer.Deserialize<AndroidLauncherUpdateCache>(File.ReadAllText(CachePath)) ?? new(); }
        catch { _cache = new(); }
        DeletePartial();
        ReconcileInstalled();
        State = HasStagedApk ? AndroidLauncherUpdateState.Ready : HasUpdate ? AndroidLauncherUpdateState.Available : AndroidLauncherUpdateState.Idle;
    }

    private void Notify() => _dispatch(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)));
    private void Save()
    {
        lock (_saveGate)
        {
        var temp = CachePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_cache));
        File.Move(temp, CachePath, true);
        }
    }
    private void DeletePartial() { if (File.Exists(PartialPath)) File.Delete(PartialPath); }
    private void ClearStaged()
    {
        _cache.StagedVersion = null; _cache.StagedIdentity = null; _cache.InstallationHandedOff = false;
        if (File.Exists(ApkPath)) File.Delete(ApkPath);
    }
    private void ReconcileInstalled()
    {
        if (_cache.Release != null && !HasUpdate) { ClearStaged(); _cache.Release = null; _cache.ETag = null; Save(); }
        else if (_cache.StagedVersion != null && !HasStagedApk) { ClearStaged(); Save(); }
    }
    public void Dismiss()
    {
        _cache.DismissedVersion = _cache.Release?.Version;
        try { Save(); } catch (Exception ex) { Error = ex.Message; }
        Notify();
    }
    public void CancelDownload() => _download?.Cancel();

    public async Task CheckAsync(bool manual = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Resume notifications during a live permission/installer handoff must not start another operation.
        if (!await _gate.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            ReconcileInstalled();
            var now = _now();
            var settings = _settings();
            var previews = VelopackUpdateService.EffectiveIncludePrerelease(Installer.Installed.VersionName, settings.AllowPrereleaseLauncherUpdates);
            var channelChanged = previews != _cache.IncludePreview;
            if (_cache.RetryAt > now)
            {
                if (manual) { Error = $"Update checks paused. Try again after {_cache.RetryAt.Value.ToLocalTime():g}."; LastFailureWasCheck = true; }
                return;
            }
            if (!manual && !channelChanged && now - _cache.LastCheck < TimeSpan.FromHours(6)) return;
            State = AndroidLauncherUpdateState.Checking; Error = null; Notify();
            var url = $"https://api.github.com/repos/{QuiverLauncherProfile.Instance.Repository}/releases?per_page=100";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(QuiverLauncherProfile.Instance.UpdaterUserAgent);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            if (!channelChanged && _cache.ETag != null) request.Headers.TryAddWithoutValidation("If-None-Match", _cache.ETag);
            using var checkTimeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
            checkTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var response = await _http.SendAsync(request, checkTimeout.Token).ConfigureAwait(false);
            _cache.LastCheck = now;
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                var retry = response.Headers.RetryAfter;
                _cache.RetryAt = retry?.Date ?? now + (retry?.Delta ?? TimeSpan.FromHours(1));
                if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values) && long.TryParse(values.FirstOrDefault(), out var epoch))
                    _cache.RetryAt = DateTimeOffset.FromUnixTimeSeconds(epoch) > now ? DateTimeOffset.FromUnixTimeSeconds(epoch) : _cache.RetryAt;
                throw new HttpRequestException($"Update checks paused. Try again after {_cache.RetryAt.Value.ToLocalTime():g}.");
            }
            if (response.StatusCode != HttpStatusCode.NotModified)
            {
                response.EnsureSuccessStatusCode();
                var release = SelectRelease(await response.Content.ReadAsStringAsync(checkTimeout.Token).ConfigureAwait(false), Installer.Installed.VersionName, previews);
                if (release != _cache.Release) ClearStaged();
                _cache.Release = release; _cache.ETag = response.Headers.ETag?.ToString();
                _cache.IncludePreview = previews;
            }
            _cache.RetryAt = null;
            _cache.LastCheckSucceeded = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Error = ex.Message; LastFailureWasCheck = true; _cache.LastCheck = _now(); _cache.LastCheckSucceeded = false; }
        finally
        {
            State = HasError ? AndroidLauncherUpdateState.Failed : HasStagedApk ? AndroidLauncherUpdateState.Ready : HasUpdate ? AndroidLauncherUpdateState.Available : AndroidLauncherUpdateState.Idle;
            try { Save(); } catch (Exception ex) { Error = ex.Message; State = AndroidLauncherUpdateState.Failed; }
            _gate.Release(); Notify();
        }
    }

    public static AndroidLauncherRelease? SelectRelease(string json, string installed, bool previews)
    {
        using var document = JsonDocument.Parse(json);
        AndroidLauncherRelease? best = null;
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            if (entry.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            var version = entry.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
            if (string.IsNullOrWhiteSpace(version) || !AndroidLauncherPackageValidation.IsNewerVersion(version, installed)) continue;
            if (!previews && ((entry.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) || LauncherVersionService.LooksLikePrereleaseTag(version))) continue;
            if (best != null && !AndroidLauncherPackageValidation.IsNewerVersion(version, best.Version)) continue;
            if (!entry.TryGetProperty("assets", out var assets)) continue;
            foreach (var asset in assets.EnumerateArray())
            {
                if (!asset.TryGetProperty("name", out var name) || name.GetString() != "QuiverLauncher-android.apk") continue;
                var download = asset.TryGetProperty("browser_download_url", out var link) ? link.GetString() : null;
                if (!Uri.TryCreate(download, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com" ||
                    !uri.AbsolutePath.StartsWith($"/{QuiverLauncherProfile.Instance.Repository}/releases/download/", StringComparison.OrdinalIgnoreCase)) continue;
                best = new(version, download!, $"https://github.com/{QuiverLauncherProfile.Instance.Repository}/releases/tag/{Uri.EscapeDataString(version)}",
                    asset.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : 0);
                break;
            }
        }
        return best;
    }

    public async Task UpdateAsync()
    {
        if (!await _gate.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            if (!HasUpdate) return;
            Error = null;
            LastFailureWasCheck = false;
            var release = _cache.Release!;
            if (!HasStagedApk)
            {
                _download = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                State = AndroidLauncherUpdateState.Downloading; Progress = 0; Notify();
                using var response = await _http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, _download.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.RequestMessage?.RequestUri?.Scheme != "https") throw new InvalidDataException("The update download must use HTTPS.");
                var total = response.Content.Headers.ContentLength ?? release.Size;
                await using (var source = await response.Content.ReadAsStreamAsync(_download.Token).ConfigureAwait(false))
                await using (var target = File.Create(PartialPath))
                {
                    var buffer = new byte[81920]; long received = 0; int count;
                    while ((count = await source.ReadAsync(buffer, _download.Token).ConfigureAwait(false)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, count), _download.Token).ConfigureAwait(false);
                        received += count;
                        var progress = total > 0 ? Math.Min(100, received * 100d / total) : 0;
                        if (progress - Progress >= 1) { Progress = progress; Notify(); }
                    }
                    if (received == 0 || (total > 0 && received != total) || (release.Size > 0 && received != release.Size))
                        throw new InvalidDataException("The update download was incomplete. Retry the download.");
                }
                _download.Token.ThrowIfCancellationRequested();
                var identity = await Installer.ValidateAsync(PartialPath, release.Version).ConfigureAwait(false);
                _download.Token.ThrowIfCancellationRequested();
                File.Move(PartialPath, ApkPath, true);
                _cache.StagedVersion = release.Version; _cache.StagedIdentity = identity; Save();
            }
            try { await Installer.ValidateAsync(ApkPath, release.Version).ConfigureAwait(false); }
            catch { ClearStaged(); throw; }
            _download?.Dispose(); _download = null;
            _backupUserData();
            _cache.InstallationHandedOff = true; Save();
            State = AndroidLauncherUpdateState.AwaitingInstallation; Notify();
            await Installer.InstallAsync(ApkPath).ConfigureAwait(false);
            ReconcileInstalled();
        }
        catch (OperationCanceledException) { Error = null; }
        catch (Exception ex) { Error = ex.Message; }
        finally
        {
            _download?.Dispose(); _download = null;
            try { DeletePartial(); _cache.InstallationHandedOff = false; Save(); }
            catch (Exception ex) { Error = ex.Message; }
            State = HasError ? AndroidLauncherUpdateState.Failed : HasStagedApk ? AndroidLauncherUpdateState.Ready : HasUpdate ? AndroidLauncherUpdateState.Available : AndroidLauncherUpdateState.Idle;
            _gate.Release(); Notify();
        }
    }
    public void Dispose() { _lifetime.Cancel(); _download?.Cancel(); }
}
