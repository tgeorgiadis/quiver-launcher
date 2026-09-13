using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class SubmenuHoverLifetimeTests
{
    [Fact]
    public void Returning_to_any_menu_region_cancels_the_deadline_and_old_callbacks_cannot_close_a_new_opening()
    {
        var callbacks = new List<Action>();
        var open = true;
        var inside = false;
        using var lifetime = new SubmenuHoverLifetime((callback, delay) =>
        {
            Assert.Equal(TimeSpan.FromMilliseconds(400), delay);
            callbacks.Add(callback); return new Cancellation();
        }, () => open, () => inside, () => open = false);
        lifetime.Leave();
        lifetime.Cancel(); // Re-enter parent, popup or nested popup.
        callbacks[0](); // Even a callback already queued on the dispatcher is harmless.
        Assert.True(open);
        lifetime.Leave();
        inside = true;
        callbacks[1]();
        Assert.True(open);
        inside = false;
        lifetime.Leave();
        lifetime.Cancel(); // Explicit close/reopen invalidates callbacks.
        callbacks[2]();
        Assert.True(open);
        lifetime.Leave();
        callbacks[3]();
        Assert.False(open);
        open = true;
        lifetime.Leave(); lifetime.Dispose(); callbacks[4]();
        Assert.True(open);
    }
    private sealed class Cancellation : IDisposable { public void Dispose() { } }
}
