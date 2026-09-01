using Avalonia.Controls;
using FluentAssertions;
using QuiverLauncher.Services;
using NavigationDirection = QuiverLauncher.Services.NavigationDirection;

namespace QuiverLauncher.Tests;

public class GamepadTopBarSearchNavTests
{
    [Fact]
    public void Search_highlight_skips_xy_and_right_selects_add()
    {
        var search = new TextBox { Name = "LibrarySearchTextBox" };
        var add = new Button { Name = "AddNewEntryButton" };

        GamepadTextInput.ShouldSkipXyFocusOnHighlight(search).Should().BeTrue();
        GamepadTextInput.ShouldSkipXyFocusOnHighlight(add).Should().BeFalse();

        var nav = new GamepadNavigationService();
        // CollectTopBarControls: Search=0, Add=1 when the clear button is hidden.
        nav.MoveHorizontalIndex(0, NavigationDirection.Right, 2).Should().Be(1);
    }
}
