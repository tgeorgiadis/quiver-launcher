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
    public static async Task OpenAsync(string urlOrPath)
    {
        if (string.IsNullOrWhiteSpace(urlOrPath))
            throw new ArgumentException("A URL or path is required.", nameof(urlOrPath));

        if (TryGetTopLevel() is TopLevel topLevel &&
            Uri.TryCreate(urlOrPath, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp ||
             uri.Scheme == Uri.UriSchemeHttps ||
             uri.Scheme == Uri.UriSchemeMailto))
        {
            if (await topLevel.Launcher.LaunchUriAsync(uri))
                return;
        }

        OpenWithProcess(urlOrPath);
    }

    public static void Open(string urlOrPath)
    {
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
            Process.Start("xdg-open", urlOrPath);
            return;
        }

        if (OperatingSystem.IsMacOS())
            Process.Start("open", urlOrPath);
    }
}
