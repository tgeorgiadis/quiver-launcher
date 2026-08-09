using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class QuiverLauncherPathsTests : IDisposable
{
    private readonly string? _previousOverride;
    private readonly Func<string?>? _previousPackageDirProvider;
    private readonly Func<string, bool>? _previousWritableTester;

    public QuiverLauncherPathsTests()
    {
        _previousOverride = QuiverLauncherPaths.OverrideUserDataRoot;
        _previousPackageDirProvider = QuiverLauncherPaths.VelopackPackageDirectoryProvider;
        _previousWritableTester = QuiverLauncherPaths.DirectoryWritableTester;
    }

    public void Dispose()
    {
        QuiverLauncherPaths.OverrideUserDataRoot = _previousOverride;
        QuiverLauncherPaths.VelopackPackageDirectoryProvider = _previousPackageDirProvider;
        QuiverLauncherPaths.DirectoryWritableTester = _previousWritableTester;
    }

    [Fact]
    public void UserDataRoot_respects_override()
    {
        var temp = Path.Combine(Path.GetTempPath(), "QuiverLauncherPaths_" + Guid.NewGuid().ToString("N"));
        QuiverLauncherPaths.OverrideUserDataRoot = temp;

        QuiverLauncherPaths.UserDataRoot.Should().Be(Path.GetFullPath(temp));
        QuiverLauncherPaths.AppsJsonPath.Should().Be(Path.Combine(Path.GetFullPath(temp), "apps.json"));
        QuiverLauncherPaths.SettingsJsonPath.Should().Be(Path.Combine(Path.GetFullPath(temp), "settings.json"));
        QuiverLauncherPaths.DefaultAppsDirectory.Should().Be(Path.Combine(Path.GetFullPath(temp), "Apps"));
        QuiverLauncherPaths.CacheDirectory.Should().Be(Path.Combine(Path.GetFullPath(temp), "Cache"));
    }

    [Fact]
    public void ResolveMacOsPackageDirectory_returns_parent_of_app_bundle()
    {
        var portableRoot = Path.Combine(Path.GetTempPath(), "QuiverMacPortable_" + Guid.NewGuid().ToString("N"));
        var appBundle = Path.Combine(portableRoot, "Quiver.app");
        var contents = Path.Combine(appBundle, "Contents", "MacOS");
        Directory.CreateDirectory(contents);
        try
        {
            QuiverLauncherPaths.ResolveMacOsPackageDirectory(appBundle)
                .Should().Be(Path.GetFullPath(portableRoot));

            QuiverLauncherPaths.ResolveMacOsPackageDirectory(
                    rootAppDir: null,
                    appContentDir: contents)
                .Should().Be(Path.GetFullPath(portableRoot));
        }
        finally
        {
            Directory.Delete(portableRoot, recursive: true);
        }
    }

    [Fact]
    public void ResolveUnixUserDataRoot_prefers_writable_package_directory()
    {
        var packageDir = Path.Combine(Path.GetTempPath(), "QuiverPkg_" + Guid.NewGuid().ToString("N"));
        var fallback = Path.Combine(Path.GetTempPath(), "QuiverFallback_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(packageDir);
        try
        {
            QuiverLauncherPaths.DirectoryWritableTester = dir =>
                string.Equals(Path.GetFullPath(dir), Path.GetFullPath(packageDir), StringComparison.OrdinalIgnoreCase);

            QuiverLauncherPaths.ResolveUnixUserDataRoot(packageDir, fallback)
                .Should().Be(Path.GetFullPath(packageDir));
        }
        finally
        {
            Directory.Delete(packageDir, recursive: true);
        }
    }

    [Fact]
    public void ResolveUnixUserDataRoot_falls_back_when_package_directory_not_writable()
    {
        var packageDir = Path.Combine(Path.GetTempPath(), "QuiverPkgRo_" + Guid.NewGuid().ToString("N"));
        var fallback = Path.Combine(Path.GetTempPath(), "QuiverFallback_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fallback);
        try
        {
            QuiverLauncherPaths.DirectoryWritableTester = _ => false;

            QuiverLauncherPaths.ResolveUnixUserDataRoot(packageDir, fallback)
                .Should().Be(Path.GetFullPath(fallback));
        }
        finally
        {
            Directory.Delete(fallback, recursive: true);
        }
    }

    [Fact]
    public void IsDirectoryWritable_detects_writable_temp_folder()
    {
        var temp = Path.Combine(Path.GetTempPath(), "QuiverWritable_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            QuiverLauncherPaths.DirectoryWritableTester = null;
            QuiverLauncherPaths.IsDirectoryWritable(temp).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}
