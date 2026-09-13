using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuiverLauncher.Services;

public sealed record AndroidPackageVersion(string PackageName, long VersionCode, string? VersionName, long LastUpdateTime);
public sealed record AndroidReleaseReceipt(string ReleaseTag, AndroidPackageVersion Package);
public sealed record AndroidPendingRelease(string ReleaseTag, AndroidPackageVersion Package, long? PreviousUpdateTime);

/// <summary>Release tags and APK version names are different version namespaces.</summary>
public static class AndroidInstalledRelease
{
    private const string ReceiptFile = "android-release.json";
    private const string PendingFile = "android-release-pending.json";

    public static void Clear(string gamePath)
    {
        // Keep the package identity and any user files for a later reinstall.
        foreach (var name in new[] { ReceiptFile, PendingFile, "version.txt" })
        {
            var path = Path.Combine(gamePath, name);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    public static void Begin(string gamePath, string releaseTag, AndroidPackageVersion package, AndroidPackageVersion? previous)
    {
        // Capture an existing legacy version before an attempted update can change anything.
        if (previous != null) Resolve(gamePath, previous);
        Write(Path.Combine(gamePath, PendingFile), new AndroidPendingRelease(releaseTag, package, previous?.LastUpdateTime),
            AndroidReleaseJsonContext.Default.AndroidPendingRelease);
    }

    public static void Cancel(string gamePath)
    {
        var path = Path.Combine(gamePath, PendingFile);
        if (File.Exists(path)) File.Delete(path);
    }

    public static string? Resolve(string gamePath, AndroidPackageVersion installed)
    {
        var pending = Read(Path.Combine(gamePath, PendingFile), AndroidReleaseJsonContext.Default.AndroidPendingRelease);
        if (pending != null && SamePackageVersion(pending.Package, installed) &&
            (pending.PreviousUpdateTime == null || pending.PreviousUpdateTime != installed.LastUpdateTime))
        {
            SaveConfirmed(gamePath, pending.ReleaseTag, installed);
            Cancel(gamePath);
            return pending.ReleaseTag;
        }

        var receiptPath = Path.Combine(gamePath, ReceiptFile);
        var receipt = Read(receiptPath, AndroidReleaseJsonContext.Default.AndroidReleaseReceipt);
        if (receipt != null)
            return receipt.Package == installed ? receipt.ReleaseTag : installed.VersionName;

        // Older Quiver installations already saved the selected release tag in version.txt.
        // Associate it once with this installed package, so external updates invalidate it.
        var versionFile = Path.Combine(gamePath, "version.txt");
        if (!File.Exists(receiptPath) && pending == null && File.Exists(versionFile))
        {
            var legacy = File.ReadAllText(versionFile).Trim();
            if (!string.IsNullOrWhiteSpace(legacy))
            {
                SaveConfirmed(gamePath, legacy, installed);
                return legacy;
            }
        }
        return installed.VersionName;
    }

    private static bool SamePackageVersion(AndroidPackageVersion a, AndroidPackageVersion b) =>
        a.PackageName == b.PackageName && a.VersionCode == b.VersionCode && a.VersionName == b.VersionName;

    private static void SaveConfirmed(string gamePath, string tag, AndroidPackageVersion package)
    {
        Write(Path.Combine(gamePath, ReceiptFile), new AndroidReleaseReceipt(tag, package),
            AndroidReleaseJsonContext.Default.AndroidReleaseReceipt);
        File.WriteAllText(Path.Combine(gamePath, "version.txt"), tag);
    }

    private static T? Read<T>(string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), type) : default; }
        catch (IOException) { return default; }
        catch (JsonException) { return default; }
    }

    private static void Write<T>(string path, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(value, type));
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

[JsonSerializable(typeof(AndroidReleaseReceipt))]
[JsonSerializable(typeof(AndroidPendingRelease))]
internal partial class AndroidReleaseJsonContext : JsonSerializerContext;
