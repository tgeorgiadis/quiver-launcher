using Android.App;
using Android.Content;
using Android.Content.PM;
using AndroidX.Core.Content;
using Avalonia.Threading;
using QuiverLauncher.Services;
using Uri = Android.Net.Uri;

namespace QuiverLauncher.Android;

public sealed class AndroidLauncherInstaller(MainActivity activity) : IAndroidLauncherInstaller
{
    public const int PermissionRequestCode = 4130;
    public const int UpdateRequestCode = 4131;
    private TaskCompletionSource<bool>? _permission;
    private TaskCompletionSource<bool>? _installation;
    private readonly CancellationTokenSource _activityLifetime = new();
    private PackageManager Manager => activity.PackageManager!;
    private string PackageName => activity.PackageName!;
    public LauncherApkIdentity Installed => Describe(Manager.GetPackageInfo(PackageName, PackageInfoFlags.Activities)!);
    private static LauncherApkIdentity Describe(PackageInfo info) => new(info.PackageName!,
        OperatingSystem.IsAndroidVersionAtLeast(28) ? info.LongVersionCode : info.VersionCode, info.VersionName ?? "0.0.0");

    public Task<LauncherApkIdentity> ValidateAsync(string path, string expectedVersion)
    {
        var flags = OperatingSystem.IsAndroidVersionAtLeast(28) ? PackageInfoFlags.SigningCertificates : PackageInfoFlags.Signatures;
        var apk = Manager.GetPackageArchiveInfo(path, flags) ?? throw new InvalidDataException("The downloaded file is not a valid Android APK.");
        var current = Manager.GetPackageInfo(PackageName, flags)!;
        var identity = Describe(apk);
        var installed = Describe(current);
        AndroidLauncherPackageValidation.ValidateIdentity(installed, identity, expectedVersion);
        if (!HasCompatibleSigner(current, apk))
            throw new InvalidDataException("This APK was signed with a different key. It cannot update this installation of Quiver.");
        return Task.FromResult(identity);
    }

    private static bool HasCompatibleSigner(PackageInfo current, PackageInfo candidate)
    {
        static HashSet<string> Keys(IEnumerable<global::Android.Content.PM.Signature>? signatures) =>
            (signatures ?? []).Select(s => Convert.ToHexString(s.ToByteArray()!)).ToHashSet(StringComparer.Ordinal);
        if (OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            var installedKeys = Keys(current.SigningInfo?.GetApkContentsSigners());
            var candidateKeys = Keys(candidate.SigningInfo?.GetApkContentsSigners());
            return AndroidLauncherPackageValidation.HasCompatibleSigningKeys(installedKeys, candidateKeys,
                Keys(candidate.SigningInfo?.GetSigningCertificateHistory()));
        }
        var previous = Keys(current.Signatures);
        return AndroidLauncherPackageValidation.HasCompatibleSigningKeys(previous, Keys(candidate.Signatures));
    }

    public async Task InstallAsync(string path)
    {
        await AndroidPackageOperations.Gate.WaitAsync(_activityLifetime.Token).ConfigureAwait(false);
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(26) && !Manager.CanRequestPackageInstalls())
            {
                if (!await ExplainPermissionAsync().ConfigureAwait(false)) return;
                _permission = new(TaskCreationOptions.RunContinuationsAsynchronously);
                await Dispatcher.UIThread.InvokeAsync(() => activity.StartActivityForResult(
                    new Intent(global::Android.Provider.Settings.ActionManageUnknownAppSources, Uri.Parse("package:" + PackageName)), PermissionRequestCode));
                await _permission.Task.WaitAsync(_activityLifetime.Token).ConfigureAwait(false);
                if (!Manager.CanRequestPackageInstalls()) throw new InvalidOperationException("Installation permission was not granted. Tap Install update to try again.");
            }
            _installation = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var uri = FileProvider.GetUriForFile(activity, PackageName + ".fileprovider", new Java.IO.File(path));
                var intent = new Intent(Intent.ActionInstallPackage);
                intent.SetDataAndType(uri, "application/vnd.android.package-archive");
                intent.AddFlags(ActivityFlags.GrantReadUriPermission);
                intent.PutExtra(Intent.ExtraReturnResult, true);
                activity.StartActivityForResult(intent, UpdateRequestCode);
            });
            await _installation.Task.WaitAsync(_activityLifetime.Token).ConfigureAwait(false);
        }
        finally { _permission = null; _installation = null; AndroidPackageOperations.Gate.Release(); }
    }
    private async Task<bool> ExplainPermissionAsync()
    {
        var choice = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (activity.IsFinishing || activity.IsDestroyed) { choice.TrySetResult(false); return; }
            var dialog = new AlertDialog.Builder(activity)
                .SetTitle("Allow Quiver to install this update")
                .SetMessage("Enable ‘Allow from this source’ in Android Settings, then return to Quiver. Android will ask you to confirm installation.")
                .SetPositiveButton("Open settings", (_, _) => choice.TrySetResult(true))
                .SetNegativeButton("Cancel", (_, _) => choice.TrySetResult(false)).Create()!;
            dialog.DismissEvent += (_, _) => choice.TrySetResult(false);
            dialog.Show();
        });
        return await choice.Task.WaitAsync(_activityLifetime.Token).ConfigureAwait(false);
    }
    public void ActivityResult(int requestCode)
    {
        if (requestCode == PermissionRequestCode) _permission?.TrySetResult(true);
        if (requestCode == UpdateRequestCode) _installation?.TrySetResult(true);
    }
    public void Detach()
    {
        _activityLifetime.Cancel();
        _permission?.TrySetCanceled(); _installation?.TrySetCanceled();
    }
}
