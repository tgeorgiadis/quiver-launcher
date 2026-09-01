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

    public AndroidAppInstallLaunchService(Context context)
    {
        _context = context.ApplicationContext ?? context;
    }

    public Task<bool> InstallAsync(GameInfo game, string downloadedPackagePath, string version)
    {
        if (string.IsNullOrWhiteSpace(downloadedPackagePath) || !File.Exists(downloadedPackagePath))
            return Task.FromResult(false);

        try
        {
            var packageInfo = _context.PackageManager?.GetPackageArchiveInfo(downloadedPackagePath, PackageInfoFlags.Activities);
            if (packageInfo?.PackageName is { Length: > 0 } packageName)
            {
                game.AndroidPackageName = packageName;
                game.InstalledVersion = version;
            }

            var file = new Java.IO.File(downloadedPackagePath);
            var uri = FileProvider.GetUriForFile(
                _context,
                _context.PackageName + ".fileprovider",
                file);

            var intent = new Intent(Intent.ActionView);
            intent.SetDataAndType(uri, "application/vnd.android.package-archive");
            intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
            _context.StartActivity(intent);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"APK install failed: {ex.Message}");
            return Task.FromResult(false);
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

        var launch = _context.PackageManager?.GetLaunchIntentForPackage(packageName);
        if (launch == null)
        {
            throw new InvalidOperationException(
                $"{game.Name} is installed but has no launcher activity Quiver can start.");
        }

        launch.AddFlags(ActivityFlags.NewTask);
        try
        {
            _context.StartActivity(launch);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not start {game.Name}: {ex.Message}", ex);
        }

        return Task.FromResult(true);
    }

    public Task<bool> UninstallAsync(GameInfo game)
    {
        var packageName = game.AndroidPackageName;
        if (string.IsNullOrWhiteSpace(packageName))
            return Task.FromResult(false);

        var intent = new Intent(Intent.ActionDelete, Uri.Parse("package:" + packageName));
        intent.AddFlags(ActivityFlags.NewTask);
        _context.StartActivity(intent);
        return Task.FromResult(true);
    }

    public bool IsInstalled(GameInfo game)
    {
        var packageName = game.AndroidPackageName;
        if (string.IsNullOrWhiteSpace(packageName))
            return false;

        try
        {
            _ = _context.PackageManager?.GetPackageInfo(packageName, PackageInfoFlags.Activities);
            return true;
        }
        catch (PackageManager.NameNotFoundException)
        {
            return false;
        }
    }

    public string? GetInstalledVersion(GameInfo game)
    {
        var packageName = game.AndroidPackageName;
        if (string.IsNullOrWhiteSpace(packageName))
            return game.InstalledVersion;

        try
        {
            var info = _context.PackageManager?.GetPackageInfo(packageName, PackageInfoFlags.Activities);
            return info?.VersionName ?? game.InstalledVersion;
        }
        catch (PackageManager.NameNotFoundException)
        {
            return null;
        }
    }
}
