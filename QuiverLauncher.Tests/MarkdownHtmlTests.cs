using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class MarkdownHtmlTests
{
    [Fact]
    public void TryParseTag_reads_img_and_anchor_attributes()
    {
        MarkdownHtml.TryParseTag(
                """<img src="https://img.shields.io/badge/YouTube-FF0000?style=for-the-badge" alt="YouTube">""",
                0,
                out var img)
            .Should().BeTrue();
        img.Name.Should().Be("img");
        img.IsSelfClosing.Should().BeTrue();
        MarkdownHtml.Attr(img, "src").Should().Contain("img.shields.io");
        MarkdownHtml.Attr(img, "alt").Should().Be("YouTube");

        MarkdownHtml.TryParseTag(
                """<img src="./.github/resources/sidestore-badge.png" alt="Add to SideStore" height="60">""",
                0,
                out var sized)
            .Should().BeTrue();
        MarkdownHtml.TryParsePixels(MarkdownHtml.Attr(sized, "height"), out var height).Should().BeTrue();
        height.Should().Be(60);
        MarkdownHtml.IsDisplaySizeHint(width: null, height: 60).Should().BeTrue();
        MarkdownHtml.IsDisplaySizeHint(1480, 662).Should().BeFalse();

        MarkdownHtml.TryParseTag("""<a href="https://bois.icu">""", 0, out var anchor).Should().BeTrue();
        MarkdownHtml.Attr(anchor, "href").Should().Be("https://bois.icu");
        anchor.IsClosing.Should().BeFalse();
    }

    [Fact]
    public void TryCollectBlock_joins_centered_badge_paragraph()
    {
        var lines = new[]
        {
            """<p align="center">""",
            "",
            """<a href="https://www.youtube.com/@bryanthaboi">""",
            """  <img src="https://img.shields.io/badge/YouTube-FF0000?style=for-the-badge" alt="YouTube">""",
            "</a>",
            "</p>",
            "",
            "### Watch the latest update video",
        };

        MarkdownHtml.LooksLikeHtml(lines[0]).Should().BeTrue();
        MarkdownHtml.TryCollectBlock(lines, 0, out var count, out var joined, out var center).Should().BeTrue();
        center.Should().BeTrue();
        count.Should().Be(6);
        joined.Should().Contain("<img src=");
        joined.Should().Contain("</p>");
        joined.Should().NotContain("Watch the latest");
    }

    [Fact]
    public void TryCollectBlock_reads_one_line_logo()
    {
        const string line =
            """<p align="center"><img src="https://raw.githubusercontent.com/o/r/logo.png"></p>""";

        MarkdownHtml.TryCollectBlock([line, "plain"], 0, out var count, out var joined, out var center)
            .Should().BeTrue();
        count.Should().Be(1);
        center.Should().BeTrue();
        joined.Should().Contain("logo.png");
    }

    [Fact]
    public void DecodeEntities_unescapes_common_values()
    {
        MarkdownHtml.DecodeEntities("A &amp; B&nbsp;C").Should().Be("A & B C");
    }
}
