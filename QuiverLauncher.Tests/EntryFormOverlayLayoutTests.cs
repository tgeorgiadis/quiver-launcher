using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class EntryFormOverlayLayoutTests
{
    [Fact]
    public void ResolveScrollMaxHeight_is_uncapped_when_filling_available_space()
    {
        EntryFormOverlayLayout.ResolveScrollMaxHeight(800, fillAvailable: true).Should().BeNull();
    }

    [Fact]
    public void ResolveScrollMaxHeight_uses_extra_height_on_tall_screens()
    {
        EntryFormOverlayLayout.ResolveScrollMaxHeight(1080, fillAvailable: false)
            .Should().Be(760);
    }

    [Fact]
    public void ResolveScrollMaxHeight_keeps_a_floor_on_short_screens()
    {
        EntryFormOverlayLayout.ResolveScrollMaxHeight(500, fillAvailable: false)
            .Should().Be(400);
    }
}
