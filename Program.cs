using Avalonia;
using Avalonia.Controls.Platform;
using QuiverLauncher.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Velopack;
using Velopack.Locators;

namespace QuiverLauncher
{
    class Program
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool FreeConsole();

        private const int ATTACH_PARENT_PROCESS = -1;

        [STAThread]
        public static int Main(string[] args)
        {
            // Must be first: Velopack install/update hooks exit inside Run().
            // Linux/macOS share packages by packId (/var/tmp or Library/Caches); disable
            // startup auto-apply so a second AppImage/.app is not silently updated.
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

            // Linux/macOS: library beside AppImage / .app when that folder is writable.
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

                int exitCode = RunCLI(args);

                if (OperatingSystem.IsWindows())
                {
                    FreeConsole();
                }

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
                    LogCrash("AppDomain.UnhandledException", ex);
            };

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                LogCrash("TaskScheduler.UnobservedTaskException", e.Exception);
                e.SetObserved();
            };
        }

        internal static void LogCrashFromUiThread(string source, Exception ex)
        {
            LogCrash(source, ex);
        }

        private static void LogCrash(string source, Exception ex)
        {
            var message = new StringBuilder();
            message.AppendLine($"[{DateTime.UtcNow:O}] {source}");
            message.AppendLine(ex.ToString());

            try
            {
                QuiverLauncherPaths.EnsureUserDataRootExists();
                File.AppendAllText(QuiverLauncherPaths.CrashLogPath, message.ToString() + Environment.NewLine);
            }
            catch
            {
                // Best-effort logging only.
            }

            Debug.WriteLine(message.ToString());
            Trace.WriteLine(message.ToString());

            try
            {
                Console.Error.WriteLine(message.ToString());
            }
            catch
            {
                // WinExe may have no console attached.
            }
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

        private static int RunCLI(string[] args)
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

            // Gamescope (Steam Deck Gaming Mode) treats separate X11 popup windows as
            // fullscreen surfaces. Embed popups in the main window so ContextMenus
            // stay composited over Quiver instead of showing a black letterbox.
            if (OperatingSystem.IsLinux())
            {
                builder = builder.With(new X11PlatformOptions { OverlayPopups = true });
            }

            return builder;
        }
    }
}
