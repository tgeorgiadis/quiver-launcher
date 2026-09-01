using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class MarkdownImageLineTests
{
    [Fact]
    public void TryParse_reads_alt_and_url()
    {
        MarkdownImageLine.TryParse("![Cover](docs/cover.png)", out var alt, out var url).Should().BeTrue();
        alt.Should().Be("Cover");
        url.Should().Be("docs/cover.png");
    }

    [Fact]
    public void TryParse_rejects_plain_text()
    {
        MarkdownImageLine.TryParse("Just a paragraph", out _, out _).Should().BeFalse();
    }

    [Fact]
    public void IsImageOnlyLine_accepts_linked_shields_badges()
    {
        const string badge =
            "[![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Android%20(ARM64)%20%7C%20Linux%20(WIP)-blue.svg)]()";

        MarkdownImageLine.IsImageOnlyLine(badge).Should().BeTrue();
        MarkdownImageLine.IsImageOnlyLine(badge + "  " + badge).Should().BeTrue();
        MarkdownImageLine.IsImageOnlyLine("A native static recompilation").Should().BeFalse();
        MarkdownImageLine.IsImageOnlyLine("> **Note on defaults:** boot native.").Should().BeFalse();
    }

    [Fact]
    public void TryParseAt_reads_shields_badge_with_parentheses_in_url()
    {
        const string line =
            "[![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Android%20(ARM64)%20%7C%20Linux%20(WIP)-blue.svg)]()";

        MarkdownImageLine.TryParseAt(line, 0, out var consumed, out var alt, out var url).Should().BeTrue();
        consumed.Should().Be(line.Length);
        alt.Should().Be("Platform");
        url.Should().Be("https://img.shields.io/badge/Platform-Windows%20%7C%20Android%20(ARM64)%20%7C%20Linux%20(WIP)-blue.svg");
    }

    [Fact]
    public void TryGetRenderableUrl_rewrites_shields_svg_to_png()
    {
        var url = MarkdownImageLine.TryGetRenderableUrl(
            "https://img.shields.io/badge/Language-C11%20%2F%20C%2B%2B20-orange.svg",
            "C++",
            rawRootUrl: null);

        url.Should().Be("https://img.shields.io/badge/Language-C11%20%2F%20C%2B%2B20-orange.png");
        MarkdownImageLine.IsCompactBadge(url!, "https://img.shields.io/badge/Language-C11%20%2F%20C%2B%2B20-orange.svg")
            .Should().BeTrue();
    }

    [Fact]
    public void LooksLikeButtonBadge_detects_store_badges()
    {
        MarkdownImageLine.LooksLikeButtonBadge("./.github/resources/sidestore-badge.png", "Add to SideStore")
            .Should().BeTrue();
        MarkdownImageLine.LooksLikeButtonBadge("docs/cover.png", "Cover art").Should().BeFalse();
    }

    [Fact]
    public void TryGetRenderableUrl_adds_png_to_shields_query_badges()
    {
        var url = MarkdownImageLine.TryGetRenderableUrl(
            "https://img.shields.io/badge/YouTube-FF0000?style=for-the-badge&logo=youtube&logoColor=white",
            "YouTube",
            rawRootUrl: null);

        url.Should().Be("https://img.shields.io/badge/YouTube-FF0000.png?style=for-the-badge&logo=youtube&logoColor=white");
    }

    [Fact]
    public void TryParseAt_reads_wrap_url_for_linked_image()
    {
        const string line = "[![Watch](https://img.youtube.com/vi/abc/maxresdefault.jpg)](https://youtu.be/abc)";
        MarkdownImageLine.TryParseAt(line, 0, out var consumed, out var alt, out var url, out var wrap)
            .Should().BeTrue();
        consumed.Should().Be(line.Length);
        alt.Should().Be("Watch");
        url.Should().Contain("maxresdefault.jpg");
        wrap.Should().Be("https://youtu.be/abc");
    }

    [Fact]
    public void TryGetRenderableUrl_recovers_filename_alt_when_src_is_empty()
    {
        var url = MarkdownImageLine.TryGetRenderableUrl(
            "",
            "docs/cover.png",
            "https://raw.githubusercontent.com/o/r/HEAD/");

        url.Should().Be("https://raw.githubusercontent.com/o/r/HEAD/docs/cover.png");
    }

    [Fact]
    public void TryGetRenderableUrl_skips_empty_unrecoverable_images()
    {
        MarkdownImageLine.TryGetRenderableUrl("", "C++", null).Should().BeNull();
    }

    [Fact]
    public void ResolveUrl_keeps_absolute_and_rewrites_relative()
    {
        MarkdownImageLine.ResolveUrl("https://cdn.example/a.png", "https://raw.githubusercontent.com/o/r/HEAD/")
            .Should().Be("https://cdn.example/a.png");

        MarkdownImageLine.ResolveUrl("./icons/app.png", "https://raw.githubusercontent.com/o/r/HEAD/")
            .Should().Be("https://raw.githubusercontent.com/o/r/HEAD/icons/app.png");

        MarkdownImageLine.ResolveUrl("//example.com/pic.png", null)
            .Should().Be("https://example.com/pic.png");
    }

    [Fact]
    public void TryReadMarkdownLink_reads_standard_and_emphasis_labels()
    {
        const string credit = "[infernozotza](https://github.com/Zotza)";
        MarkdownImageLine.TryReadMarkdownLink(credit, 0, out var consumed, out var label, out var url)
            .Should().BeTrue();
        label.Should().Be("infernozotza");
        url.Should().Be("https://github.com/Zotza");
        consumed.Should().Be(credit.Length);

        MarkdownImageLine.TryReadMarkdownLink("[**crack**](https://github.com/tomcl7)", 0, out _, out var boldLabel, out _)
            .Should().BeTrue();
        boldLabel.Should().Be("crack");
    }

    [Fact]
    public void LinkUrlAt_uses_character_index_inside_span()
    {
        var spans = new List<(int Start, int End, string Url)>
        {
            (0, 5, "https://github.com/tomcl7"),
        };

        MarkdownImageLine.LinkUrlAt(spans, 0).Should().Be("https://github.com/tomcl7");
        MarkdownImageLine.LinkUrlAt(spans, 4).Should().Be("https://github.com/tomcl7");
        MarkdownImageLine.LinkUrlAt(spans, 5).Should().BeNull();
    }
}
