using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class GamepadNavigationRepeatTests
{
    private const int InitialDelay = 500;
    private const int RepeatDelay = 250;

    [Fact]
    public void ShouldAllowNavigationMove_allows_first_move_immediately()
    {
        GamepadNavigationRepeat.ShouldAllowNavigationMove(0, 0, InitialDelay, RepeatDelay)
            .Should().BeTrue();
        GamepadNavigationRepeat.ShouldAllowNavigationMove(0, 1, InitialDelay, RepeatDelay)
            .Should().BeTrue();
        GamepadNavigationRepeat.ShouldAllowNavigationMove(0, 249, InitialDelay, RepeatDelay)
            .Should().BeTrue();
    }

    [Fact]
    public void ShouldAllowNavigationMove_blocks_second_move_before_initial_delay()
    {
        GamepadNavigationRepeat.ShouldAllowNavigationMove(1, 499, InitialDelay, RepeatDelay)
            .Should().BeFalse();
        GamepadNavigationRepeat.ShouldAllowNavigationMove(1, 500, InitialDelay, RepeatDelay)
            .Should().BeTrue();
    }

    [Fact]
    public void ShouldAllowNavigationMove_uses_repeat_delay_for_third_and_later_moves()
    {
        GamepadNavigationRepeat.ShouldAllowNavigationMove(2, 249, InitialDelay, RepeatDelay)
            .Should().BeFalse();
        GamepadNavigationRepeat.ShouldAllowNavigationMove(2, 250, InitialDelay, RepeatDelay)
            .Should().BeTrue();
        GamepadNavigationRepeat.ShouldAllowNavigationMove(5, 249, InitialDelay, RepeatDelay)
            .Should().BeFalse();
        GamepadNavigationRepeat.ShouldAllowNavigationMove(5, 250, InitialDelay, RepeatDelay)
            .Should().BeTrue();
    }
}
