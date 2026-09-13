using Android.Content;
using Android.Content.PM;
using AndroidX.Core.Content;
using QuiverLauncher.Models;
using QuiverLauncher.Services;
using Application = Android.App.Application;
using Uri = Android.Net.Uri;

namespace QuiverLauncher.Android;

public sealed class AndroidAppInstallLaunchService : IAppInstallLaunchService
{
    private readonly Context _context;
    private readonly MainActivity _activity;
    public const int InstallRequestCode = 4127;
    public const int UninstallRequestCode = 4128;
    private static SemaphoreSlim PackageOperationGate => AndroidPackageOperations.Gate;
    private static TaskCompletionSource<bool>? _installCompletion;
    private static TaskCompletionSource<bool>? _uninstallCompletion;

    public AndroidAppInstallLaunchService(MainActivity context)
    {
        _activity = context;
        _context = context.ApplicationContext ?? context;
    }

    public static void CompleteInstall(bool success) => _installCompletion?.TrySetResult(success);
    public static void CompleteUninstall(bool success) => _uninstallCompletion?.TrySetResult(success);

    public async Task<bool> InstallAsync(GameInfo game, string downloadedPackagePath, string version, string gamePath)
    {
        if (string.IsNullOrWhiteSpace(downloadedPackagePath) || !File.Exists(downloadedPackagePath))
            return false;

        await PackageOperationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var packageInfo = _context.PackageManager?.GetPackageArchiveInfo(downloadedPackagePath, PackageInfoFlags.Activities);
            if (packageInfo?.PackageName is not { Length: > 0 } packageName)
                throw new InvalidDataException("The downloaded APK is not a valid Android package.");

            var previous = GetPackageVersion(packageName);
            AndroidInstalledRelease.Begin(gamePath, version, Describe(packageInfo), previous);
            game.AndroidPackageName = packageName;
            await File.WriteAllTextAsync(Path.Combine(gamePath, GameStatusService.AndroidPackageFileName), packageName).ConfigureAwait(false);

            var file = new Java.IO.File(downloadedPackagePath);
            var uri = FileProvider.GetUriForFile(
                _context,
                _context.PackageName + ".fileprovider",
                file);

            var intent = new Intent(Intent.ActionInstallPackage);
            intent.SetDataAndType(uri, "application/vnd.android.package-archive");
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);
            intent.PutExtra(Intent.ExtraReturnResult, true);
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _installCompletion = completion;
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => _activity.StartActivityForResult(intent, InstallRequestCode));
            // Keep the staged APK alive until Package Installer finishes reading it.
            var accepted = await completion.Task.ConfigureAwait(false);
            if (!accepted)
            {
                AndroidInstalledRelease.Cancel(gamePath);
                return false;
            }
            var confirmed = await AndroidPackageReadiness.WaitAsync(() =>
                GetPackageVersion(packageName) is { } installed && AndroidInstalledRelease.Resolve(gamePath, installed) == version).ConfigureAwait(false);
            if (!confirmed) return false;

            // Requesting an installer result can omit Android's own Open button.
            // Offer it here, without treating a successful install as a failed launch.
            try
            {
                if (await ShowInstalledPromptAsync(game).ConfigureAwait(false))
                {
                    try { await LaunchPackageAsync(game, packageName).ConfigureAwait(false); }
                    catch (Exception ex)
                    {
                        await ShowInstalledPromptAsync(game, $"Installation succeeded, but the app could not be opened: {ex.Message}\n\nYou can try Play again from your Library.").ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                // An activity closing while the optional Open prompt appears must
                // not turn an already confirmed installation into an install failure.
                System.Diagnostics.Debug.WriteLine($"Installed-app prompt unavailable: {ex.Message}");
            }
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"APK install failed: {ex.Message}");
            AndroidInstalledRelease.Cancel(gamePath);
            throw;
        }
        finally
        {
            _installCompletion = null;
            PackageOperationGate.Release();
        }
    }

    public Task<bool> LaunchAsync(GameInfo game, string gamesFolder)
    {
        if (string.IsNullOrWhiteSpace(game.AndroidPackageName))
            GameStatusService.TryRestoreAndroidPackageName(game, game.GetInstallPath(gamesFolder));

        var packageName = game.AndroidPackageName;
        if (string.IsNullOrWhiteSpace(packageName))
        {
            throw new InvalidOperationException(
                "Quiver does not know this app's Android package id. Reinstall it from Quiver, then tap Play.");
        }

        return LaunchPackageAsync(game, packageName);
    }

    private async Task<bool> LaunchPackageAsync(GameInfo game, string packageName)
    {
        Intent? launch = null;
        if (!await AndroidPackageReadiness.WaitAsync(() => (launch = _context.PackageManager?.GetLaunchIntentForPackage(packageName)) != null).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"{game.Name} is installed but has no launcher activity Quiver can start.");
        }

        launch!.AddFlags(ActivityFlags.NewTask);
        try
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => _context.StartActivity(launch));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not start {game.Name}: {ex.Message}", ex);
        }

        return true;
    }

    public async Task<bool> UninstallAsync(GameInfo game, string gamePath)
    {
        GameStatusService.TryRestoreAndroidPackageName(game, gamePath);
        var packageName = game.AndroidPackageName;
        if (string.IsNullOrWhiteSpace(packageName))
            throw new InvalidOperationException("Quiver does not know this app's Android package id. You can uninstall it from Android Settings → Apps.");

        await PackageOperationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsInstalled(game)) return true;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _uninstallCompletion = completion;
            var intent = new Intent(Intent.ActionUninstallPackage, Uri.Parse("package:" + packageName));
            intent.PutExtra(Intent.ExtraReturnResult, true);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => _activity.StartActivityForResult(intent, UninstallRequestCode));
            if (!await completion.Task.ConfigureAwait(false)) return false;
            if (!await AndroidPackageReadiness.WaitAsync(() => !IsInstalled(game)).ConfigureAwait(false))
                throw new InvalidOperationException("Android has not confirmed that the app was removed. Its saved information has been kept.");
            return true;
        }
        finally
        {
            _uninstallCompletion = null;
            PackageOperationGate.Release();
        }
    }

    private async Task<bool> ShowInstalledPromptAsync(GameInfo game, string? openError = null)
    {
        var choice = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_activity.IsFinishing || _activity.IsDestroyed) { choice.TrySetResult(false); return; }
            var builder = new global::Android.App.AlertDialog.Builder(_activity)
                .SetTitle("App installed")
                .SetMessage(openError ?? $"{game.Name} is ready to use.")
                .SetPositiveButton(openError == null ? "Open app" : "OK", (_, _) => choice.TrySetResult(openError == null));
            if (openError == null) builder.SetNegativeButton("Done", (_, _) => choice.TrySetResult(false));
            var dialog = builder.Create()!;
            dialog.DismissEvent += (_, _) => choice.TrySetResult(false);
            dialog.Show();
        });
        return await choice.Task.ConfigureAwait(false);
    }

    public bool IsInstalled(GameInfo game)
    {
        var packageName = game.AndroidPackageName;
        if (string.IsNullOrWhiteSpace(packageName))
            return false;

        try
        {
            return _context.PackageManager?.GetPackageInfo(packageName, PackageInfoFlags.Activities) != null;
        }
        catch (PackageManager.NameNotFoundException)
        {
            return false;
        }
    }

    public string? GetInstalledVersion(GameInfo game, string gamePath)
    {
        var packageName = game.AndroidPackageName;
        if (string.IsNullOrWhiteSpace(packageName))
            return game.InstalledVersion;

        try
        {
            var info = GetPackageVersion(packageName);
            return info == null ? null : AndroidInstalledRelease.Resolve(gamePath, info);
        }
        catch (PackageManager.NameNotFoundException)
        {
            return null;
        }
    }

    private AndroidPackageVersion? GetPackageVersion(string packageName)
    {
        try
        {
            var info = _context.PackageManager?.GetPackageInfo(packageName, PackageInfoFlags.Activities);
            return info == null ? null : Describe(info);
        }
        catch (PackageManager.NameNotFoundException) { return null; }
    }

    private static AndroidPackageVersion Describe(PackageInfo info) => new(info.PackageName!,
        OperatingSystem.IsAndroidVersionAtLeast(28) ? info.LongVersionCode : info.VersionCode,
        info.VersionName, info.LastUpdateTime);
}
