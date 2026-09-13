using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using FluentAssertions;
using QuiverLauncher.Services;

namespace QuiverLauncher.Tests;

public class MarkdownRendererTests
{
    private readonly MarkdownRenderer _renderer = new(_ => { });

    [AvaloniaFact]
    public void Empty_content_retains_placeholder()
    {
        _renderer.Render("").Should().ContainSingle().Which.Should()
            .BeOfType<SelectableTextBlock>().Which.Text.Should().Be("No changelog available.");
    }

    [AvaloniaFact]
    public void Nested_lists_preserve_indentation_and_markers()
    {
        var list = (StackPanel)_renderer.Render("- Parent\n  - Child").Single();
        var rows = list.Children.Cast<Grid>().ToArray();
        rows.Should().HaveCount(2);
        rows[1].Margin.Left.Should().BeGreaterThan(rows[0].Margin.Left);
        ((TextBlock)rows[0].Children[0]).Text.Should().Be("•");
        ((TextBlock)rows[1].Children[0]).Text.Should().Be("◦");
    }

    [AvaloniaFact]
    public void Table_preserves_columns_rows_and_alignment()
    {
        var border = (Border)_renderer.Render("| Left | Right |\n| --- | ---: |\n| A | B |").Single();
        var grid = (Grid)((ScrollViewer)border.Child!).Content!;
        grid.ColumnDefinitions.Should().HaveCount(2);
        grid.RowDefinitions.Should().HaveCount(2);
        var cell = (SelectableTextBlock)((Border)grid.Children[3]).Child!;
        cell.TextAlignment.Should().Be(TextAlignment.Right);
    }

    [AvaloniaFact]
    public void Code_and_quote_remain_separate_blocks()
    {
        var blocks = _renderer.Render("```cs\nvar value = 1;\n```\n\n> Quoted text");
        blocks.Should().HaveCount(2);
        ((SelectableTextBlock)((Border)blocks[0]).Child!).Text.Should().Be("var value = 1;");
        ((Border)blocks[1]).BorderThickness.Left.Should().Be(4);
    }

    [AvaloniaFact]
    public void Linked_image_resolves_relative_target_and_invokes_injected_action()
    {
        string? opened = null;
        var renderer = new MarkdownRenderer(url => opened = url);
        var paragraph = (SelectableTextBlock)renderer.Render(
            "[![Cover](cover.png)](details.html)", "https://example.com/docs/").Single();
        var button = (Button)paragraph.Inlines!.OfType<InlineUIContainer>().Single().Child!;
        button.Content.Should().BeOfType<Image>();
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        opened.Should().Be("https://example.com/docs/details.html");
    }

    [AvaloniaFact]
    public void Text_links_keep_label_and_underline()
    {
        var paragraph = (SelectableTextBlock)_renderer.Render("[Read more](https://example.com)").Single();
        var run = paragraph.Inlines!.OfType<Run>().Single();
        run.Text.Should().Be("Read more");
        run.TextDecorations.Should().Contain(TextDecorations.Underline.Single());
    }
}
