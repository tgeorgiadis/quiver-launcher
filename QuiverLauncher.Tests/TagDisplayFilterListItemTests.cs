using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class TagDisplayFilterListItemTests
{
    [Fact]
    public void TryUpdateSelection_updates_selected_flags_without_replacing_items()
    {
        var a = new TagDisplayFilter { Name = "N64" };
        var b = new TagDisplayFilter { Name = "PC" };
        var items = new List<TagDisplayFilterListItem>
        {
            TagDisplayFilterListItem.FromFilter(a, true),
            TagDisplayFilterListItem.FromFilter(b, false),
        };
        var originalA = items[0];
        var originalB = items[1];

        TagDisplayFilterListItem.TryUpdateSelection(items, [a, b], b.Id).Should().BeTrue();

        items[0].Should().BeSameAs(originalA);
        items[1].Should().BeSameAs(originalB);
        items[0].IsSelected.Should().BeFalse();
        items[1].IsSelected.Should().BeTrue();
    }

    [Fact]
    public void TryUpdateSelection_returns_false_when_ids_or_count_change()
    {
        var a = new TagDisplayFilter { Name = "N64" };
        var b = new TagDisplayFilter { Name = "PC" };
        var items = new List<TagDisplayFilterListItem>
        {
            TagDisplayFilterListItem.FromFilter(a, false),
        };

        TagDisplayFilterListItem.TryUpdateSelection(items, [a, b], a.Id).Should().BeFalse();
        TagDisplayFilterListItem.TryUpdateSelection(items, [b], a.Id).Should().BeFalse();
    }
}
