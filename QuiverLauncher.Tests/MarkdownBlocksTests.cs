using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class MarkdownBlocksTests
{
    [Fact]
    public void TryParseListLine_reads_nested_indent()
    {
        MarkdownBlocks.TryParseListLine("- Parent", out var parent).Should().BeTrue();
        parent.Level.Should().Be(0);
        parent.Text.Should().Be("Parent");
        MarkdownBlocks.ListMarker(parent).Should().Be("•");

        MarkdownBlocks.TryParseListLine("  - Child", out var child).Should().BeTrue();
        child.Level.Should().Be(1);
        child.Text.Should().Be("Child");
        MarkdownBlocks.ListMarker(child).Should().Be("◦");

        MarkdownBlocks.TryParseListLine(" - Same level", out var singleSpace).Should().BeTrue();
        singleSpace.Level.Should().Be(0);
    }

    [Fact]
    public void IsBlockquoteLine_strips_marker()
    {
        MarkdownBlocks.IsBlockquoteLine("> **Note on defaults:** boot to native GBC.", out var content)
            .Should().BeTrue();
        content.Should().Be("**Note on defaults:** boot to native GBC.");

        MarkdownBlocks.IsBlockquoteLine("plain", out _).Should().BeFalse();
    }

    [Fact]
    public void TryParseAlertType_reads_github_alert_fence()
    {
        MarkdownBlocks.TryParseAlertType("[!NOTE]", out var type).Should().BeTrue();
        type.Should().Be("NOTE");
        MarkdownBlocks.TryParseAlertType("**Note on defaults:**", out _).Should().BeFalse();
    }

    [Fact]
    public void IsHiddenComment_reads_github_link_ref_comments()
    {
        MarkdownBlocks.IsHiddenComment("[comment]: <> (Todo: Make Light Mode Image)")
            .Should().BeTrue();
        MarkdownBlocks.IsHiddenComment("[comment]: <> (Todo: Make Dark Mode Image)")
            .Should().BeTrue();
        MarkdownBlocks.IsHiddenComment("[//]: # (hidden)")
            .Should().BeTrue();
        MarkdownBlocks.IsHiddenComment("Lead Developer:")
            .Should().BeFalse();
        MarkdownBlocks.IsHiddenComment("[Harbour Masters](https://example.com)")
            .Should().BeFalse();
        MarkdownBlocks.IsHiddenComment("<!-- retcomm-readme-metrics -->")
            .Should().BeTrue();
        MarkdownBlocks.IsHiddenComment("<!-- /retcomm-readme-metrics -->")
            .Should().BeTrue();
        MarkdownBlocks.IsHiddenComment("<!-- retcomm-readme-boxart -->")
            .Should().BeTrue();
        MarkdownBlocks.IsHiddenComment("Visible <!-- note --> text")
            .Should().BeFalse();
    }

    [Fact]
    public void StripHtmlComments_removes_html_comments()
    {
        MarkdownBlocks.StripHtmlComments("<!-- retcomm-readme-metrics -->")
            .Should().BeEmpty();
        MarkdownBlocks.StripHtmlComments("Before <!-- hide --> after")
            .Should().Be("Before  after");
    }

    [Fact]
    public void TrySkipHtmlComment_skips_single_and_multiline_html_comments()
    {
        var lines = new[]
        {
            "<!-- retcomm-readme-metrics -->",
            "badges",
            "<!--",
            "  boxart",
            "-->",
            "body",
        };

        MarkdownBlocks.TrySkipHtmlComment(lines, 0, out var first).Should().BeTrue();
        first.Should().Be(1);
        MarkdownBlocks.TrySkipHtmlComment(lines, 1, out _).Should().BeFalse();
        MarkdownBlocks.TrySkipHtmlComment(lines, 2, out var wrapped).Should().BeTrue();
        wrapped.Should().Be(3);
    }

    [Fact]
    public void CanContinueParagraph_joins_wrapped_readme_lines()
    {
        MarkdownBlocks.CanContinueParagraph("data cache in your OS's normal per-user app data folder.")
            .Should().BeTrue();
        MarkdownBlocks.CanContinueParagraph("- `save.lua` is written to that folder")
            .Should().BeFalse();
        MarkdownBlocks.CanContinueParagraph("## Controls").Should().BeFalse();
        MarkdownBlocks.CanContinueParagraph("").Should().BeFalse();
        MarkdownBlocks.CanContinueParagraph("— START > LINK connects two copies directly over UDP.")
            .Should().BeTrue();
        MarkdownBlocks.CanContinueParagraph("[comment]: <> (Todo: Make Light Mode Image)")
            .Should().BeFalse();
        MarkdownBlocks.CanContinueParagraph("<!-- retcomm-readme-metrics -->")
            .Should().BeFalse();
    }
}
