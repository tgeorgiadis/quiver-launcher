using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using AsyncImageLoader;
using System.Text;

namespace QuiverLauncher.Services;

/// <summary>Renders shared readme, changelog, and mod content without owning launcher state.</summary>
public sealed class MarkdownRenderer
{
    private readonly Action<string> _openUrl;

    public MarkdownRenderer(Action<string> openUrl)
    {
        _openUrl = openUrl ?? throw new ArgumentNullException(nameof(openUrl));
    }
        public List<Control> Render(string markdown, string? imageBaseUrl = null)
        {
            var controls = new List<Control>();
            if (string.IsNullOrWhiteSpace(markdown))
            {
                controls.Add(new SelectableTextBlock
                {
                    Text = "No changelog available.",
                    Foreground = new SolidColorBrush(Color.Parse("#B8B8B8")),
                    FontSize = 14
                });
                return controls;
            }

            var lines = markdown.Split('\n');
            var listItems = new List<MarkdownListLine>();
            var codeBlockLines = new List<string>();
            bool inCodeBlock = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');

                // Code blocks
                if (line.TrimStart().StartsWith("```"))
                {
                    if (inCodeBlock)
                    {
                        if (codeBlockLines.Count > 0)
                        {
                            var codeBlock = new Border
                            {
                                Background = new SolidColorBrush(Color.Parse("#1e1e1e")),
                                BorderBrush = new SolidColorBrush(Color.Parse("#2d2d30")),
                                BorderThickness = new Thickness(1),
                                CornerRadius = new CornerRadius(4),
                                Padding = new Thickness(12),
                                Margin = new Thickness(0, 8, 0, 8)
                            };
                            codeBlock.Child = new SelectableTextBlock
                            {
                                Text = string.Join("\n", codeBlockLines),
                                FontFamily = new FontFamily("Consolas,Courier New,monospace"),
                                FontSize = 13,
                                Foreground = new SolidColorBrush(Color.Parse("#d4d4d4"))
                            };
                            controls.Add(codeBlock);
                            codeBlockLines.Clear();
                        }
                        inCodeBlock = false;
                    }
                    else
                    {
                        FlushListItems(controls, listItems, imageBaseUrl);
                        inCodeBlock = true;
                    }
                    continue;
                }

                if (inCodeBlock)
                {
                    codeBlockLines.Add(line);
                    continue;
                }

                if (MarkdownBlocks.TrySkipHtmlComment(lines, i, out var htmlCommentLines))
                {
                    i += htmlCommentLines - 1;
                    continue;
                }

                if (MarkdownBlocks.IsHiddenComment(line))
                    continue;

                if (MarkdownBlocks.IsBlockquoteLine(line, out _))
                {
                    FlushListItems(controls, listItems, imageBaseUrl);
                    var quoteLines = new List<string>();
                    string? alertType = null;
                    while (i < lines.Length &&
                           MarkdownBlocks.IsBlockquoteLine(lines[i].TrimEnd('\r'), out var quoteContent))
                    {
                        if (quoteLines.Count == 0 &&
                            MarkdownBlocks.TryParseAlertType(quoteContent, out var parsedAlert))
                        {
                            alertType = parsedAlert;
                        }
                        else
                        {
                            quoteLines.Add(quoteContent);
                        }

                        i++;
                    }

                    i--;
                    controls.Add(CreateMarkdownQuote(quoteLines, alertType, imageBaseUrl));
                    continue;
                }

                // Headers
                if (line.StartsWith("#"))
                {
                    FlushListItems(controls, listItems, imageBaseUrl);
                    int level = 0;
                    while (level < line.Length && line[level] == '#') level++;
                    var headerText = line.Substring(level).Trim();
                    var fontSize = level switch { 1 => 24, 2 => 20, 3 => 18, 4 => 16, _ => 14 };
                    var fontWeight = level <= 2 ? FontWeight.Bold : FontWeight.SemiBold;
                    var headerBlocks = ParseInlineMarkdown(headerText, imageBaseUrl, fontSize, heading: true);
                    Control header = headerBlocks.Count == 1
                        ? headerBlocks[0]
                        : CreateInlineStack(headerBlocks);
                    if (header is SelectableTextBlock headerTextBlock)
                    {
                        headerTextBlock.FontSize = fontSize;
                        headerTextBlock.FontWeight = fontWeight;
                        headerTextBlock.Foreground = new SolidColorBrush(Colors.White);
                    }

                    if (level == 2)
                    {
                        controls.Add(new Border
                        {
                            BorderBrush = new SolidColorBrush(Color.Parse("#3d444d")),
                            BorderThickness = new Thickness(0, 0, 0, 1),
                            Padding = new Thickness(0, 0, 0, 8),
                            Margin = new Thickness(0, 16, 0, 8),
                            Child = header,
                        });
                    }
                    else
                    {
                        header.Margin = new Thickness(0, level == 1 ? 4 : 12, 0, 8);
                        controls.Add(header);
                    }

                    continue;
                }

                if (MarkdownBlocks.TryParseListLine(line, out var listItem))
                {
                    var itemText = new StringBuilder(listItem.Text);
                    while (i + 1 < lines.Length)
                    {
                        var next = lines[i + 1].TrimEnd('\r');
                        if (!MarkdownBlocks.CanContinueParagraph(next) ||
                            MarkdownTable.TryCollect(lines, i + 1, out _, out _))
                        {
                            break;
                        }

                        i++;
                        itemText.Append(' ');
                        itemText.Append(next.Trim());
                    }

                    listItems.Add(listItem with { Text = itemText.ToString() });
                    continue;
                }

                if (MarkdownHtml.LooksLikeHtml(line) &&
                    MarkdownHtml.TryCollectBlock(lines, i, out var htmlLineCount, out var html, out var htmlCenter))
                {
                    FlushListItems(controls, listItems, imageBaseUrl);
                    foreach (var block in ParseInlineMarkdown(html, imageBaseUrl, center: htmlCenter))
                    {
                        block.Margin = new Thickness(0, 4, 0, 8);
                        if (htmlCenter)
                            block.HorizontalAlignment = HorizontalAlignment.Stretch;
                        controls.Add(block);
                    }

                    i += htmlLineCount - 1;
                    continue;
                }

                if (MarkdownImageLine.IsImageOnlyLine(line))
                {
                    FlushListItems(controls, listItems, imageBaseUrl);
                    var imageMarkdown = new StringBuilder(line);
                    while (i + 1 < lines.Length &&
                           MarkdownImageLine.IsImageOnlyLine(lines[i + 1].TrimEnd('\r')))
                    {
                        i++;
                        imageMarkdown.Append(' ');
                        imageMarkdown.Append(lines[i].TrimEnd('\r'));
                    }

                    foreach (var block in ParseInlineMarkdown(imageMarkdown.ToString(), imageBaseUrl))
                    {
                        block.Margin = new Thickness(0, 4, 0, 8);
                        controls.Add(block);
                    }

                    continue;
                }

                if (MarkdownTable.TryCollect(lines, i, out var tableLineCount, out var table))
                {
                    FlushListItems(controls, listItems, imageBaseUrl);
                    controls.Add(CreateMarkdownTable(table, imageBaseUrl));
                    i += tableLineCount - 1;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line) || line.Trim().StartsWith("---"))
                {
                    FlushListItems(controls, listItems, imageBaseUrl);
                    if (line.Trim().StartsWith("---"))
                        controls.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.Parse("#2d2d30")), Margin = new Thickness(0, 12, 0, 12) });
                    continue;
                }

                FlushListItems(controls, listItems, imageBaseUrl);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    var paragraph = new StringBuilder(line.TrimEnd());
                    while (i + 1 < lines.Length)
                    {
                        var next = lines[i + 1].TrimEnd('\r');
                        if (!MarkdownBlocks.CanContinueParagraph(next) ||
                            MarkdownTable.TryCollect(lines, i + 1, out _, out _))
                        {
                            break;
                        }

                        i++;
                        paragraph.Append(' ');
                        paragraph.Append(next.Trim());
                    }

                    foreach (var block in ParseInlineMarkdown(paragraph.ToString(), imageBaseUrl))
                    {
                        block.HorizontalAlignment = HorizontalAlignment.Stretch;
                        block.Margin = new Thickness(0, 0, 0, 8);
                        controls.Add(block);
                    }
                }
            }

            FlushListItems(controls, listItems, imageBaseUrl);
            return controls;
        }

        private Control CreateMarkdownQuote(List<string> quoteLines, string? alertType, string? imageBaseUrl)
        {
            var isAlert = !string.IsNullOrWhiteSpace(alertType);
            var (borderColorHex, iconPath) = alertType switch
            {
                "NOTE" => ("#0969da", "markdown_info.png"),
                "TIP" => ("#1a7f37", "markdown_tip.png"),
                "IMPORTANT" => ("#8250df", "markdown_important.png"),
                "WARNING" => ("#9a6700", "markdown_warning.png"),
                "CAUTION" => ("#d1242f", "markdown_caution.png"),
                _ => ("#6e7681", ""),
            };

            var quoteColor = Color.Parse(borderColorHex);
            var quoteBrush = new SolidColorBrush(quoteColor);
            var quoteBorder = new Border
            {
                BorderBrush = quoteBrush,
                BorderThickness = new Thickness(4, 0, 0, 0),
                CornerRadius = new CornerRadius(0, 4, 4, 0),
                Padding = new Thickness(14, 8, 12, 8),
                Margin = new Thickness(0, 8, 0, 8),
                Background = new SolidColorBrush(quoteColor) { Opacity = isAlert ? 0.05 : 0.04 },
            };

            var quotePanel = new StackPanel { Spacing = 4 };
            if (isAlert)
            {
                var titlePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
                try
                {
                    titlePanel.Children.Add(new Avalonia.Controls.Shapes.Rectangle
                    {
                        Width = 16,
                        Height = 16,
                        Margin = new Thickness(0, 0, 8, 0),
                        Fill = quoteBrush,
                        VerticalAlignment = VerticalAlignment.Center,
                        OpacityMask = new ImageBrush
                        {
                            Source = new Avalonia.Media.Imaging.Bitmap(
                                Avalonia.Platform.AssetLoader.Open(
                                    new Uri($"avares://QuiverLauncher/Assets/{iconPath}")))
                        }
                    });
                }
                catch (Exception)
                {
                    titlePanel.Children.Add(new Avalonia.Controls.Shapes.Ellipse
                    {
                        Width = 8,
                        Height = 8,
                        Fill = quoteBrush,
                        Margin = new Thickness(0, 0, 8, 0)
                    });
                }

                titlePanel.Children.Add(new SelectableTextBlock
                {
                    Text = alertType,
                    FontSize = 14,
                    FontWeight = FontWeight.Bold,
                    Foreground = quoteBrush,
                    VerticalAlignment = VerticalAlignment.Center
                });
                quotePanel.Children.Add(titlePanel);
            }

            if (quoteLines.Count > 0)
            {
                foreach (var block in ParseInlineMarkdown(string.Join("\n", quoteLines), imageBaseUrl))
                    quotePanel.Children.Add(block);
            }

            quoteBorder.Child = quotePanel;
            return quoteBorder;
        }

        private static Control CreateInlineStack(List<Control> blocks)
        {
            var panel = new StackPanel { Spacing = 4 };
            foreach (var block in blocks)
                panel.Children.Add(block);
            return panel;
        }

        private Control? CreateMarkdownImage(
            string url,
            string alt,
            string? imageBaseUrl,
            string? linkUrl = null,
            double? htmlWidth = null,
            double? htmlHeight = null)
        {
            var resolved = MarkdownImageLine.TryGetRenderableUrl(url, alt, imageBaseUrl);
            if (string.IsNullOrWhiteSpace(resolved))
                return null;

            var isShield = MarkdownImageLine.IsCompactBadge(resolved, url);
            var isButtonBadge = !isShield && MarkdownImageLine.LooksLikeButtonBadge(resolved, alt);
            var useHtmlSize = MarkdownHtml.IsDisplaySizeHint(htmlWidth, htmlHeight);
            var height = useHtmlSize
                ? htmlHeight ?? double.NaN
                : isShield ? 20 : isButtonBadge ? 60 : double.NaN;
            var maxHeight = useHtmlSize
                ? htmlHeight ?? 96
                : isShield ? 22 : isButtonBadge ? 60 : 240;
            var maxWidth = useHtmlSize
                ? htmlWidth ?? 320
                : isShield || isButtonBadge ? 280 : 560;
            var image = new Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Width = useHtmlSize ? htmlWidth ?? double.NaN : double.NaN,
                Height = height,
                MaxHeight = maxHeight,
                MaxWidth = maxWidth,
                Margin = isShield || isButtonBadge || useHtmlSize
                    ? new Thickness(0, 2, 8, 2)
                    : new Thickness(0, 8, 0, 8),
            };
            ImageLoader.SetSource(image, resolved);

            var resolvedLink = MarkdownImageLine.ResolveLinkUrl(linkUrl ?? string.Empty, imageBaseUrl);
            if (string.IsNullOrWhiteSpace(resolvedLink))
                return image;

            var button = new Button
            {
                Content = image,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = resolvedLink,
            };
            button.Click += (_, _) =>
            {
                if (button.Tag is string href)
                {
                    try { _openUrl(href); } catch { }
                }
            };
            return button;
        }

        private List<Control> ParseInlineMarkdown(
            string text,
            string? imageBaseUrl = null,
            double fontSize = 14,
            bool center = false,
            bool heading = false)
        {
            text = MarkdownBlocks.StripHtmlComments(text);
            var blocks = new List<Control>();
            var bodyColor = heading ? Colors.White : Color.Parse("#B8B8B8");
            var paragraph = new SelectableTextBlock
            {
                Inlines = new InlineCollection(),
                FontSize = fontSize,
                Foreground = new SolidColorBrush(heading ? Colors.White : bodyColor),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = center ? TextAlignment.Center : TextAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            string? activeHref = null;
            var linkSpans = new List<(int Start, int End, string Url)>();
            var textPosition = 0;

            void AddRun(string value, bool bold = false, bool italic = false, bool link = false, string? linkUrl = null)
            {
                if (value.Length == 0)
                    return;

                var decoded = MarkdownHtml.DecodeEntities(value);
                var href = linkUrl;
                if (string.IsNullOrWhiteSpace(href) && !string.IsNullOrWhiteSpace(activeHref))
                    href = activeHref;

                var resolvedLink = MarkdownImageLine.ResolveLinkUrl(href ?? string.Empty, imageBaseUrl);
                var isLink = !string.IsNullOrWhiteSpace(resolvedLink) && (link || activeHref != null);
                if (isLink)
                    linkSpans.Add((textPosition, textPosition + decoded.Length, resolvedLink!));

                paragraph.Inlines.Add(new Run
                {
                    Text = decoded,
                    FontWeight = bold || heading ? FontWeight.Bold : FontWeight.Normal,
                    FontStyle = italic ? FontStyle.Italic : FontStyle.Normal,
                    Foreground = new SolidColorBrush(
                        isLink ? Color.Parse("#58a6ff") : bold || heading ? Colors.White : bodyColor),
                    TextDecorations = isLink ? TextDecorations.Underline : null,
                });
                textPosition += decoded.Length;
            }

            void AddInlineControl(Control child, BaselineAlignment alignment = BaselineAlignment.Center)
            {
                paragraph.Inlines.Add(new InlineUIContainer
                {
                    BaselineAlignment = alignment,
                    Child = child,
                });
                textPosition++;
            }

            void AddCodePill(string code)
            {
                paragraph.Inlines.Add(new Run
                {
                    Text = "\u00A0" + code + "\u00A0",
                    FontFamily = new FontFamily("Consolas,Courier New,monospace"),
                    FontSize = Math.Max(12, fontSize - 1),
                    Foreground = new SolidColorBrush(Color.Parse("#e6edf3")),
                    Background = new SolidColorBrush(Color.Parse("#21262d")),
                });
                textPosition += code.Length + 2;
            }

            void AddStyledText(string value, bool bold = false, bool italic = false)
            {
                var j = 0;
                while (j < value.Length)
                {
                    if (MarkdownImageLine.TryReadMarkdownLink(value, j, out var nestedConsumed, out var nestedLabel, out var nestedUrl))
                    {
                        AddRun(nestedLabel, bold: bold, italic: italic, link: true, linkUrl: nestedUrl);
                        j += nestedConsumed;
                        continue;
                    }

                    if (MarkdownEmphasis.TryRead(value, j, out var emConsumed, out var emInner, out var emBold, out var emItalic))
                    {
                        AddStyledText(emInner, bold || emBold, italic || emItalic);
                        j += emConsumed;
                        continue;
                    }

                    var next = j + 1;
                    while (next < value.Length &&
                           value[next] != '[' &&
                           !MarkdownEmphasis.TryRead(value, next, out _, out _, out _, out _))
                    {
                        next++;
                    }

                    AddRun(value[j..next], bold: bold, italic: italic);
                    j = next;
                }
            }

            var i = 0;
            while (i < text.Length)
            {
                if (text[i] == '\n' || text[i] == '\r')
                {
                    if (i + 1 < text.Length && (text[i + 1] == '\n' || text[i + 1] == '\r') && text[i] != text[i + 1])
                        i++;
                    i++;
                    AddRun(" ");
                    continue;
                }

                if (MarkdownEmphasis.TryRead(text, i, out var emConsumed, out var emInner, out var emBold, out var emItalic))
                {
                    AddStyledText(emInner, emBold, emItalic);
                    i += emConsumed;
                    continue;
                }

                if (text[i] == '`')
                {
                    i++;
                    var codeText = new StringBuilder();
                    while (i < text.Length && text[i] != '`')
                    {
                        codeText.Append(text[i]);
                        i++;
                    }

                    if (i < text.Length)
                        i++;
                    AddCodePill(codeText.ToString());
                    continue;
                }

                if (MarkdownHtml.TryParseTag(text, i, out var htmlTag))
                {
                    var tagName = htmlTag.Name;
                    if (tagName.Equals("a", StringComparison.OrdinalIgnoreCase))
                    {
                        activeHref = htmlTag.IsClosing ? null : MarkdownHtml.Attr(htmlTag, "href");
                    }
                    else if (tagName.Equals("img", StringComparison.OrdinalIgnoreCase))
                    {
                        MarkdownHtml.TryParsePixels(MarkdownHtml.Attr(htmlTag, "width"), out var htmlWidth);
                        MarkdownHtml.TryParsePixels(MarkdownHtml.Attr(htmlTag, "height"), out var htmlHeight);
                        var image = CreateMarkdownImage(
                            MarkdownHtml.Attr(htmlTag, "src") ?? string.Empty,
                            MarkdownHtml.Attr(htmlTag, "alt") ?? string.Empty,
                            imageBaseUrl,
                            activeHref,
                            htmlWidth > 0 ? htmlWidth : null,
                            htmlHeight > 0 ? htmlHeight : null);
                        if (image != null)
                            AddInlineControl(image);
                    }
                    else if (tagName.Equals("br", StringComparison.OrdinalIgnoreCase))
                    {
                        paragraph.Inlines.Add(new LineBreak());
                    }

                    i += htmlTag.Length;
                    continue;
                }

                if (MarkdownImageLine.TryParseAt(text, i, out var imageConsumed, out var imageAlt, out var imageUrl, out var wrapUrl))
                {
                    var image = CreateMarkdownImage(imageUrl, imageAlt, imageBaseUrl, wrapUrl);
                    if (image != null)
                        AddInlineControl(image);

                    i += imageConsumed;
                    continue;
                }

                if (MarkdownImageLine.TryReadMarkdownLink(text, i, out var linkConsumed, out var linkLabel, out var linkUrl))
                {
                    AddRun(linkLabel, link: true, linkUrl: linkUrl);
                    i += linkConsumed;
                    continue;
                }

                var runStart = i;
                while (i < text.Length &&
                       text[i] is not ('\n' or '\r' or '`' or '[' or '<' or '*' or '_') &&
                       !(text[i] == '!' && i + 1 < text.Length && text[i + 1] == '['))
                {
                    i++;
                }

                if (i == runStart)
                {
                    AddRun(text[i].ToString());
                    i++;
                }
                else
                {
                    AddRun(text[runStart..i]);
                }
            }

            if (paragraph.Inlines.Count > 0)
            {
                if (linkSpans.Count > 0)
                    AttachMarkdownLinkHandlers(paragraph, linkSpans);
                blocks.Add(paragraph);
            }

            return blocks;
        }

        private void AttachMarkdownLinkHandlers(
            SelectableTextBlock paragraph,
            List<(int Start, int End, string Url)> linkSpans)
        {
            string? LinkAt(Point controlPoint)
            {
                try
                {
                    var point = new Point(
                        controlPoint.X - paragraph.Padding.Left,
                        controlPoint.Y - paragraph.Padding.Top);
                    var hit = paragraph.TextLayout.HitTestPoint(point);
                    var index = hit.CharacterHit.FirstCharacterIndex;
                    return MarkdownImageLine.LinkUrlAt(linkSpans, index);
                }
                catch
                {
                    // Layout may not be ready yet.
                }

                return null;
            }

            paragraph.AddHandler(InputElement.PointerMovedEvent, (_, e) =>
            {
                paragraph.Cursor = LinkAt(e.GetPosition(paragraph)) != null
                    ? new Cursor(StandardCursorType.Hand)
                    : new Cursor(StandardCursorType.Ibeam);
            }, RoutingStrategies.Bubble, handledEventsToo: true);

            paragraph.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left)
                    return;
                var url = LinkAt(e.GetPosition(paragraph));
                if (url == null)
                    return;
                try { _openUrl(url); } catch { }
                e.Handled = true;
            }, RoutingStrategies.Bubble, handledEventsToo: true);
        }

        private Control CreateMarkdownTable(MarkdownTableModel table, string? imageBaseUrl)
        {
            var borderColor = Color.Parse("#3d444d");
            var headerBg = Color.Parse("#161b22");
            var altRowBg = Color.Parse("#0d1117");
            var columns = table.Headers.Count;
            var grid = new Grid();
            for (var c = 0; c < columns; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

            void AddCell(int row, int col, string markdown, bool header, MarkdownTableAlign align)
            {
                var alignment = align switch
                {
                    MarkdownTableAlign.Center => TextAlignment.Center,
                    MarkdownTableAlign.Right => TextAlignment.Right,
                    _ => TextAlignment.Left,
                };
                var content = ParseInlineMarkdown(markdown, imageBaseUrl);
                Control child = content.Count == 1
                    ? content[0]
                    : content.Count == 0
                        ? new SelectableTextBlock { Text = " ", FontSize = 13 }
                        : CreateInlineStack(content);
                if (child is SelectableTextBlock text)
                {
                    text.TextAlignment = alignment;
                    text.FontSize = 13;
                    if (header)
                    {
                        text.FontWeight = FontWeight.SemiBold;
                        text.Foreground = new SolidColorBrush(Colors.White);
                    }
                }

                var cell = new Border
                {
                    BorderBrush = new SolidColorBrush(borderColor),
                    BorderThickness = new Thickness(0, 0, col < columns - 1 ? 1 : 0, 1),
                    Padding = new Thickness(12, 7),
                    Background = new SolidColorBrush(header ? headerBg : row % 2 == 1 ? altRowBg : Colors.Transparent),
                    Child = child,
                };
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, col);
                grid.Children.Add(cell);
            }

            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var col = 0; col < columns; col++)
            {
                var align = col < table.Alignments.Count ? table.Alignments[col] : MarkdownTableAlign.Left;
                AddCell(0, col, table.Headers[col], header: true, align);
            }

            for (var row = 0; row < table.Rows.Count; row++)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var cells = table.Rows[row];
                for (var col = 0; col < columns; col++)
                {
                    var align = col < table.Alignments.Count ? table.Alignments[col] : MarkdownTableAlign.Left;
                    var markdown = col < cells.Count ? cells[col] : string.Empty;
                    AddCell(row + 1, col, markdown, header: false, align);
                }
            }

            return new Border
            {
                BorderBrush = new SolidColorBrush(borderColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                ClipToBounds = true,
                Margin = new Thickness(0, 8, 0, 12),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Child = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = grid,
                },
            };
        }

        private void FlushListItems(List<Control> controls, List<MarkdownListLine> listItems, string? imageBaseUrl = null)
        {
            if (listItems.Count == 0)
                return;

            var listPanel = new StackPanel
            {
                Margin = new Thickness(16, 4, 0, 8),
                Spacing = 2,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            foreach (var item in listItems)
            {
                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                    Margin = new Thickness(item.Level * 16, 1, 0, 1),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                row.Children.Add(new TextBlock
                {
                    Text = MarkdownBlocks.ListMarker(item),
                    FontSize = 14,
                    MinWidth = 0,
                    Padding = new Thickness(0),
                    Foreground = new SolidColorBrush(Colors.White),
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 0, 8, 0),
                });
                var body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
                foreach (var block in ParseInlineMarkdown(item.Text, imageBaseUrl))
                {
                    block.HorizontalAlignment = HorizontalAlignment.Stretch;
                    body.Children.Add(block);
                }
                Grid.SetColumn(body, 1);
                row.Children.Add(body);
                listPanel.Children.Add(row);
            }

            controls.Add(listPanel);
            listItems.Clear();
        }

}
