using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FluentAssertions;

namespace QuiverLauncher.Tests;

public class CatalogSourceCardActionIndexTests
{
    [AvaloniaFact]
    public void GetDefaultCatalogSourceCardActionIndex_prefers_first_button_over_checkbox()
    {
        var controls = new List<Control>
        {
            new CheckBox { Content = "Enabled" },
            new Button { Content = "Review" },
            new Button { Content = "Remove" },
        };

        MainWindow.GetDefaultCatalogSourceCardActionIndex(controls).Should().Be(1);
    }

    [AvaloniaFact]
    public void GetDefaultCatalogSourceCardActionIndex_falls_back_to_checkbox_when_no_buttons()
    {
        var controls = new List<Control>
        {
            new CheckBox { Content = "Enabled" },
        };

        MainWindow.GetDefaultCatalogSourceCardActionIndex(controls).Should().Be(0);
    }

    [Fact]
    public void GetDefaultCatalogSourceCardActionIndex_empty_returns_negative()
    {
        MainWindow.GetDefaultCatalogSourceCardActionIndex([]).Should().Be(-1);
    }
}
