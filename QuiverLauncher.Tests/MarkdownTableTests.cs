using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class MarkdownTableTests
{
    [Fact]
    public void TryCollect_reads_github_pipe_table()
    {
        var lines = new[]
        {
            "| Action | Primary Key | Secondary Key |",
            "| :--- | :--- | :--- |",
            "| **Move Up** | `W` | `Up Arrow` |",
            "| **Move Down** | `S` | `Down Arrow` |",
            "",
            "Rebind any of these.",
        };

        MarkdownTable.TryCollect(lines, 0, out var count, out var table).Should().BeTrue();
        count.Should().Be(4);
        table.Headers.Should().Equal("Action", "Primary Key", "Secondary Key");
        table.Alignments.Should().Equal(
            MarkdownTableAlign.Left, MarkdownTableAlign.Left, MarkdownTableAlign.Left);
        table.Rows.Should().HaveCount(2);
        table.Rows[0].Should().Equal("**Move Up**", "`W`", "`Up Arrow`");
    }

    [Fact]
    public void TryCollect_reads_dashed_separator_without_colons()
    {
        var lines = new[]
        {
            "| Action | Keyboard          | Controller         |",
            "| ------ | ----------------- | ------------------ |",
            "| Move   | Arrow keys / WASD | D-pad / left stick |",
        };

        MarkdownTable.TryCollect(lines, 0, out var count, out var table).Should().BeTrue();
        count.Should().Be(3);
        table.Headers[0].Should().Be("Action");
        table.Rows[0][1].Should().Be("Arrow keys / WASD");
    }

    [Fact]
    public void TryParseSeparator_reads_center_and_right_align()
    {
        MarkdownTable.TryParseSeparator("| :---: | ---: |", 2, out var aligns).Should().BeTrue();
        aligns.Should().Equal(MarkdownTableAlign.Center, MarkdownTableAlign.Right);
    }

    [Fact]
    public void TryCollect_reads_single_dash_separator_used_by_github()
    {
        var lines = new[]
        {
            "| N64 | A | B | L | R | Z | Start | Analog stick | C buttons | D-Pad |",
            "| - | - | - | - | - | - | - | - | - | - |",
            "| Keyboard | X | C | E | R | Z | Space | WASD | Arrow keys | TFGH |",
        };

        MarkdownTable.TryCollect(lines, 0, out var count, out var table).Should().BeTrue();
        count.Should().Be(3);
        table.Headers.Should().Equal(
            "N64", "A", "B", "L", "R", "Z", "Start", "Analog stick", "C buttons", "D-Pad");
        table.Rows.Should().HaveCount(1);
        table.Rows[0].Should().Equal(
            "Keyboard", "X", "C", "E", "R", "Z", "Space", "WASD", "Arrow keys", "TFGH");
    }

    [Fact]
    public void TryCollect_rejects_plain_paragraph()
    {
        MarkdownTable.TryCollect(["Just a paragraph", "and another"], 0, out _, out _).Should().BeFalse();
    }
}
