using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class GamepadConnectionStateTests
{
    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(1, 1, null)]
    [InlineData(2, 3, null)]
    [InlineData(0, 1, true)]
    [InlineData(0, 2, true)]
    [InlineData(1, 0, false)]
    [InlineData(3, 0, false)]
    public void GetConnectionChangedSignal_only_when_crossing_zero(
        int previousCount,
        int nextCount,
        bool? expected)
    {
        InputService.GetConnectionChangedSignal(previousCount, nextCount).Should().Be(expected);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void ShouldKeepOpenController_only_when_already_open_and_attached(
        bool alreadyOpen,
        bool attached,
        bool expected)
    {
        InputService.ShouldKeepOpenController(alreadyOpen, attached).Should().Be(expected);
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void ShouldReopenController_when_already_open_but_detached(
        bool alreadyOpen,
        bool attached,
        bool expected)
    {
        InputService.ShouldReopenController(alreadyOpen, attached).Should().Be(expected);
    }
}
