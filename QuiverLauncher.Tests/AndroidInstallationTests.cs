using System.IO.Compression;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public sealed class AndroidInstallationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "quiver-android-install", Guid.NewGuid().ToString("N"));
    private string GamePath => Path.Combine(_root, "game");
    private static AndroidPackageVersion Package(long updated = 100, long code = 16) => new("test.battleship", code, "0.10-spike", updated);

    public AndroidInstallationTests() => Directory.CreateDirectory(GamePath);
    public void Dispose() => Directory.Delete(_root, true);

    private string Archive(params string[] entries)
    {
        var path = Path.Combine(_root, "download.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var name in entries)
        {
            using var stream = new StreamWriter(zip.CreateEntry(name).Open());
            stream.Write("apk-fixture-bytes");
        }
        return path;
    }

    [Theory]
    [InlineData("game.apk")]
    [InlineData("Android/subfolder/game.APK")]
    public async Task Archive_preparation_returns_the_contained_apk_without_touching_the_install(string entry)
    {
        File.WriteAllText(Path.Combine(GamePath, "version.txt"), "v1.5");
        var path = await AndroidPackagePreparation.PrepareAsync(Archive(entry, "readme.txt"), "Android.zip", Path.Combine(_root, "staging"));
        Path.GetExtension(path).Should().BeEquivalentTo(".apk");
        File.ReadAllText(path).Should().Be("apk-fixture-bytes");
        File.ReadAllText(Path.Combine(GamePath, "version.txt")).Should().Be("v1.5");
    }

    [Fact]
    public async Task Direct_apk_is_not_extracted_as_a_zip()
    {
        var path = Archive("AndroidManifest.xml", "classes.dex");
        (await AndroidPackagePreparation.PrepareAsync(path, "game.apk", Path.Combine(_root, "staging"))).Should().Be(path);
        Directory.Exists(Path.Combine(_root, "staging")).Should().BeFalse();
    }

    [Theory]
    [InlineData("readme.txt", "game.exe", "*no APK*")]
    [InlineData("first.apk", "second.apk", "*multiple APKs*")]
    public async Task Missing_or_ambiguous_apks_are_explained(string first, string second, string message)
    {
        var action = () => AndroidPackagePreparation.PrepareAsync(Archive(first, second), "Android.zip", Path.Combine(_root, "staging"));
        await action.Should().ThrowAsync<InvalidDataException>().WithMessage(message);
    }

    [Theory]
    [InlineData("../../outside.apk")]
    [InlineData("..\\..\\outside.apk")]
    [InlineData("nested/../../../outside.apk")]
    public async Task Archive_path_traversal_is_rejected(string entry)
    {
        var action = () => AndroidPackagePreparation.PrepareAsync(Archive(entry), "Android.zip", Path.Combine(_root, "staging"));
        await action.Should().ThrowAsync<InvalidDataException>();
        File.Exists(Path.Combine(_root, "outside.apk")).Should().BeFalse();
    }

    [Fact]
    public async Task Invalid_archive_is_not_reported_as_installed()
    {
        var path = Path.Combine(_root, "bad.zip");
        File.WriteAllText(path, "not an archive");
        var action = () => AndroidPackagePreparation.PrepareAsync(path, "Android.zip", Path.Combine(_root, "staging"));
        await action.Should().ThrowAsync<InvalidDataException>();
        File.Exists(Path.Combine(GamePath, "version.txt")).Should().BeFalse();
    }

    [Fact]
    public void Legacy_release_tag_survives_restart_even_when_apk_version_name_differs()
    {
        File.WriteAllText(Path.Combine(GamePath, "version.txt"), "v1.6");
        AndroidInstalledRelease.Resolve(GamePath, Package()).Should().Be("v1.6");
        AndroidInstalledRelease.Resolve(GamePath, Package()).Should().Be("v1.6");
    }

    [Fact]
    public void Successful_new_install_confirms_release_and_persists_it()
    {
        AndroidInstalledRelease.Begin(GamePath, "v1.6", Package(0), null);
        File.Exists(Path.Combine(GamePath, "version.txt")).Should().BeFalse();
        AndroidInstalledRelease.Resolve(GamePath, Package()).Should().Be("v1.6");
        File.ReadAllText(Path.Combine(GamePath, "version.txt")).Should().Be("v1.6");
        AndroidInstalledRelease.Resolve(GamePath, Package()).Should().Be("v1.6");
    }

    [Fact]
    public void Update_reusing_apk_version_metadata_is_confirmed_only_after_package_changes()
    {
        File.WriteAllText(Path.Combine(GamePath, "version.txt"), "v1.5");
        AndroidInstalledRelease.Begin(GamePath, "v1.6", Package(0), Package());
        AndroidInstalledRelease.Resolve(GamePath, Package()).Should().Be("v1.5");
        AndroidInstalledRelease.Resolve(GamePath, Package(200)).Should().Be("v1.6");
        AndroidInstalledRelease.Resolve(GamePath, Package(200)).Should().Be("v1.6");
    }

    [Fact]
    public void Cancelled_update_preserves_the_previous_release()
    {
        File.WriteAllText(Path.Combine(GamePath, "version.txt"), "v1.5");
        AndroidInstalledRelease.Begin(GamePath, "v1.6", Package(0), Package());
        AndroidInstalledRelease.Cancel(GamePath);
        AndroidInstalledRelease.Resolve(GamePath, Package()).Should().Be("v1.5");
        File.ReadAllText(Path.Combine(GamePath, "version.txt")).Should().Be("v1.5");
    }

    [Fact]
    public void Cancelled_first_install_does_not_create_an_installed_version()
    {
        AndroidInstalledRelease.Begin(GamePath, "v1.6", Package(0), null);
        AndroidInstalledRelease.Cancel(GamePath);
        File.Exists(Path.Combine(GamePath, "version.txt")).Should().BeFalse();
    }

    [Fact]
    public void External_package_update_does_not_reuse_an_old_release_tag()
    {
        File.WriteAllText(Path.Combine(GamePath, "version.txt"), "v1.6");
        AndroidInstalledRelease.Resolve(GamePath, Package()).Should().Be("v1.6");
        AndroidInstalledRelease.Resolve(GamePath, Package(200, 17)).Should().Be("0.10-spike");
        AndroidInstalledRelease.Resolve(GamePath, Package(200, 17)).Should().Be("0.10-spike");
    }

    [Fact]
    public void A_different_package_cannot_confirm_a_pending_release()
    {
        AndroidInstalledRelease.Begin(GamePath, "v1.6", Package(0), null);
        AndroidInstalledRelease.Resolve(GamePath, Package() with { PackageName = "different.package" }).Should().Be("0.10-spike");
        File.Exists(Path.Combine(GamePath, "version.txt")).Should().BeFalse();
    }
}
