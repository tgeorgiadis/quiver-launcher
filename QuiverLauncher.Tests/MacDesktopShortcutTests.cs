using System.Diagnostics;
using System.Runtime.InteropServices;
using FluentAssertions;
using QuiverLauncher.Core.Services;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public sealed class MacDesktopShortcutTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "quiver-mac-shortcuts", Guid.NewGuid().ToString("N"));
    private readonly GameInfo _game = new() { Name = "Ocarina: \"Recomp\"", FolderName = "game" };
    private string GamePath => Path.Combine(_root, "game");
    private string Desktop => Path.Combine(_root, "Desktop");

    public MacDesktopShortcutTests()
    {
        Directory.CreateDirectory(GamePath);
        Directory.CreateDirectory(Desktop);
    }

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void Saved_app_bundle_is_a_valid_executable_on_macos()
    {
        var app = Directory.CreateDirectory(Path.Combine(GamePath, "Game.app")).FullName;

        GameInstallationService.IsValidSavedExecutable(app, OSPlatform.OSX).Should().BeTrue();
        GameInstallationService.IsValidSavedExecutable(app, OSPlatform.Linux).Should().BeFalse();
        GameInstallationService.IsValidSavedExecutable(Path.Combine(GamePath, "Missing.app"), OSPlatform.OSX).Should().BeFalse();
    }

    [Fact]
    public async Task Picking_an_app_bundle_from_several_choices_creates_a_target()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Skip("App bundles are launch targets on macOS only.");

        var app = CreateAppBundle(GamePath, "Game");
        WriteScript(Path.Combine(GamePath, "server"));

        var target = await GameShortcutLaunch.PrepareAsync(_game, _root, new(), candidates =>
        {
            candidates.Should().HaveCount(2);
            return Task.FromResult<string?>(app);
        });

        target!.FileName.Should().Be(app);
        File.ReadAllText(Path.Combine(GamePath, "selected_executable.txt")).Should().Be(app);
    }

    [Fact]
    public async Task App_bundle_shortcut_is_a_link_to_the_app()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Skip("Requires macOS.");

        var app = CreateAppBundle(GamePath, "Game");

        await ShortcutHelper.CreateGameShortcutAsync(_game, new GameShortcutTarget(app, [], GamePath), null, Desktop);
        await ShortcutHelper.CreateGameShortcutAsync(_game, new GameShortcutTarget(app, [], GamePath), null, Desktop);

        var link = new FileInfo(Path.Combine(Desktop, "Ocarina \"Recomp\""));
        link.LinkTarget.Should().Be(app);
        Directory.EnumerateFileSystemEntries(Desktop).Should().ContainSingle("creating it again replaces the shortcut");
    }

    [Fact]
    public async Task Executable_shortcut_is_an_app_that_runs_the_game_in_its_folder()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Skip("Requires macOS.");

        var marker = Path.Combine(_root, "ran.txt");
        var executable = Path.Combine(GamePath, "game $ 'x'");
        WriteScript(executable, $"pwd > '{marker}'\necho \"$1\" >> '{marker}'\n");
        var icon = Path.Combine(_root, "icon.png");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Assets", "app.png"), icon);
        _game.CustomIconPath = icon;

        await ShortcutHelper.CreateGameShortcutAsync(_game,
            new GameShortcutTarget(executable, ["--fullscreen \"now\""], GamePath), Path.Combine(_root, "Cache"), Desktop);

        var app = Path.Combine(Desktop, "Ocarina \"Recomp\".app");
        var launch = Path.Combine(app, "Contents", "MacOS", "launch");
        File.GetUnixFileMode(launch).Should().HaveFlag(UnixFileMode.UserExecute);
        File.Exists(Path.Combine(app, "Contents", "Resources", "icon.icns")).Should().BeTrue();
        var plist = Run("plutil", "-convert", "json", "-o", "-", Path.Combine(app, "Contents", "Info.plist"));
        plist.Should().Contain("\"CFBundleExecutable\":\"launch\"").And.Contain("\"CFBundleIconFile\":\"icon\"");

        Run("open", "-W", app);

        File.ReadAllLines(marker).Should().Equal(Path.GetFullPath(GamePath), "--fullscreen \"now\"");
    }

    [Fact]
    public async Task Existing_desktop_item_not_created_by_quiver_is_never_replaced()
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Skip("Requires macOS.");

        var executable = Path.Combine(GamePath, "game");
        WriteScript(executable);
        var usersApp = CreateAppBundle(Desktop, "Ocarina \"Recomp\"");
        File.WriteAllText(Path.Combine(usersApp, "Contents", "Info.plist"), "<plist><dict/></plist>");

        var create = () => ShortcutHelper.CreateGameShortcutAsync(_game, new GameShortcutTarget(executable, [], GamePath), null, Desktop);

        await create.Should().ThrowAsync<IOException>().WithMessage("*not created by Quiver Launcher*");
        File.ReadAllText(Path.Combine(usersApp, "Contents", "Info.plist")).Should().Be("<plist><dict/></plist>");
    }

    private static string CreateAppBundle(string parent, string name)
    {
        var app = Path.Combine(parent, name + ".app");
        Directory.CreateDirectory(Path.Combine(app, "Contents", "MacOS"));
        return app;
    }

    private static void WriteScript(string path, string body = "exit 0\n")
    {
        File.WriteAllText(path, "#!/bin/sh\n" + body);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static string Run(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, $"{fileName} failed: {error}");
        return output;
    }
}
