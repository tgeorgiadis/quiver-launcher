using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FluentAssertions;

namespace QuiverLauncher.Tests;

public class ReleasePackagingTests
{
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromMinutes(8);

    private readonly ITestOutputHelper _output;

    public ReleasePackagingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", ".."));

    [Fact]
    public void Release_workflow_does_not_bundle_apps_json_in_platform_archives()
    {
        var workflowPath = Path.Combine(RepoRoot, ".github", "workflows", "dotnet-desktop.yml");

        File.Exists(workflowPath).Should().BeTrue($"expected workflow at {workflowPath}");

        var workflow = File.ReadAllText(workflowPath);

        workflow.Should().NotContain("Copy-Item apps.json");
        workflow.Should().NotContain("cp apps.json");
    }

    [Fact]
    public void Release_workflow_uses_velopack_pack_and_not_custom_updater()
    {
        var workflowPath = Path.Combine(RepoRoot, ".github", "workflows", "dotnet-desktop.yml");
        var workflow = File.ReadAllText(workflowPath);

        workflow.Should().Contain("vpk pack");
        workflow.Should().Contain("gh release create");
        workflow.Should().Contain("--packId");
        workflow.Should().Contain("--channel");
        workflow.Should().Contain("PACK_ID: QuiverLauncher");
        workflow.Should().Contain("publish-github-release");
        workflow.Should().NotContain("Quiver.Updater/Quiver.Updater.csproj");
        workflow.Should().NotContain("Quiver.Updater.exe");
        workflow.Should().Contain("win-x64");
        workflow.Should().Contain("linux-x64");
        workflow.Should().Contain("linux-arm64");
        workflow.Should().Contain("osx-x64");
        workflow.Should().Contain("osx-arm64");
        workflow.Should().Contain("AZURE_TRUSTED_SIGNING_ENABLED");
        workflow.Should().Contain("VELOPACK_VERSION");
        workflow.Should().Contain("Assets/quiver-icon.png");
        workflow.Should().Contain("QuiverLauncher.icns");
        workflow.Should().Contain("rm -rf publish/${{ matrix.rid }}/Apps");
        workflow.Should().Contain("squashfs-tools");
        workflow.Should().Contain("continue-on-error: true");
        workflow.Should().Contain("--noInst true");
        workflow.Should().Contain("environment: signing");
        workflow.Should().Contain("## Downloads");
        workflow.Should().Contain("QuiverLauncher-win-Portable.zip");
        workflow.Should().Contain("QuiverLauncher-linux-x64.AppImage");
        workflow.Should().Contain("QuiverLauncher-linux-arm64.AppImage");
        workflow.Should().Contain("QuiverLauncher-android.apk");
        workflow.Should().Contain("macOS:** work in progress while I get the signing sorted");
        workflow.Should().Contain("-name '*.AppImage'");
        workflow.Should().Contain("-name '*.apk'");
        workflow.Should().Contain("releases.*.json");
        workflow.Should().Contain("actions/setup-java@v4");
        workflow.Should().Contain("AndroidKeyStore=true");
        workflow.Should().Contain("ApplicationDisplayVersion");
        workflow.Should().Contain("ApplicationVersion");
        workflow.Should().Contain("InstallAndroidDependencies");
        workflow.Should().Contain("ci-release.jks");
        workflow.Should().Contain("AndroidPackageFormats=apk");
        workflow.Should().Contain("file:${STORE_PASS}");
        workflow.Should().Contain("-iname '*signed*.apk'");
        workflow.Should().NotContain("env:ANDROID_KEYSTORE_PASSWORD");
        workflow.Should().Contain("needs.build-android.result == 'success'");
        workflow.Should().Contain("name: android-apk");
        // Publish bare AppImages; do not wrap in tar.gz or strip executable bit via CI chmod.
        workflow.Should().NotContain("Package AppImage as tar.gz");
        workflow.Should().NotContain("chmod +x releases/");
        workflow.Should().NotContain("find release-assets -type f -name '*.AppImage' -delete");
        workflow.Should().NotContain("QuiverLauncher-linux-x64.tar.gz");
        workflow.Should().NotContain("QuiverLauncher-linux-arm64.tar.gz");
        // Do not advertise unsigned macOS downloads in release notes.
        workflow.Should().NotContain("QuiverLauncher-osx-x64-Portable.zip");
        workflow.Should().NotContain("QuiverLauncher-osx-arm64-Portable.zip");
    }

    [Fact]
    public void Repository_includes_linux_pack_icon_png()
    {
        var iconPath = Path.Combine(RepoRoot, "Assets", "quiver-icon.png");
        File.Exists(iconPath).Should().BeTrue();
    }

    [Fact]
    public void Project_references_velopack_and_does_not_copy_apps_json()
    {
        var sharedPath = Path.Combine(RepoRoot, "QuiverLauncher.App.csproj");
        var desktopPath = Path.Combine(RepoRoot, "QuiverLauncher.Desktop", "QuiverLauncher.Desktop.csproj");
        var shared = File.ReadAllText(sharedPath);
        var desktop = File.ReadAllText(desktopPath);

        desktop.Should().Contain("Avalonia.Desktop");
        shared.Should().Contain("Velopack");
        shared.Should().NotContain(
            "<None Update=\"apps.json\">",
            "apps.json must not be copied to publish output; it is user data created at runtime");
        shared.Should().NotContain("CopyWindowsUpdater");
        desktop.Should().Contain("osx-arm64");
        desktop.Should().Contain("DestinationFiles=\"$(_AliasDir)QuiverLauncher.exe\"");
        desktop.Should().Contain("DestinationFiles=\"$(_AliasDir)QuiverLauncher\"");
        desktop.Should().Contain("!Exists('$(_AliasDir)$(AssemblyName).exe')");
    }

    [Fact]
    public void Android_excludes_system_drawing_common_from_aot_graph()
    {
        var androidPath = Path.Combine(RepoRoot, "QuiverLauncher.Android", "QuiverLauncher.Android.csproj");
        var sharedPath = Path.Combine(RepoRoot, "QuiverLauncher.App.csproj");
        var android = File.ReadAllText(androidPath);
        var shared = File.ReadAllText(sharedPath);

        android.Should().Contain("QuiverExcludeWindowsDrawing=true");
        android.Should().Contain("<ExcludeAssets>all</ExcludeAssets>");
        android.Should().Contain("System.Drawing.Common");

        shared.Should().Contain("QuiverExcludeWindowsDrawing");
        shared.Should().Contain("EXCLUDE_WINDOWS_DRAWING");
        shared.Should().Contain("bin\\android-ref\\");
        shared.Should().Contain("obj\\android-ref\\");
        shared.Should().Contain(
            "Include=\"System.Drawing.Common\" Version=\"10.0.3\" Condition=\"'$(QuiverExcludeWindowsDrawing)' != 'true'\"");
        shared.Should().NotContain(
            "<PackageReference Include=\"System.Drawing.Common\" Version=\"10.0.3\" />");
    }

    [Fact]
    public void Repository_includes_apps_json_example_for_documentation()
    {
        var examplePath = Path.Combine(RepoRoot, "apps.json.example");

        File.Exists(examplePath).Should().BeTrue();
        File.ReadAllText(examplePath).Should().Contain("\"apps\"");
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task Publish_output_does_not_include_apps_json()
    {
        var publishDir = Path.Combine(Path.GetTempPath(), "QuiverPublishTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(publishDir);

        Process? process = null;

        try
        {
            var runtimeIdentifier = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win-x64"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx-x64"
                : "linux-x64";

            var publishArgs =
                $"publish \"{Path.Combine(RepoRoot, "QuiverLauncher.Desktop", "QuiverLauncher.Desktop.csproj")}\" -c Release -r {runtimeIdentifier} --self-contained true -p:PublishTrimmed=false -o \"{publishDir}\"";

            using var timeoutCts = new CancellationTokenSource(PublishTimeout);

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = publishArgs,
                WorkingDirectory = RepoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            _output.WriteLine($"Starting publish (timeout {PublishTimeout.TotalMinutes:F0} minutes)...");
            _output.WriteLine(startInfo.FileName + " " + startInfo.Arguments);

            process = Process.Start(startInfo);
            process.Should().NotBeNull();

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            process!.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null)
                    return;

                stdout.AppendLine(e.Data);
                _output.WriteLine("[stdout] " + e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null)
                    return;

                stderr.AppendLine(e.Data);
                _output.WriteLine("[stderr] " + e.Data);
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!process.HasExited)
            {
                throw new TimeoutException(
                    $"dotnet publish did not finish within {PublishTimeout.TotalMinutes:F0} minutes.");
            }

            process.ExitCode.Should().Be(0, because: stderr.ToString());

            File.Exists(Path.Combine(publishDir, "apps.json")).Should().BeFalse(
                "release publish output must not ship a blank apps.json that could wipe user libraries on update");

            File.Exists(Path.Combine(publishDir, "Quiver.Updater.exe")).Should().BeFalse(
                "custom Quiver.Updater.exe must not ship; Velopack owns updates");
        }
        finally
        {
            if (process is { HasExited: false })
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best effort cleanup after timeout or test failure.
                }
            }

            process?.Dispose();

            if (Directory.Exists(publishDir))
                Directory.Delete(publishDir, true);
        }
    }
}
