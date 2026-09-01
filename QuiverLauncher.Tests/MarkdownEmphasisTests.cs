using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class MarkdownEmphasisTests
{
    [Fact]
    public void TryRead_parses_italic_asterisks()
    {
        const string text = "Recomp is an acronym. *Reverse Engineering Causes Obsessive Mental Problems*";
        var start = text.IndexOf('*');

        MarkdownEmphasis.TryRead(text, start, out var consumed, out var inner, out var bold, out var italic)
            .Should().BeTrue();
        inner.Should().Be("Reverse Engineering Causes Obsessive Mental Problems");
        bold.Should().BeFalse();
        italic.Should().BeTrue();
        consumed.Should().Be(inner.Length + 2);
    }

    [Fact]
    public void TryRead_parses_bold_and_bold_italic()
    {
        MarkdownEmphasis.TryRead("**[crack](https://github.com/tomcl7)**", 0, out _, out var boldInner, out var bold, out var italic)
            .Should().BeTrue();
        boldInner.Should().Be("[crack](https://github.com/tomcl7)");
        bold.Should().BeTrue();
        italic.Should().BeFalse();

        MarkdownEmphasis.TryRead("***both***", 0, out _, out var bothInner, out var bothBold, out var bothItalic)
            .Should().BeTrue();
        bothInner.Should().Be("both");
        bothBold.Should().BeTrue();
        bothItalic.Should().BeTrue();
    }

    [Fact]
    public void TryRead_parses_underscore_italic_but_skips_snake_case()
    {
        MarkdownEmphasis.TryRead("_italic_", 0, out _, out var inner, out var bold, out var italic)
            .Should().BeTrue();
        inner.Should().Be("italic");
        bold.Should().BeFalse();
        italic.Should().BeTrue();

        MarkdownEmphasis.TryRead("snake_case", 5, out _, out _, out _, out _).Should().BeFalse();
        MarkdownEmphasis.TryRead("3 * 4 = 12", 2, out _, out _, out _, out _).Should().BeFalse();
    }
}
