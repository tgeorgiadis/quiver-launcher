using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace QuiverLauncher.Services;

public sealed record FlatpakReceipt(string ApplicationId, string Architecture, string Branch,
    string Commit, string ReleaseTag, int SchemaVersion = 1, string Scope = "user")
{
    [JsonIgnore] public string Reference => $"app/{ApplicationId}/{Architecture}/{Branch}";
    public void Validate()
    {
        if (SchemaVersion != 1 || Scope != "user" ||
            !Regex.IsMatch(ApplicationId ?? "", @"^[A-Za-z_][A-Za-z0-9_-]*(\.[A-Za-z_][A-Za-z0-9_-]*){2,}$") ||
            !Regex.IsMatch(Architecture ?? "", @"^[A-Za-z0-9_]+$") ||
            !Regex.IsMatch(Branch ?? "", @"^[A-Za-z0-9][A-Za-z0-9._-]*$") ||
            !Regex.IsMatch(Commit ?? "", @"^[a-fA-F0-9]{64}$") || ReleaseTag == null)
            throw new InvalidDataException("Invalid Flatpak installation record. Restore the app's metadata before managing this installation.");
    }
}

public sealed record FlatpakProcessResult(int ExitCode, string Output, string Error);
public interface IFlatpakProcessRunner
{
    Task<FlatpakProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken token = default);
}

public sealed class FlatpakProcessRunner : IFlatpakProcessRunner
{
    public async Task<FlatpakProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken token = default)
    {
        if (!OperatingSystem.IsLinux() || OperatingSystem.IsAndroid())
            throw new PlatformNotSupportedException("Flatpak operations require Linux.");
        using var process = Process.Start(FlatpakService.StartInfo(arguments))
            ?? throw new InvalidOperationException("Could not start Flatpak.");
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().ConfigureAwait(false);
            throw;
        }
        return new(process.ExitCode, await output, await error);
    }
}

public sealed record FlatpakInstalledState(FlatpakReceipt Receipt, bool Installed, string Version);

/// <summary>Flatpak owns applications and saves; Quiver owns release receipts.</summary>
public sealed class FlatpakService
{
    public const string ReceiptFileName = "flatpak-install.json";
    public const string PendingFileName = "flatpak-install.pending.json";
    public const string SetupGuidance = "Flatpak support is not available. Install Flatpak (including libflatpak) using your Linux distribution's software manager, then retry. Setup instructions: https://flatpak.org/setup/";
    public static FlatpakService Current { get; } = new(new FlatpakProcessRunner(), new FlatpakBundleReader());
    private readonly IFlatpakProcessRunner _runner;
    private readonly IFlatpakBundleReader _bundles;
    private readonly string _ownersRoot;
    private readonly Action<string, FlatpakReceipt> _writeReceipt;

    public FlatpakService(IFlatpakProcessRunner runner, IFlatpakBundleReader bundles,
        string? ownersRoot = null, Action<string, FlatpakReceipt>? writeReceipt = null)
    {
        _runner = runner;
        _bundles = bundles;
        // Shared by portable Quiver copies, because per-user Flatpak installations are shared too.
        _ownersRoot = ownersRoot ?? Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"),
            "QuiverLauncher", "flatpak-owners");
        _writeReceipt = writeReceipt ?? WriteReceipt;
    }

    public static bool HasReceipt(string gamePath) => File.Exists(Path.Combine(gamePath, ReceiptFileName)) ||
        File.Exists(Path.Combine(gamePath, PendingFileName));

    public static FlatpakReceipt? ReadReceipt(string gamePath, bool pending = false)
    {
        var path = Path.Combine(gamePath, pending ? PendingFileName : ReceiptFileName);
        if (!File.Exists(path)) return null;
        var receipt = JsonSerializer.Deserialize<FlatpakReceipt>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Empty Flatpak installation record.");
        receipt.Validate();
        return receipt;
    }

    public static void WriteReceipt(string path, FlatpakReceipt receipt)
    {
        receipt.Validate();
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(receipt));
        File.Move(temporary, path, true);
    }

    public async Task CheckAvailableAsync(CancellationToken token = default)
    {
        _bundles.CheckAvailable();
        try { RequireSuccess(await _runner.RunAsync(["--version"], token)); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { throw new InvalidOperationException(SetupGuidance, ex); }
    }

    public async Task<FlatpakInstalledState?> GetStateAsync(string gamePath, CancellationToken token = default)
    {
        var saved = ReadReceipt(gamePath);
        var pending = ReadReceipt(gamePath, pending: true);
        var receipt = pending ?? saved;
        if (receipt == null) return null;
        var commit = await GetCommitAsync(receipt, token).ConfigureAwait(false);
        // A pending write is a recovery record, not proof that installation succeeded.
        if (pending != null && commit == pending.Commit)
            return new(pending, true, pending.ReleaseTag);
        receipt = saved ?? receipt;
        return new(receipt, commit != null, commit == null ? "" :
            commit == receipt.Commit ? receipt.ReleaseTag : "Unknown");
    }

    private async Task<string?> GetCommitAsync(FlatpakReceipt receipt, CancellationToken token)
    {
        receipt.Validate();
        // Enumeration distinguishes absent packages from package-manager failures without parsing translated errors.
        var list = await _runner.RunAsync(["list", "--user", "--app", "--columns=ref"], token);
        RequireSuccess(list);
        if (!list.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Any(line =>
            line.Trim() == receipt.Reference[4..]))
            return null;
        var info = await _runner.RunAsync(["info", "--user", "--show-commit", receipt.Reference], token);
        RequireSuccess(info);
        var commit = info.Output.Trim();
        if (!Regex.IsMatch(commit, "^[a-fA-F0-9]{64}$"))
            throw new InvalidDataException("Flatpak returned an invalid installed commit.");
        return commit;
    }

    public async Task<FlatpakReceipt> InstallAsync(string bundlePath, string releaseTag, string gamePath,
        IEnumerable<string>? otherGamePaths = null, CancellationToken token = default)
    {
        await CheckAvailableAsync(token).ConfigureAwait(false);
        var receipt = _bundles.Read(bundlePath, releaseTag);
        receipt.Validate();
        using var gate = await LockAsync(token).ConfigureAwait(false);
        var priorState = await GetStateAsync(gamePath, token).ConfigureAwait(false);
        var previous = priorState?.Receipt;
        if (previous != null && (priorState?.Installed == true || previous.ReleaseTag.Length > 0) && previous.Reference != receipt.Reference)
            throw new InvalidOperationException("This bundle belongs to a different Flatpak application or branch. Uninstall the current app before switching.");
        if (priorState?.Installed == true && LauncherVersionService.IsNewerVersion(priorState.Version, releaseTag))
            throw new InvalidOperationException("Flatpak version rollback is not supported. Uninstall the app before installing an older release; your saves will be preserved.");
        var ownerPath = OwnerPath(receipt);
        var candidates = (otherGamePaths ?? []).Concat(File.Exists(ownerPath) ? [File.ReadAllText(ownerPath)] : []);
        foreach (var candidate in candidates)
        {
            if (Path.GetFullPath(candidate) == Path.GetFullPath(gamePath)) continue;
            var other = ReadReceipt(candidate) ?? ReadReceipt(candidate, pending: true);
            if (other?.Reference == receipt.Reference && other.ReleaseTag.Length > 0 && await GetCommitAsync(other, token) != null)
                throw new InvalidOperationException($"This Flatpak is already managed by another Quiver entry: {candidate}");
        }
        Directory.CreateDirectory(gamePath);
        // Save recovery information before modifying anything in Flatpak.
        WriteReceipt(Path.Combine(gamePath, PendingFileName), receipt);
        File.WriteAllText(ownerPath, Path.GetFullPath(gamePath));
        var result = await _runner.RunAsync(["install", "--user", "--bundle", "--or-update",
            "--noninteractive", "--assumeyes", Path.GetFullPath(bundlePath)], token).ConfigureAwait(false);
        RequireSuccess(result);
        if (await GetCommitAsync(receipt, token).ConfigureAwait(false) != receipt.Commit)
            throw new InvalidOperationException("Flatpak did not install the requested bundle commit. The saved recovery record has been kept.");
        _writeReceipt(Path.Combine(gamePath, ReceiptFileName), receipt);
        File.WriteAllText(Path.Combine(gamePath, "version.txt"), releaseTag);
        File.Delete(Path.Combine(gamePath, PendingFileName));
        return receipt;
    }

    public async Task UninstallAsync(string gamePath, CancellationToken token = default)
    {
        using var gate = await LockAsync(token).ConfigureAwait(false);
        var state = await GetStateAsync(gamePath, token).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No Flatpak installation record was found.");
        var ownerPath = OwnerPath(state.Receipt);
        if (File.Exists(ownerPath) && File.ReadAllText(ownerPath) != Path.GetFullPath(gamePath))
            throw new InvalidOperationException("This Flatpak is managed by another Quiver entry.");
        if (state.Installed)
        {
            RequireSuccess(await _runner.RunAsync(["uninstall", "--user", "--noninteractive", "--assumeyes", "--no-related",
                state.Receipt.Reference], token).ConfigureAwait(false));
            if (await GetCommitAsync(state.Receipt, token).ConfigureAwait(false) != null)
                throw new InvalidOperationException("Flatpak still reports this app as installed. Its installation record has been kept.");
        }
        // Keep the identity as a marker so the remaining metadata folder never looks like a portable install.
        WriteReceipt(Path.Combine(gamePath, ReceiptFileName), state.Receipt with { ReleaseTag = "" });
        File.Delete(Path.Combine(gamePath, PendingFileName));
        File.Delete(Path.Combine(gamePath, "version.txt"));
        File.Delete(ownerPath);
    }

    public static IReadOnlyList<string> LaunchArguments(FlatpakReceipt receipt)
    {
        receipt.Validate();
        return ["run", "--user", "--arch=" + receipt.Architecture, "--branch=" + receipt.Branch, receipt.ApplicationId];
    }

    public static ProcessStartInfo StartInfo(IReadOnlyList<string> arguments, bool capture = true)
    {
        var info = new ProcessStartInfo("flatpak") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = capture, RedirectStandardError = capture,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        HostProcessEnvironment.Sanitize(info);
        if (OperatingSystem.IsLinux())
        {
            // Steam shortcuts need a stable executable path; also avoid resolving through an AppImage mount.
            info.Environment.TryGetValue("PATH", out var pathValue);
            var searchPath = pathValue ?? "/usr/local/bin:/usr/bin:/bin";
            var executable = searchPath.Split(Path.PathSeparator).Where(Path.IsPathFullyQualified)
                .Select(directory => Path.Combine(directory, "flatpak")).FirstOrDefault(File.Exists);
            info.FileName = executable ?? throw new InvalidOperationException(SetupGuidance);
        }
        return info;
    }

    public static string DataDirectory(FlatpakReceipt receipt)
    {
        receipt.Validate();
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".var", "app", receipt.ApplicationId);
    }

    private string OwnerPath(FlatpakReceipt receipt) => Path.Combine(_ownersRoot,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(receipt.Reference))) + ".txt");

    private async Task<FileStream> LockAsync(CancellationToken token)
    {
        Directory.CreateDirectory(_ownersRoot);
        var deadline = DateTime.UtcNow.AddMinutes(30);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(Path.Combine(_ownersRoot, "transaction.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < deadline) { await Task.Delay(100, token).ConfigureAwait(false); }
        }
    }

    private static void RequireSuccess(FlatpakProcessResult result)
    {
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Flatpak failed (exit {result.ExitCode}).\n{result.Error}\n{result.Output}\nIf a runtime is unavailable, configure its repository in your software manager and retry.");
    }
}
