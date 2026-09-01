namespace QuiverLauncher.Services;

/// <summary>
/// Resolves the user-data root for Quiver Launcher.
/// Windows Velopack: <c>RootAppDir</c> (sibling of <c>current/</c>).
/// Linux/macOS Velopack: writable directory beside the AppImage / .app when possible;
/// otherwise XDG / Application Support.
/// </summary>
public static class QuiverLauncherPaths
{
    public const string AppName = "QuiverLauncher";

    /// <summary>Test override for the user-data root.</summary>
    public static string? OverrideUserDataRoot { get; set; }

    /// <summary>
    /// Optional provider for Velopack's <c>RootAppDir</c>. Set from startup after
    /// <c>VelopackApp.Build().Run()</c>. Used on Windows.
    /// </summary>
    public static Func<string?>? VelopackRootAppDirProvider { get; set; }

    /// <summary>
    /// Optional provider for the directory containing the Velopack package
    /// (folder with the <c>.AppImage</c>, or parent of the <c>.app</c> bundle).
    /// Used on Linux/macOS for portable sidecar library data.
    /// </summary>
    public static Func<string?>? VelopackPackageDirectoryProvider { get; set; }

    /// <summary>
    /// Optional provider for Android <c>Context.FilesDir</c>. Set from MainActivity.
    /// </summary>
    public static Func<string?>? AndroidFilesDirProvider { get; set; }

    /// <summary>Test hook to override directory writability checks.</summary>
    internal static Func<string, bool>? DirectoryWritableTester { get; set; }

    public static string UserDataRoot
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(OverrideUserDataRoot))
                return NormalizeDirectory(OverrideUserDataRoot);

            if (OperatingSystem.IsAndroid())
            {
                var androidRoot = AndroidFilesDirProvider?.Invoke();
                if (!string.IsNullOrWhiteSpace(androidRoot))
                    return NormalizeDirectory(androidRoot);
            }

            if (OperatingSystem.IsWindows())
            {
                var velopackRoot = VelopackRootAppDirProvider?.Invoke();
                if (!string.IsNullOrWhiteSpace(velopackRoot))
                    return NormalizeDirectory(velopackRoot);

                // Unpackaged / debug: keep data beside the build output.
                return NormalizeDirectory(AppDomain.CurrentDomain.BaseDirectory);
            }

            return ResolveUnixUserDataRoot(
                VelopackPackageDirectoryProvider?.Invoke(),
                GetOsFallbackUserDataRoot());
        }
    }

    /// <summary>
    /// Prefer a writable Velopack package directory (beside AppImage / .app); otherwise OS fallback.
    /// </summary>
    internal static string ResolveUnixUserDataRoot(string? packageDirectory, string fallbackRoot)
    {
        if (!string.IsNullOrWhiteSpace(packageDirectory) && IsDirectoryWritable(packageDirectory))
            return NormalizeDirectory(packageDirectory);

        return NormalizeDirectory(fallbackRoot);
    }

    public static string AppsJsonPath => Path.Combine(UserDataRoot, "apps.json");
    public static string SettingsJsonPath => Path.Combine(UserDataRoot, "settings.json");
    public static string GamesJsonPath => Path.Combine(UserDataRoot, "games.json");
    public static string LegacyGamesJsonPath => Path.Combine(UserDataRoot, "games.json");
    public static string CacheDirectory => Path.Combine(UserDataRoot, "Cache");
    public static string DefaultAppsDirectory => Path.Combine(UserDataRoot, "Apps");
    public static string CrashLogPath => Path.Combine(UserDataRoot, "crash.log");
    public static string GamepadDebugLogPath => Path.Combine(UserDataRoot, GamepadDebugLog.FileName);

    public static void EnsureUserDataRootExists()
    {
        Directory.CreateDirectory(UserDataRoot);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(DefaultAppsDirectory);
    }

    /// <summary>
    /// Resolves the portable library folder for a macOS Velopack/.app install:
    /// parent of the <c>.app</c> bundle when detectable.
    /// </summary>
    public static string? ResolveMacOsPackageDirectory(
        string? rootAppDir,
        string? appContentDir = null,
        string? fallbackStartDir = null)
    {
        var fromRoot = ParentIfAppBundle(rootAppDir);
        if (!string.IsNullOrWhiteSpace(fromRoot))
            return fromRoot;

        var fromContent = FindAppBundleParent(appContentDir);
        if (!string.IsNullOrWhiteSpace(fromContent))
            return fromContent;

        return FindAppBundleParent(fallbackStartDir);
    }

    internal static bool IsDirectoryWritable(string directory)
    {
        if (DirectoryWritableTester != null)
            return DirectoryWritableTester(directory);

        try
        {
            if (string.IsNullOrWhiteSpace(directory))
                return false;

            var full = NormalizeDirectory(directory);
            if (!Directory.Exists(full))
                Directory.CreateDirectory(full);

            var probe = Path.Combine(full, ".quiver_write_probe_" + Guid.NewGuid().ToString("N"));
            using (File.Create(probe, bufferSize: 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static string GetOsFallbackUserDataRoot()
    {
        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "Application Support",
                AppName);
        }

        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdg))
            return Path.Combine(xdg, AppName);

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local",
            "share",
            AppName);
    }

    private static string? ParentIfAppBundle(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var normalized = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (normalized.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            return Path.GetDirectoryName(normalized);

        return null;
    }

    private static string? FindAppBundleParent(string? startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
            return null;

        try
        {
            var current = new DirectoryInfo(NormalizeDirectory(startDirectory));
            while (current != null)
            {
                if (current.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                    return current.Parent?.FullName;

                current = current.Parent;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string NormalizeDirectory(string path) =>
        Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
}
