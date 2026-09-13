using System.Runtime.InteropServices;
using System.Text;
using FluentAssertions;
using QuiverLauncher.Models;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class DirectGameShortcutTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "quiver-shortcuts", Guid.NewGuid().ToString("N"));
    private readonly GameInfo _game = new() { Name = "Banjo's game $ test", FolderName = "game" };
    private string GamePath => Path.Combine(_root, "game");
    private string SelectionPath => Path.Combine(GamePath, "selected_executable.txt");
    public DirectGameShortcutTests() => Directory.CreateDirectory(GamePath);
    public void Dispose() => Directory.Delete(_root, true);

    private string Executable(string name = "game")
    {
        var path = Path.Combine(GamePath, name + (OperatingSystem.IsWindows() ? ".exe" : ".AppImage"));
        File.WriteAllText(path, "#!/bin/sh\nexit 0\n");
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    [Fact]
    public async Task Saved_executable_is_used_directly_without_prompting_or_launching()
    {
        var executable = Executable();
        File.WriteAllText(SelectionPath, executable);
        var target = await GameShortcutLaunch.PrepareAsync(_game, _root, new(), _ => throw new Exception("Should not prompt"));
        target!.FileName.Should().Be(executable);
        target.WorkingDirectory.Should().Be(GamePath);
        target.Arguments.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Single_executable_is_selected_without_prompting_and_saved_for_the_worker(bool headless)
    {
        var executable = Executable();
        var target = await GameShortcutLaunch.PrepareAsync(_game, _root, new(),
            headless ? null : _ => throw new Exception("A single executable should not prompt"));
        target!.FileName.Should().Be(executable);
        _game.SelectedExecutable.Should().Be(executable);
        File.ReadAllText(SelectionPath).Should().Be(executable);
        _game.SelectedExecutable = null;
        var workerTarget = await GameShortcutLaunch.PrepareAsync(_game, _root, new());
        workerTarget!.FileName.Should().Be(target!.FileName);
        workerTarget.Arguments.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancellation_does_not_choose_a_default_or_create_a_selection()
    {
        Executable();
        Executable("second");
        var target = await GameShortcutLaunch.PrepareAsync(_game, _root, new(), _ => Task.FromResult<string?>(null));
        target.Should().BeNull();
        File.Exists(SelectionPath).Should().BeFalse();
    }

    [Fact]
    public async Task Stale_selection_prompts_again_and_multiple_choices_use_the_picked_file()
    {
        File.WriteAllText(SelectionPath, Path.Combine(GamePath, "deleted.exe"));
        Executable("first");
        var chosen = Executable("second");
        var target = await GameShortcutLaunch.PrepareAsync(_game, _root, new(), candidates =>
        {
            candidates.Should().HaveCount(2);
            return Task.FromResult<string?>(chosen);
        });
        target!.FileName.Should().Be(chosen);
    }

    [Fact]
    public async Task Headless_worker_refuses_to_guess_and_revalidates_missing_files()
    {
        var executable = Executable();
        Executable("second");
        var missingChoice = () => GameShortcutLaunch.PrepareAsync(_game, _root, new());
        await missingChoice.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Choose and save*");
        var removed = () => GameShortcutLaunch.PrepareAsync(_game, _root, new(), _ =>
        {
            File.Delete(executable);
            return Task.FromResult<string?>(executable);
        });
        await removed.Should().ThrowAsync<FileNotFoundException>();
        File.Exists(SelectionPath).Should().BeFalse();
    }

    [Fact]
    public async Task Desktop_file_targets_the_game_and_uses_its_working_directory()
    {
        var executable = Executable("game's $ spaced");
        _game.SelectedExecutable = executable;
        var target = await GameShortcutLaunch.PrepareAsync(_game, _root, new());
        await ShortcutHelper.CreateGameShortcutAsync(_game, target!, null, _root);
        if (OperatingSystem.IsWindows()) AssertWindowsShortcut(executable);
        else if (OperatingSystem.IsLinux())
        {
            var file = Directory.GetFiles(_root, "*.desktop").Single();
            var text = File.ReadAllText(file);
            text.Should().Contain("Path=" + GamePath).And.NotContain("--run").And.NotContain("QuiverLauncher");
            text.Should().Contain("Exec=").And.Contain("game's");
            File.GetUnixFileMode(file).HasFlag(UnixFileMode.UserExecute).Should().BeTrue();
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void AssertWindowsShortcut(string executable)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            dynamic link = ((dynamic)shell).CreateShortcut(Directory.GetFiles(_root, "*.lnk").Single());
            shortcut = link;
            ((string)link.TargetPath).Should().Be(executable);
            ((string)link.Arguments).Should().BeEmpty();
            ((string)link.WorkingDirectory).Should().Be(GamePath);
        }
        finally
        {
            if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
            if (shell != null) Marshal.FinalReleaseComObject(shell);
        }
    }

    [Fact]
    public async Task Linux_desktop_launcher_runs_the_game_with_literal_arguments_and_working_directory()
    {
        if (!OperatingSystem.IsLinux()) return;
        var executable = Executable("game's $ spaced");
        File.WriteAllText(executable, "#!/bin/sh\nprintf '%s\\n' \"$PWD\" \"$@\" > launched.txt\n");
        var target = new GameShortcutTarget(executable, ["space's $literal `text` % value"], GamePath);
        await ShortcutHelper.CreateGameShortcutAsync(_game, target, null, _root);
        // Use the desktop environment's parser/launcher, rather than treating Exec
        // as a shell command (older gio command-line versions lack `gio launch`).
        var app = g_desktop_app_info_new_from_filename(Directory.GetFiles(_root, "*.desktop").Single());
        app.Should().NotBe(IntPtr.Zero);
        IntPtr error = IntPtr.Zero;
        try { g_app_info_launch(app, IntPtr.Zero, IntPtr.Zero, out error).Should().Be(1); }
        finally { g_object_unref(app); if (error != IntPtr.Zero) g_error_free(error); }
        var marker = Path.Combine(GamePath, "launched.txt");
        for (var attempt = 0; attempt < 40 && !File.Exists(marker); attempt++) await Task.Delay(50);
        File.ReadAllLines(marker).Should().Equal(GamePath, target.Arguments[0]);
    }

    [DllImport("libgio-2.0.so.0")]
    private static extern IntPtr g_desktop_app_info_new_from_filename([MarshalAs(UnmanagedType.LPUTF8Str)] string filename);
    [DllImport("libgio-2.0.so.0")]
    private static extern int g_app_info_launch(IntPtr app, IntPtr files, IntPtr context, out IntPtr error);
    [DllImport("libgobject-2.0.so.0")]
    private static extern void g_object_unref(IntPtr instance);
    [DllImport("libglib-2.0.so.0")]
    private static extern void g_error_free(IntPtr error);

    [Fact]
    public async Task Linux_windows_shortcut_uses_configured_runner_directly()
    {
        if (!OperatingSystem.IsLinux()) return;
        var executable = Path.Combine(GamePath, "windows.exe");
        File.WriteAllText(executable, "fixture");
        _game.SelectedExecutable = executable;
        _game.LinuxRunner = "custom";
        _game.LinuxCustomLaunchCommand = "/usr/bin/custom-wine --prefix '{gamePath}/my prefix' '{exe}'";
        var target = await GameShortcutLaunch.PrepareAsync(_game, _root, new());
        target!.FileName.Should().Be("/usr/bin/custom-wine");
        target.Arguments.Should().Equal("--prefix", GamePath + "/my prefix", executable);
    }

    [Fact]
    public void Linux_desktop_quoting_does_not_expand_shell_characters_or_inject_keys()
    {
        var text = ShortcutHelper.BuildLinuxDesktopFile("Game\nExec=bad", new("/games/a $`%\\\".AppImage", ["argument with spaces"], "/games/a\\b"), null);
        text.Should().Contain("Name=Game\\nExec=bad\n").And.NotContain("\nExec=bad\n");
        text.Should().Contain("\\\\$").And.Contain("\\\\`").And.Contain("%%");
        text.Should().Contain("Path=/games/a\\\\b");
    }

    [Fact]
    public void Windows_runner_arguments_and_environment_are_preserved_without_quiver()
    {
        var target = GameShortcutLaunch.FromRunner(new()
        {
            FileName = "/opt/Proton/proton", Arguments = ["run", "/games/my game/game.exe"],
            EnvironmentVariables = new() { ["STEAM_COMPAT_DATA_PATH"] = "/games/my game/prefix" },
        }, "/games/my game");
        target.FileName.Should().Be("/usr/bin/env");
        target.Arguments.Should().Equal("STEAM_COMPAT_DATA_PATH=/games/my game/prefix", "/opt/Proton/proton", "run", "/games/my game/game.exe");
        target.WorkingDirectory.Should().Be("/games/my game");
    }

    [Fact]
    public void Steam_entry_replaces_quiver_target_without_duplicates_or_loss_of_identity()
    {
        var path = Path.Combine(_root, "shortcuts.vdf");
        var old = new GameShortcutTarget("/old/QuiverLauncher", ["--run", _game.Name!], "/old");
        ShortcutHelper.WriteGameToSteamFile(path, _game, old, "existing-artwork", out var updated);
        updated.Should().BeFalse();
        var before = ReadVdf(path);
        var beforeEntry = (Dictionary<string, object>)((Dictionary<string, object>)before["shortcuts"])["0"];
        var target = new GameShortcutTarget("/games/Banjo/game.exe", [], "/games/Banjo");
        ShortcutHelper.WriteGameToSteamFile(path, _game, target, null, out updated);
        updated.Should().BeTrue();
        var entries = (Dictionary<string, object>)ReadVdf(path)["shortcuts"];
        entries.Should().ContainSingle();
        var entry = (Dictionary<string, object>)entries["0"];
        entry["exe"].Should().Be("\"/games/Banjo/game.exe\"");
        entry["StartDir"].Should().Be("\"/games/Banjo\"");
        entry["LaunchOptions"].Should().Be("");
        entry["appid"].Should().Be(beforeEntry["appid"]);
        entry["icon"].Should().Be("existing-artwork");
        ShortcutHelper.WriteGameToSteamFile(path, _game, target, null, out updated);
        ((Dictionary<string, object>)ReadVdf(path)["shortcuts"]).Should().ContainSingle();
    }

    [Fact]
    public void Queued_worker_captures_app_identity_and_only_creates_the_shortcut()
    {
        var worker = ShortcutHelper.CreateSteamShortcutWorkerStartInfo(_game, "/opt/QuiverLauncher.AppImage");
        worker.ArgumentList.Should().Equal("--add-steam-shortcut", _game.Name!, "--app-identity", _game.IdentityKey, "--wait-for-steam-exit");
        worker.ArgumentList.Should().NotContain("--run");
    }

    [Fact]
    public void Invalid_steam_file_is_not_overwritten()
    {
        var path = Path.Combine(_root, "shortcuts.vdf");
        File.WriteAllBytes(path, [255, 0]);
        var act = () => ShortcutHelper.WriteGameToSteamFile(path, _game, new("game", [], _root), null, out _);
        act.Should().Throw<InvalidDataException>();
        File.ReadAllBytes(path).Should().Equal(255, 0);
    }

    private static Dictionary<string, object> ReadVdf(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path), Encoding.UTF8);
        string ReadString()
        {
            var bytes = new List<byte>();
            byte value;
            while ((value = reader.ReadByte()) != 0) bytes.Add(value);
            return Encoding.UTF8.GetString(bytes.ToArray());
        }
        Dictionary<string, object> ReadObject()
        {
            var result = new Dictionary<string, object>();
            byte type;
            while ((type = reader.ReadByte()) != 8)
            {
                var key = ReadString();
                result.Add(key, type switch { 0 => ReadObject(), 1 => ReadString(), 2 => reader.ReadInt32(), _ => throw new InvalidDataException() });
            }
            return result;
        }
        return ReadObject();
    }
}
