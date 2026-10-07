using System.Diagnostics;
using System.ComponentModel;
using System.Text.Json;
using QuiverLauncher.Core.Models;
using QuiverLauncher.Models;

namespace QuiverLauncher.Services;

public sealed record WindowsInstallerReceipt(string Phase, string? ReleaseTag, string? ExecutablePath);

public interface IWindowsInstallerProcessRunner
{
    Task<int> RunAsync(ProcessStartInfo startInfo);
}

public sealed class WindowsInstallerProcessRunner : IWindowsInstallerProcessRunner
{
    public async Task<int> RunAsync(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows Installer could not be started.");
        // Do not kill an installer or remove its staged package while it is still running.
        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode;
    }
}

/// <summary>Windows owns installed files; Quiver owns only the receipt and launch link.</summary>
public sealed class WindowsInstallerService(IWindowsInstallerProcessRunner runner)
{
    public const string ReceiptFileName = "windows-installer.json";
    public const string InstalledAppsUri = "ms-settings:appsfeatures";
    public static WindowsInstallerService Current { get; } = new(new WindowsInstallerProcessRunner());
    public static bool HasReceipt(string path) => File.Exists(Path.Combine(path, ReceiptFileName));
    public static bool IsExecutable(string? path) => !string.IsNullOrWhiteSpace(path) &&
        Path.IsPathFullyQualified(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path);

    public static WindowsInstallerReceipt? ReadReceipt(string path)
    {
        if (!HasReceipt(path)) return null;
        var receipt = JsonSerializer.Deserialize<WindowsInstallerReceipt>(File.ReadAllText(Path.Combine(path, ReceiptFileName)));
        if (receipt == null || receipt.Phase is not ("Installing" or "NeedsExecutable" or "Linked"))
            throw new InvalidDataException("The Windows installation record is invalid. Restore Quiver's installation metadata before continuing.");
        return receipt;
    }

    public static void WriteReceipt(string path, WindowsInstallerReceipt receipt)
    {
        Directory.CreateDirectory(path);
        var target = Path.Combine(path, ReceiptFileName);
        var temporary = target + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(receipt));
        File.Move(temporary, target, overwrite: true);
    }

    public static ProcessStartInfo InstallerStartInfo(string package, string log)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msiexec.exe"))
        {
            UseShellExecute = true,
        };
        foreach (var argument in new[] { "/i", Path.GetFullPath(package), "/qf", "/norestart", "/L*V", Path.GetFullPath(log) })
            info.ArgumentList.Add(argument);
        return info;
    }

    public async Task<bool> InstallAsync(GameInfo game, string package, string version, string metadataPath,
        IGameDownloadDialogs dialogs)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("MSI installation requires Windows desktop. Choose a native download for this platform.");
        if (!await dialogs.ConfirmWindowsInstallerAsync(game.Name ?? "this app").ConfigureAwait(false)) return false;

        var previous = ReadReceipt(metadataPath);
        WriteReceipt(metadataPath, new("Installing", previous?.ReleaseTag, previous?.ExecutablePath));
        var log = Path.Combine(metadataPath, $"installer-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
        int code;
        try
        {
            code = await runner.RunAsync(InstallerStartInfo(package, log)).ConfigureAwait(false);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            RestorePrevious(metadataPath, previous);
            return false;
        }
        catch
        {
            RestorePrevious(metadataPath, previous);
            throw;
        }
        if (code != 0 && code != 3010)
        {
            RestorePrevious(metadataPath, previous);
            if (code == 1602) return false;
            throw new InvalidOperationException($"Windows Installer exited with code {code}. Installation log: {log}");
        }

        var executable = IsExecutable(previous?.ExecutablePath) ? previous!.ExecutablePath : null;
        WriteReceipt(metadataPath, new(executable == null ? "NeedsExecutable" : "Linked", version, executable));
        ApplyState(game, metadataPath);
        if (code == 3010)
            await dialogs.ShowErrorAsync("Installation completed. Windows reports that a restart is required. Restart when convenient.", "Restart Required").ConfigureAwait(false);
        if (executable == null) await SelectExecutableAsync(game, metadataPath, dialogs).ConfigureAwait(false);
        return true;
    }

    private static void RestorePrevious(string path, WindowsInstallerReceipt? previous)
    {
        if (previous == null) File.Delete(Path.Combine(path, ReceiptFileName));
        else WriteReceipt(path, previous);
    }

    public static void ApplyState(GameInfo game, string metadataPath)
    {
        var receipt = ReadReceipt(metadataPath);
        game.IsWindowsInstaller = receipt != null;
        if (receipt == null) return;
        game.SelectedExecutable = IsExecutable(receipt.ExecutablePath) ? receipt.ExecutablePath : null;
        game.InstalledVersion = receipt.ReleaseTag ?? "";
        game.Status = game.SelectedExecutable == null ? GameStatus.NeedsExecutable : GameStatus.Installed;
        if (game.Status == GameStatus.Installed) game.RefreshInstalledStatus();
    }

    public static async Task SelectExecutableAsync(GameInfo game, string metadataPath, IGameDownloadDialogs dialogs)
    {
        var receipt = ReadReceipt(metadataPath) ?? throw new InvalidOperationException("No Windows installation record was found.");
        var selected = await dialogs.PickWindowsExecutableAsync(game.Name ?? "this app", receipt.ExecutablePath).ConfigureAwait(false);
        if (selected != null)
        {
            if (!IsExecutable(selected)) throw new InvalidOperationException("Choose an existing Windows .exe file.");
            WriteReceipt(metadataPath, receipt with { Phase = "Linked", ExecutablePath = Path.GetFullPath(selected) });
        }
        ApplyState(game, metadataPath);
    }
}
