using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class TagDisplayFilterListItemTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Refresh_notifies_renamed_label_without_replacing_the_menu_item(bool replaceFilter)
    {
        var filter = new TagDisplayFilter { Name = "Old name" };
        var item = TagDisplayFilterListItem.FromFilter(filter, true);
        var items = new List<TagDisplayFilterListItem> { item };
        // Model a UI binding: it only rereads the label on PropertyChanged.
        var displayedName = item.Name;
        item.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(item.Name)) displayedName = item.Name;
        };
        if (replaceFilter) filter = new TagDisplayFilter { Id = filter.Id, Name = "New name" };
        else filter.Name = "New name";

        TagDisplayFilterListItem.TryUpdateSelection(items, [filter], filter.Id).Should().BeTrue();

        displayedName.Should().Be("New name");
        items[0].Should().BeSameAs(item);
        item.Filter.Should().BeSameAs(filter);
        item.IsSelected.Should().BeTrue();
    }

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
