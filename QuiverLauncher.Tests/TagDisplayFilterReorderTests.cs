using FluentAssertions;
using QuiverLauncher;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class TagDisplayFilterReorderTests
{
    private static List<TagDisplayFilter> CreateFilters(params string[] names) =>
        names.Select(name => new TagDisplayFilter { Name = name }).ToList();

    [Fact]
    public void Move_first_to_last_reorders_list()
    {
        var filters = CreateFilters("A", "B", "C");

        TagDisplayFilterReorder.Move(filters, 0, 2);

        filters.Select(f => f.Name).Should().Equal("B", "C", "A");
    }

    [Fact]
    public void Move_last_to_first_reorders_list()
    {
        var filters = CreateFilters("A", "B", "C");

        TagDisplayFilterReorder.Move(filters, 2, 0);

        filters.Select(f => f.Name).Should().Equal("C", "A", "B");
    }

    [Fact]
    public void Move_same_index_is_no_op()
    {
        var filters = CreateFilters("A", "B", "C");

        TagDisplayFilterReorder.Move(filters, 1, 1);

        filters.Select(f => f.Name).Should().Equal("A", "B", "C");
    }

    [Fact]
    public void Move_out_of_range_is_no_op()
    {
        var filters = CreateFilters("A", "B");

        TagDisplayFilterReorder.Move(filters, -1, 0);
        TagDisplayFilterReorder.Move(filters, 0, 5);
        TagDisplayFilterReorder.Move(filters, 5, 0);

        filters.Select(f => f.Name).Should().Equal("A", "B");
    }

    [Fact]
    public void Move_preserves_item_identity()
    {
        var filters = CreateFilters("A", "B", "C");
        var moved = filters[0];

        TagDisplayFilterReorder.Move(filters, 0, 2);

        filters[2].Should().BeSameAs(moved);
    }
}
