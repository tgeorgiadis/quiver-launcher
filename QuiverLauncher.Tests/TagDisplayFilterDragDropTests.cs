using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class TagDisplayFilterDragDropTests
{
    private const double ListTop = 100;
    private const double RowStride = 40;

    [Fact]
    public void ResolveInsertIndex_top_slot_returns_zero()
    {
        TagDisplayFilterDragDrop.ResolveInsertIndex(3, 1, ListTop + 5, ListTop, RowStride)
            .Should().Be(0);
    }

    [Fact]
    public void ResolveInsertIndex_middle_slot_returns_one()
    {
        TagDisplayFilterDragDrop.ResolveInsertIndex(3, 0, ListTop + 45, ListTop, RowStride)
            .Should().Be(1);
    }

    [Fact]
    public void ResolveInsertIndex_bottom_slot_returns_last_index()
    {
        TagDisplayFilterDragDrop.ResolveInsertIndex(3, 0, ListTop + 95, ListTop, RowStride)
            .Should().Be(2);
    }

    [Fact]
    public void ResolveInsertIndex_clamps_above_list()
    {
        TagDisplayFilterDragDrop.ResolveInsertIndex(3, 1, ListTop - 50, ListTop, RowStride)
            .Should().Be(0);
    }

    [Fact]
    public void ResolveInsertIndex_clamps_below_list()
    {
        TagDisplayFilterDragDrop.ResolveInsertIndex(3, 1, ListTop + 500, ListTop, RowStride)
            .Should().Be(2);
    }

    [Fact]
    public void ResolveInsertIndex_single_item_returns_zero()
    {
        TagDisplayFilterDragDrop.ResolveInsertIndex(1, 0, ListTop + 200, ListTop, RowStride)
            .Should().Be(0);
    }

    [Fact]
    public void ResolveInsertIndex_empty_list_returns_drag_index()
    {
        TagDisplayFilterDragDrop.ResolveInsertIndex(0, 2, ListTop, ListTop, RowStride)
            .Should().Be(2);
    }
}
