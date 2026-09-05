using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using System.Diagnostics;

namespace QuiverLauncher.Services;

/// <summary>
/// Opens http(s) URLs via Avalonia's launcher (works on Android) with a desktop process fallback.
/// </summary>
public static class UrlLauncher
{
    /// <summary>
    /// Starts a Linux desktop opener. Returns exit code when waited; null when the
    /// process is still running after hand-off. Throws on launch failure.
    /// </summary>
    internal delegate int? LinuxProcessStarter(ProcessStartInfo startInfo);

    public static async Task OpenAsync(string urlOrPath)
    {
        if (string.IsNullOrWhiteSpace(urlOrPath))
            throw new ArgumentException("A URL or path is required.", nameof(urlOrPath));

        if (TryGetTopLevel() is TopLevel topLevel &&
            IsWebOrMailUri(urlOrPath, out var uri))
        {
            if (await topLevel.Launcher.LaunchUriAsync(uri))
                return;
        }

        OpenWithProcess(urlOrPath);
    }

    public static void Open(string urlOrPath)
    {
        if (string.IsNullOrWhiteSpace(urlOrPath))
            throw new ArgumentException("A URL or path is required.", nameof(urlOrPath));

        // Local paths (and non-http schemes) open synchronously so callers can
        // surface failures. http(s)/mailto stay async via Avalonia's launcher.
        if (!IsWebOrMailUri(urlOrPath, out _))
        {
            OpenWithProcess(urlOrPath);
            return;
        }

        try
        {
            if (DispatcherAvailable())
            {
                _ = OpenAsync(urlOrPath);
                return;
            }
        }
        catch
        {
            // Fall through to the process helper.
        }

        OpenWithProcess(urlOrPath);
    }

    internal static bool IsWebOrMailUri(string urlOrPath, out Uri uri)
    {
        if (Uri.TryCreate(urlOrPath, UriKind.Absolute, out uri!) &&
            (uri.Scheme == Uri.UriSchemeHttp ||
             uri.Scheme == Uri.UriSchemeHttps ||
             uri.Scheme == Uri.UriSchemeMailto))
        {
            return true;
        }

        uri = null!;
        return false;
    }

    private static bool DispatcherAvailable() => Application.Current != null;

    private static TopLevel? TryGetTopLevel()
    {
        switch (Application.Current?.ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop when desktop.MainWindow != null:
                return desktop.MainWindow;
            case IActivityApplicationLifetime:
                return global::QuiverLauncher.App.TryGetHostedMainView() is { } hosted
                    ? TopLevel.GetTopLevel(hosted)
                    : null;
            case ISingleViewApplicationLifetime single when single.MainView is Visual visual:
                return TopLevel.GetTopLevel(visual);
            default:
                return null;
        }
    }

    private static void OpenWithProcess(string urlOrPath)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo(urlOrPath) { UseShellExecute = true });
            return;
        }

        if (OperatingSystem.IsLinux() && !OperatingSystem.IsAndroid())
        {
            OpenOnLinux(urlOrPath);
            return;
        }

        if (OperatingSystem.IsMacOS())
            Process.Start("open", urlOrPath);
    }

    internal static void OpenOnLinux(
        string urlOrPath,
        LinuxProcessStarter? startProcess = null,
        Func<string, bool>? fileExists = null)
    {
        startProcess ??= StartLinuxProcess;
        fileExists ??= File.Exists;

        var xdgOpen = fileExists("/usr/bin/xdg-open") ? "/usr/bin/xdg-open" : "xdg-open";
        var attempts = new (string FileName, string[] Arguments)[]
        {
            (xdgOpen, [urlOrPath]),
            ("gio", ["open", urlOrPath]),
            ("kde-open", [urlOrPath]),
            ("dolphin", [urlOrPath]),
        };

        var errors = new List<string>();
        foreach (var (fileName, arguments) in attempts)
        {
            try
            {
                var startInfo = CreateLinuxStartInfo(fileName, arguments);
                var exitCode = startProcess(startInfo);
                if (exitCode is null or 0)
                    return;

                errors.Add($"{fileName} exited with code {exitCode}");
            }
            catch (Exception ex)
            {
                errors.Add($"{fileName}: {ex.Message}");
            }
        }

        throw new InvalidOperationException(
            "Unable to open the path with xdg-open, gio, kde-open, or dolphin. " +
            string.Join("; ", errors));
    }

    internal static ProcessStartInfo CreateLinuxStartInfo(string fileName, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        HostProcessEnvironment.Sanitize(startInfo);
        return startInfo;
    }

    private static int? StartLinuxProcess(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo);
        if (process == null)
            throw new InvalidOperationException($"Failed to start '{startInfo.FileName}'.");

        // xdg-open / gio / kde-open usually exit quickly after handing off.
        if (!process.WaitForExit(3000))
            return null;

        return process.ExitCode;
    }
}
