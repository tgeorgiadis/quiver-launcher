using System.Diagnostics;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public sealed class HostGameSessionTests
{
    [Fact]
    public async Task Host_session_waits_for_remaining_children_and_preserves_literal_arguments()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Requires Linux sessions and process groups.");
        var root = Path.Combine(Path.GetTempPath(), "quiver-host-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var info = new ProcessStartInfo("sh") { UseShellExecute = false, WorkingDirectory = root };
            foreach (var arg in new[] { "-c", "printf '%s' \"$1\" > argument.txt; (sleep 1; printf finished > child.txt) & exit 7", "game", "space's $value `literal`" })
                info.ArgumentList.Add(arg);
            using var process = HostGameSession.StartTracked(info, root);
            await HostGameSession.ExitTask(process)!.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            process.ExitCode.Should().Be(7);
            File.ReadAllText(Path.Combine(root, "argument.txt")).Should().Be("space's $value `literal`");
            File.ReadAllText(Path.Combine(root, "child.txt")).Should().Be("finished");
            Directory.GetFiles(root, "*.pid").Should().BeEmpty();
        }
        finally { TestFixtures.CleanupDirectory(root); }
    }
}
