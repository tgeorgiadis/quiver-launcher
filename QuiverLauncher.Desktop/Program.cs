using Avalonia;
using Avalonia.Controls.Platform;
using QuiverLauncher.Services;
using System.Runtime.InteropServices;
using Velopack;
using Velopack.Locators;

namespace QuiverLauncher.Desktop;

internal static class Program
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    private const int ATTACH_PARENT_PROCESS = -1;

    [STAThread]
    public static int Main(string[] args)
    {
        AppInstallLaunch.Current = new DesktopAppInstallLaunchService();

        var velopack = VelopackApp.Build();
        if (!OperatingSystem.IsWindows())
            velopack.SetAutoApplyOnStartup(false);
        velopack.Run();

        QuiverLauncherPaths.VelopackRootAppDirProvider = () =>
        {
            try
            {
                return VelopackLocator.Current?.RootAppDir;
            }
            catch
            {
                return null;
            }
        };

        QuiverLauncherPaths.VelopackPackageDirectoryProvider = ResolveVelopackPackageDirectory;
        QuiverLauncherPaths.EnsureUserDataRootExists();

        if (args.Length > 0 && args[0].StartsWith("-"))
        {
            if (OperatingSystem.IsWindows())
            {
                if (AttachConsole(ATTACH_PARENT_PROCESS))
                {
                    Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
                    Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
                }
            }

            int exitCode = RunCli(args);

            if (OperatingSystem.IsWindows())
                FreeConsole();

            return exitCode;
        }

#if DEBUG
        RegisterDebugExceptionHandlers();
#endif

        DefaultMenuInteractionHandler.MenuShowDelay = TimeSpan.Zero;

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
        return 0;
    }

#if DEBUG
    private static void RegisterDebugExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                CrashLog.Log("AppDomain.UnhandledException", ex);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Log("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }
#endif

    private static string? ResolveVelopackPackageDirectory()
    {
        try
        {
            var locator = VelopackLocator.Current;
            if (locator is null)
                return null;

            if (OperatingSystem.IsLinux() &&
                locator is LinuxVelopackLocator linux &&
                !string.IsNullOrWhiteSpace(linux.AppImagePath))
            {
                return Path.GetDirectoryName(linux.AppImagePath);
            }

            if (OperatingSystem.IsMacOS())
            {
                return QuiverLauncherPaths.ResolveMacOsPackageDirectory(
                    locator.RootAppDir,
                    locator.AppContentDir,
                    AppDomain.CurrentDomain.BaseDirectory);
            }
        }
        catch
        {
            // Not a Velopack install, or locator unavailable.
        }

        return null;
    }

    private static int RunCli(string[] args)
    {
        try
        {
            var cliHandler = new CLIHandler();
            return cliHandler.Execute(args).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

        if (OperatingSystem.IsLinux())
            builder = builder.With(new X11PlatformOptions { OverlayPopups = true });

        return builder;
    }
}
