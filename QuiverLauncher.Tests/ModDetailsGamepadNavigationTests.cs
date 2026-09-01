using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class ModDetailsGamepadNavigationTests
{
    [Theory]
    [InlineData(ModDetailsGamepadSlot.Details, NavigationDirection.Right, ModDetailsGamepadSlot.Changelog)]
    [InlineData(ModDetailsGamepadSlot.Changelog, NavigationDirection.Left, ModDetailsGamepadSlot.Details)]
    [InlineData(ModDetailsGamepadSlot.Details, NavigationDirection.Up, ModDetailsGamepadSlot.OpenPage)]
    [InlineData(ModDetailsGamepadSlot.Changelog, NavigationDirection.Up, ModDetailsGamepadSlot.Close)]
    [InlineData(ModDetailsGamepadSlot.OpenPage, NavigationDirection.Down, ModDetailsGamepadSlot.Details)]
    [InlineData(ModDetailsGamepadSlot.Close, NavigationDirection.Down, ModDetailsGamepadSlot.Changelog)]
    [InlineData(ModDetailsGamepadSlot.OpenPage, NavigationDirection.Right, ModDetailsGamepadSlot.Close)]
    [InlineData(ModDetailsGamepadSlot.Close, NavigationDirection.Left, ModDetailsGamepadSlot.OpenPage)]
    public void Move_walks_2x2_grid(
        ModDetailsGamepadSlot from,
        NavigationDirection direction,
        ModDetailsGamepadSlot expected)
    {
        var move = ModDetailsGamepadNavigation.Move(from, direction, openPageVisible: true);

        move.Slot.Should().Be(expected);
        move.LeaveZone.Should().BeNull();
        move.ScrollBody.Should().BeFalse();
    }

    [Fact]
    public void Move_details_up_goes_to_close_when_open_page_hidden()
    {
        var move = ModDetailsGamepadNavigation.Move(
            ModDetailsGamepadSlot.Details,
            NavigationDirection.Up,
            openPageVisible: false);

        move.Slot.Should().Be(ModDetailsGamepadSlot.Close);
        move.LeaveZone.Should().BeNull();
        move.ScrollBody.Should().BeFalse();
    }

    [Fact]
    public void Move_close_left_goes_to_details_when_open_page_hidden()
    {
        var move = ModDetailsGamepadNavigation.Move(
            ModDetailsGamepadSlot.Close,
            NavigationDirection.Left,
            openPageVisible: false);

        move.Slot.Should().Be(ModDetailsGamepadSlot.Details);
    }

    [Theory]
    [InlineData(ModDetailsGamepadSlot.Details)]
    [InlineData(ModDetailsGamepadSlot.Changelog)]
    public void Move_down_from_tabs_scrolls_body(ModDetailsGamepadSlot from)
    {
        var move = ModDetailsGamepadNavigation.Move(from, NavigationDirection.Down, openPageVisible: true);

        move.ScrollBody.Should().BeTrue();
        move.Slot.Should().BeNull();
        move.LeaveZone.Should().BeNull();
    }

    [Theory]
    [InlineData(ModDetailsGamepadSlot.OpenPage, NavigationDirection.Left, GamepadNavigationZone.Sidebar)]
    [InlineData(ModDetailsGamepadSlot.Details, NavigationDirection.Left, GamepadNavigationZone.Sidebar)]
    [InlineData(ModDetailsGamepadSlot.OpenPage, NavigationDirection.Up, GamepadNavigationZone.TopBar)]
    [InlineData(ModDetailsGamepadSlot.Close, NavigationDirection.Up, GamepadNavigationZone.TopBar)]
    public void Move_leaves_to_chrome(
        ModDetailsGamepadSlot from,
        NavigationDirection direction,
        GamepadNavigationZone expected)
    {
        var move = ModDetailsGamepadNavigation.Move(from, direction, openPageVisible: true);

        move.LeaveZone.Should().Be(expected);
        move.Slot.Should().BeNull();
        move.ScrollBody.Should().BeFalse();
    }

    [Theory]
    [InlineData(ModDetailsGamepadSlot.Close, NavigationDirection.Right)]
    [InlineData(ModDetailsGamepadSlot.Changelog, NavigationDirection.Right)]
    public void Move_stays_on_right_edge(ModDetailsGamepadSlot from, NavigationDirection direction)
    {
        var move = ModDetailsGamepadNavigation.Move(from, direction, openPageVisible: true);

        move.Slot.Should().Be(from);
        move.LeaveZone.Should().BeNull();
        move.ScrollBody.Should().BeFalse();
    }

    [Fact]
    public void SlotReturningFromChrome_sidebar_prefers_open_page()
    {
        ModDetailsGamepadNavigation.SlotReturningFromChrome(
                GamepadNavigationZone.Sidebar,
                openPageVisible: true)
            .Should().Be(ModDetailsGamepadSlot.OpenPage);

        ModDetailsGamepadNavigation.SlotReturningFromChrome(
                GamepadNavigationZone.Sidebar,
                openPageVisible: false)
            .Should().Be(ModDetailsGamepadSlot.Details);
    }

    [Fact]
    public void SlotReturningFromChrome_top_bar_prefers_open_page()
    {
        ModDetailsGamepadNavigation.SlotReturningFromChrome(
                GamepadNavigationZone.TopBar,
                openPageVisible: true)
            .Should().Be(ModDetailsGamepadSlot.OpenPage);

        ModDetailsGamepadNavigation.SlotReturningFromChrome(
                GamepadNavigationZone.AnnouncementBanner,
                openPageVisible: false)
            .Should().Be(ModDetailsGamepadSlot.Close);
    }
}
