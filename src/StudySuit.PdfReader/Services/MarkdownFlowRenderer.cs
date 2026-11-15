using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StudySuit.PdfReader.Models;

namespace StudySuit.PdfReader.Services;

/// <summary>
/// Parses rich Markdown (headings, lists, tables, blockquotes, code blocks, links, inline formatting,
/// LaTeX math equations, chart/graph specs, and images) into a styled WPF FlowDocument.
/// </summary>
public static partial class MarkdownFlowRenderer
{
    private static readonly HttpClient ImageHttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static FlowDocument RenderToFlowDocument(
        string markdown,
        bool isDarkTheme,
        Action<string>? onOpenLink = null)
    {
        var palette = ThemePalette.For(isDarkTheme);
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(0),
            ColumnWidth = double.MaxValue,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            Foreground = palette.TextBrush,
            TextAlignment = TextAlignment.Left
        };

        if (string.IsNullOrWhiteSpace(markdown))
        {
            return doc;
        }

        string normalized = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] lines = normalized.Split('\n');

        int i = 0;
        while (i < lines.Length)
        {
            string line = lines[i];
            string trimmed = line.Trim();

            if (string.IsNullOrEmpty(trimmed))
            {
                i++;
                continue;
            }

            // 1. Fenced code / chart / graph / math blocks
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                string lang = trimmed[3..].Trim().ToLowerInvariant();
                i++;
                var blockLines = new List<string>();
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    blockLines.Add(lines[i]);
                    i++;
                }

                if (i < lines.Length)
                {
                    i++; // consume closing ```
                }

                string blockContent = string.Join('\n', blockLines);
                if (lang is "chart" or "graph")
                {
                    doc.Blocks.Add(BuildChartBlock(blockContent, palette, isDarkTheme));
                }
                else if (lang is "cards" or "grid")
                {
                    doc.Blocks.Add(BuildCardsBlock(blockContent, palette, isDarkTheme, onOpenLink));
                }
                else if (lang is "diagram" or "flow" or "flowchart")
                {
                    doc.Blocks.Add(BuildDiagramBlock(blockContent, palette));
                }
                else if (lang is "metrics" or "kpi" or "stats")
                {
                    doc.Blocks.Add(BuildMetricsBlock(blockContent, palette));
                }
                else if (lang is "callout" or "alert")
                {
                    doc.Blocks.Add(BuildCalloutJsonBlock(blockContent, palette, isDarkTheme, onOpenLink));
                }
                else if (lang is "math" or "latex" or "chem" or "chemistry" or "equation")
                {
                    doc.Blocks.Add(BuildEquationBlock(blockContent, isDarkTheme));
                }
                else
                {
                    doc.Blocks.Add(BuildCodeBlock(lang, blockContent, palette));
                }

                continue;
            }

            // 2. Display Math Blocks ($$ ... $$ or \[ ... \])
            if (trimmed.StartsWith("$$", StringComparison.Ordinal) ||
                trimmed.StartsWith(@"\[", StringComparison.Ordinal))
            {
                string closeToken = trimmed.StartsWith("$$", StringComparison.Ordinal) ? "$$" : @"\]";
                string openToken = trimmed.StartsWith("$$", StringComparison.Ordinal) ? "$$" : @"\[";

                if (trimmed.Length > openToken.Length + closeToken.Length &&
                    trimmed.EndsWith(closeToken, StringComparison.Ordinal))
                {
                    string formula = trimmed[openToken.Length..^closeToken.Length].Trim();
                    doc.Blocks.Add(BuildEquationBlock(formula, isDarkTheme));
                    i++;
                    continue;
                }

                var mathLines = new List<string>();
                string firstRest = trimmed[openToken.Length..].Trim();
                if (!string.IsNullOrEmpty(firstRest))
                {
                    mathLines.Add(firstRest);
                }

                i++;
                while (i < lines.Length)
                {
                    string cur = lines[i].Trim();
                    if (cur.EndsWith(closeToken, StringComparison.Ordinal))
                    {
                        string beforeClose = cur[..^closeToken.Length].Trim();
                        if (!string.IsNullOrEmpty(beforeClose))
                        {
                            mathLines.Add(beforeClose);
                        }

                        i++;
                        break;
                    }

                    mathLines.Add(lines[i]);
                    i++;
                }

                doc.Blocks.Add(BuildEquationBlock(string.Join(' ', mathLines), isDarkTheme));
                continue;
            }

            // 3. Standalone Image line: ![alt](src)
            var imgMatch = StandaloneImageRegex().Match(trimmed);
            if (imgMatch.Success)
            {
                doc.Blocks.Add(BuildImageBlock(imgMatch.Groups[1].Value, imgMatch.Groups[2].Value, palette));
                i++;
                continue;
            }

            // 4. Horizontal Rule
            if (HorizontalRuleRegex().IsMatch(trimmed))
            {
                doc.Blocks.Add(new BlockUIContainer(new Border
                {
                    Height = 1,
                    Background = palette.BorderBrush,
                    Margin = new Thickness(0, 8, 0, 8)
                }));
                i++;
                continue;
            }

            // 5. Headings (# .. ####)
            var headingMatch = HeadingRegex().Match(trimmed);
            if (headingMatch.Success)
            {
                int level = headingMatch.Groups[1].Value.Length;
                string headingText = headingMatch.Groups[2].Value.Trim();
                var p = new Paragraph
                {
                    FontSize = level switch { 1 => 18, 2 => 16, 3 => 14.5, _ => 13.5 },
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, level == 1 ? 8 : 6, 0, 4)
                };
                AppendInlines(p.Inlines, headingText, palette, isDarkTheme, onOpenLink);
                doc.Blocks.Add(p);
                i++;
                continue;
            }

            // 6. Markdown Table (| ... | followed by | --- |)
            if (trimmed.StartsWith('|') && i + 1 < lines.Length && TableSeparatorRegex().IsMatch(lines[i + 1].Trim()))
            {
                var tableLines = new List<string> { lines[i] };
                i += 2; // skip header & separator
                while (i < lines.Length && lines[i].Trim().StartsWith('|'))
                {
                    tableLines.Add(lines[i]);
                    i++;
                }

                doc.Blocks.Add(BuildTableBlock(tableLines, palette, isDarkTheme, onOpenLink));
                continue;
            }

            // 7. Blockquote (> ... and GitHub [!NOTE]/[!TIP]/[!IMPORTANT]/[!WARNING]/[!CAUTION] Alerts)
            if (trimmed.StartsWith('>'))
            {
                var quoteLines = new List<string>();
                while (i < lines.Length && lines[i].TrimStart().StartsWith('>'))
                {
                    quoteLines.Add(lines[i].TrimStart()[1..].TrimStart());
                    i++;
                }

                string joinedQuote = string.Join("\n", quoteLines).Trim();
                var alertMatch = CalloutHeaderRegex().Match(joinedQuote);
                if (alertMatch.Success)
                {
                    string kind = alertMatch.Groups[1].Value.ToUpperInvariant();
                    string body = joinedQuote[alertMatch.Length..].Trim();
                    doc.Blocks.Add(BuildAlertCard(kind, kind, body, palette, isDarkTheme, onOpenLink));
                    continue;
                }

                var quotePara = new Paragraph
                {
                    Margin = new Thickness(0, 4, 0, 6),
                    Padding = new Thickness(10, 6, 8, 6),
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    BorderBrush = palette.AccentBrush,
                    Background = palette.SubtleSurfaceBrush,
                    Foreground = palette.MutedBrush
                };
                AppendInlines(quotePara.Inlines, string.Join(" ", quoteLines), palette, isDarkTheme, onOpenLink);
                doc.Blocks.Add(quotePara);
                continue;
            }

            // 8. Bullet or Numbered List
            if (BulletItemRegex().IsMatch(trimmed) || NumberedItemRegex().IsMatch(trimmed))
            {
                bool isOrdered = NumberedItemRegex().IsMatch(trimmed);
                var list = new List
                {
                    MarkerStyle = isOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                    Margin = new Thickness(0, 3, 0, 6),
                    Padding = new Thickness(18, 0, 0, 0)
                };

                while (i < lines.Length)
                {
                    string curTrim = lines[i].Trim();
                    Match m = isOrdered ? NumberedItemRegex().Match(curTrim) : BulletItemRegex().Match(curTrim);
                    if (!m.Success)
                    {
                        break;
                    }

                    var itemPara = new Paragraph { Margin = new Thickness(0, 1, 0, 2) };
                    AppendInlines(itemPara.Inlines, m.Groups[1].Value.Trim(), palette, isDarkTheme, onOpenLink);
                    list.ListItems.Add(new ListItem(itemPara));
                    i++;
                }

                doc.Blocks.Add(list);
                continue;
            }

            // 9. Standard Paragraph (collect consecutive plain text lines)
            var paraLines = new List<string>();
            while (i < lines.Length)
            {
                string curTrim = lines[i].Trim();
                if (string.IsNullOrEmpty(curTrim) ||
                    curTrim.StartsWith("```", StringComparison.Ordinal) ||
                    curTrim.StartsWith("$$", StringComparison.Ordinal) ||
                    curTrim.StartsWith(@"\[", StringComparison.Ordinal) ||
                    curTrim.StartsWith('>') ||
                    HeadingRegex().IsMatch(curTrim) ||
                    BulletItemRegex().IsMatch(curTrim) ||
                    NumberedItemRegex().IsMatch(curTrim) ||
                    HorizontalRuleRegex().IsMatch(curTrim) ||
                    StandaloneImageRegex().IsMatch(curTrim) ||
                    (curTrim.StartsWith('|') && i + 1 < lines.Length && TableSeparatorRegex().IsMatch(lines[i + 1].Trim())))
                {
                    break;
                }

                paraLines.Add(curTrim);
                i++;
            }

            if (paraLines.Count > 0)
            {
                var p = new Paragraph { Margin = new Thickness(0, 0, 0, 7) };
                AppendInlines(p.Inlines, string.Join(" ", paraLines), palette, isDarkTheme, onOpenLink);
                doc.Blocks.Add(p);
            }
        }

        return doc;
    }

    private static void AppendInlines(
        InlineCollection inlines,
        string text,
        ThemePalette palette,
        bool isDarkTheme,
        Action<string>? onOpenLink)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        int pos = 0;
        foreach (Match m in InlineTokenRegex().Matches(text))
        {
            if (m.Index > pos)
            {
                inlines.Add(new Run(text[pos..m.Index]));
            }

            if (m.Groups["inlineimg"].Success)
            {
                var img = CreateImageElement(m.Groups["imgalt"].Value, m.Groups["imgurl"].Value, maxW: 240);
                inlines.Add(new InlineUIContainer(img));
            }
            else if (m.Groups["link"].Success)
            {
                string label = m.Groups["linktext"].Value;
                string url = m.Groups["linkurl"].Value;
                var link = new Hyperlink(new Run(label))
                {
                    Foreground = palette.LinkBrush,
                    ToolTip = url
                };
                link.Click += (_, _) => onOpenLink?.Invoke(url);
                inlines.Add(link);
            }
            else if (m.Groups["code"].Success)
            {
                inlines.Add(new Run($" {m.Groups["codebody"].Value} ")
                {
                    FontFamily = new FontFamily("Consolas, Courier New"),
                    FontSize = 12,
                    Background = palette.CodeBgBrush,
                    Foreground = palette.CodeTextBrush
                });
            }
            else if (m.Groups["math"].Success)
            {
                string expr = m.Groups["mathbody"].Success
                    ? m.Groups["mathbody"].Value
                    : m.Groups["parenmathbody"].Value;
                try
                {
                    var bmp = MathEquationRenderer.RenderEquation(expr.Trim(), fontSize: 16, darkBackground: isDarkTheme);
                    var img = new Image
                    {
                        Source = bmp,
                        Height = Math.Min(34, bmp.Height),
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(2, 0, 2, -4),
                        ToolTip = expr
                    };
                    inlines.Add(new InlineUIContainer(img) { BaselineAlignment = BaselineAlignment.Center });
                }
                catch
                {
                    inlines.Add(new Run(expr) { FontFamily = new FontFamily("Cambria Math, Georgia"), FontStyle = FontStyles.Italic });
                }
            }
            else if (m.Groups["bolditalic"].Success)
            {
                inlines.Add(new Run(m.Groups["bi"].Value) { FontWeight = FontWeights.Bold, FontStyle = FontStyles.Italic });
            }
            else if (m.Groups["bold"].Success)
            {
                inlines.Add(new Run(m.Groups["b"].Value) { FontWeight = FontWeights.Bold });
            }
            else if (m.Groups["italic"].Success)
            {
                inlines.Add(new Run(m.Groups["it"].Value) { FontStyle = FontStyles.Italic });
            }
            else if (m.Groups["strike"].Success)
            {
                var run = new Run(m.Groups["st"].Value);
                run.TextDecorations.Add(TextDecorations.Strikethrough);
                inlines.Add(run);
            }

            pos = m.Index + m.Length;
        }

        if (pos < text.Length)
        {
            inlines.Add(new Run(text[pos..]));
        }
    }

    private static Block BuildEquationBlock(string formula, bool isDarkTheme)
    {
        try
        {
            var bmp = MathEquationRenderer.RenderEquation(formula.Trim(), fontSize: 21, darkBackground: isDarkTheme);
            var img = new Image
            {
                Source = bmp,
                MaxWidth = 520,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = formula
            };
            return new BlockUIContainer(new Border
            {
                Margin = new Thickness(0, 6, 0, 8),
                Child = img
            });
        }
        catch
        {
            return new Paragraph(new Run(formula))
            {
                FontFamily = new FontFamily("Cambria Math, Consolas"),
                Margin = new Thickness(8, 4, 0, 6)
            };
        }
    }

    private static Block BuildChartBlock(string spec, ThemePalette palette, bool isDarkTheme)
    {
        try
        {
            BitmapSource bmp = RenderChartFromSpec(spec);
            var img = new Image
            {
                Source = bmp,
                MaxWidth = 540,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            return new BlockUIContainer(new Border
            {
                Margin = new Thickness(0, 6, 0, 8),
                CornerRadius = new CornerRadius(8),
                ClipToBounds = true,
                Child = img
            });
        }
        catch
        {
            return BuildCodeBlock("chart", spec, palette);
        }
    }

    private static Block BuildCardsBlock(
        string spec,
        ThemePalette palette,
        bool isDarkTheme,
        Action<string>? onOpenLink)
    {
        try
        {
            using var doc = JsonDocument.Parse(spec, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;
            string? title = root.TryGetProperty("title", out var tp) ? tp.GetString() : null;
            int columns = root.TryGetProperty("columns", out var cp) && cp.TryGetInt32(out int cVal)
                ? Math.Clamp(cVal, 1, 3)
                : 2;

            var outer = new StackPanel();
            if (!string.IsNullOrWhiteSpace(title))
            {
                outer.Children.Add(new TextBlock
                {
                    Text = title,
                    FontSize = 13.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = palette.TextBrush,
                    Margin = new Thickness(0, 0, 0, 8)
                });
            }

            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = columns };
            if (root.TryGetProperty("cards", out var cardsArr) && cardsArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var card in cardsArr.EnumerateArray())
                {
                    string cardTitle = card.TryGetProperty("title", out var ct) ? ct.GetString() ?? "" : "";
                    string badge = card.TryGetProperty("badge", out var cb) ? cb.GetString() ?? "" : "";
                    Brush accent = ParseHexBrush(card.TryGetProperty("color", out var cc) ? cc.GetString() : null, palette.AccentBrush);

                    var cardStack = new StackPanel();
                    var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                    if (!string.IsNullOrWhiteSpace(badge))
                    {
                        var pill = new Border
                        {
                            Background = CreateTintedBrush(accent, 0.18),
                            CornerRadius = new CornerRadius(5),
                            Padding = new Thickness(6, 2, 6, 2),
                            Margin = new Thickness(6, 0, 0, 0),
                            VerticalAlignment = VerticalAlignment.Center,
                            Child = new TextBlock
                            {
                                Text = badge,
                                FontSize = 10,
                                FontWeight = FontWeights.SemiBold,
                                Foreground = accent
                            }
                        };
                        DockPanel.SetDock(pill, Dock.Right);
                        header.Children.Add(pill);
                    }

                    header.Children.Add(new TextBlock
                    {
                        Text = cardTitle,
                        FontSize = 12.5,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = palette.TextBrush,
                        TextWrapping = TextWrapping.Wrap
                    });
                    cardStack.Children.Add(header);

                    if (card.TryGetProperty("items", out var itemsArr) && itemsArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in itemsArr.EnumerateArray())
                        {
                            string itemText = item.GetString() ?? string.Empty;
                            if (string.IsNullOrWhiteSpace(itemText))
                            {
                                continue;
                            }

                            var tb = new TextBlock
                            {
                                FontSize = 11.8,
                                Foreground = palette.TextBrush,
                                TextWrapping = TextWrapping.Wrap,
                                Margin = new Thickness(0, 1.5, 0, 1.5)
                            };
                            tb.Inlines.Add(new Run("• ") { Foreground = accent, FontWeight = FontWeights.Bold });
                            AppendInlines(tb.Inlines, itemText, palette, isDarkTheme, onOpenLink);
                            cardStack.Children.Add(tb);
                        }
                    }
                    else if (card.TryGetProperty("body", out var bodyProp))
                    {
                        var tb = new TextBlock
                        {
                            FontSize = 11.8,
                            Foreground = palette.TextBrush,
                            TextWrapping = TextWrapping.Wrap
                        };
                        AppendInlines(tb.Inlines, bodyProp.GetString() ?? "", palette, isDarkTheme, onOpenLink);
                        cardStack.Children.Add(tb);
                    }

                    var cardBorder = new Border
                    {
                        Background = palette.SubtleSurfaceBrush,
                        BorderBrush = palette.BorderBrush,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(8),
                        Margin = new Thickness(0, 0, 8, 8),
                        Padding = new Thickness(10, 8, 10, 8),
                        Child = new Border
                        {
                            BorderBrush = accent,
                            BorderThickness = new Thickness(3, 0, 0, 0),
                            Padding = new Thickness(8, 0, 0, 0),
                            Child = cardStack
                        }
                    };
                    grid.Children.Add(cardBorder);
                }
            }

            outer.Children.Add(grid);
            return new BlockUIContainer(new Border
            {
                Margin = new Thickness(0, 4, 0, 8),
                Child = outer
            });
        }
        catch
        {
            return BuildCodeBlock("cards", spec, palette);
        }
    }

    private static Block BuildDiagramBlock(string spec, ThemePalette palette)
    {
        try
        {
            using var doc = JsonDocument.Parse(spec, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;
            string? title = root.TryGetProperty("title", out var tp) ? tp.GetString() : null;
            string direction = root.TryGetProperty("direction", out var dp) ? (dp.GetString() ?? "horizontal").ToLowerInvariant() : "horizontal";
            bool vertical = direction is "vertical" or "td" or "down";

            var outer = new StackPanel();
            if (!string.IsNullOrWhiteSpace(title))
            {
                outer.Children.Add(new TextBlock
                {
                    Text = title,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = palette.TextBrush,
                    Margin = new Thickness(0, 0, 0, 8)
                });
            }

            Panel container = vertical
                ? new StackPanel { Orientation = Orientation.Vertical }
                : new WrapPanel { Orientation = Orientation.Horizontal };

            JsonElement stepsArr = default;
            bool hasSteps = root.TryGetProperty("steps", out stepsArr) && stepsArr.ValueKind == JsonValueKind.Array;
            if (!hasSteps)
            {
                hasSteps = root.TryGetProperty("nodes", out stepsArr) && stepsArr.ValueKind == JsonValueKind.Array;
            }

            if (hasSteps)
            {
                var stepList = stepsArr.EnumerateArray().ToList();
                for (int idx = 0; idx < stepList.Count; idx++)
                {
                    var step = stepList[idx];
                    string label = step.ValueKind == JsonValueKind.String
                        ? step.GetString() ?? ""
                        : step.TryGetProperty("label", out var lp) ? lp.GetString() ?? "" :
                          step.TryGetProperty("title", out var sp) ? sp.GetString() ?? "" : "";
                    string detail = step.ValueKind == JsonValueKind.Object && step.TryGetProperty("detail", out var detp)
                        ? detp.GetString() ?? ""
                        : "";
                    Brush accent = ParseHexBrush(
                        step.ValueKind == JsonValueKind.Object && step.TryGetProperty("color", out var colP) ? colP.GetString() : null,
                        palette.AccentBrush);

                    var nodeStack = new StackPanel();
                    nodeStack.Children.Add(new TextBlock
                    {
                        Text = label,
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = palette.TextBrush,
                        TextWrapping = TextWrapping.Wrap
                    });
                    if (!string.IsNullOrWhiteSpace(detail))
                    {
                        nodeStack.Children.Add(new TextBlock
                        {
                            Text = detail,
                            FontSize = 11,
                            Foreground = palette.MutedBrush,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 2, 0, 0)
                        });
                    }

                    container.Children.Add(new Border
                    {
                        Background = palette.SubtleSurfaceBrush,
                        BorderBrush = accent,
                        BorderThickness = new Thickness(1.5),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(10, 6, 10, 6),
                        Margin = new Thickness(0, 0, 4, 6),
                        MaxWidth = vertical ? 500 : 190,
                        Child = nodeStack
                    });

                    if (idx < stepList.Count - 1)
                    {
                        container.Children.Add(new TextBlock
                        {
                            Text = vertical ? "↓" : "→",
                            FontSize = 16,
                            FontWeight = FontWeights.Bold,
                            Foreground = palette.AccentBrush,
                            HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = vertical ? new Thickness(0, 0, 0, 6) : new Thickness(2, 0, 6, 6)
                        });
                    }
                }
            }

            outer.Children.Add(container);
            return new BlockUIContainer(new Border
            {
                Margin = new Thickness(0, 4, 0, 8),
                Child = outer
            });
        }
        catch
        {
            return BuildCodeBlock("diagram", spec, palette);
        }
    }

    private static Block BuildMetricsBlock(string spec, ThemePalette palette)
    {
        try
        {
            using var doc = JsonDocument.Parse(spec, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            JsonElement arr = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement
                : doc.RootElement.TryGetProperty("metrics", out var mp) ? mp :
                  doc.RootElement.TryGetProperty("items", out var ip) ? ip : default;

            if (arr.ValueKind != JsonValueKind.Array)
            {
                return BuildCodeBlock("metrics", spec, palette);
            }

            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var item in arr.EnumerateArray())
            {
                string label = item.TryGetProperty("label", out var lp) ? lp.GetString() ?? "" : "";
                string value = item.TryGetProperty("value", out var vp) ? vp.ToString() : "";
                string note = item.TryGetProperty("note", out var np) ? np.GetString() ?? "" : "";
                Brush accent = ParseHexBrush(item.TryGetProperty("color", out var cp) ? cp.GetString() : null, palette.AccentBrush);

                var stack = new StackPanel();
                stack.Children.Add(new TextBlock
                {
                    Text = label,
                    FontSize = 10.5,
                    Foreground = palette.MutedBrush
                });
                stack.Children.Add(new TextBlock
                {
                    Text = value,
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = accent,
                    Margin = new Thickness(0, 2, 0, 0)
                });
                if (!string.IsNullOrWhiteSpace(note))
                {
                    stack.Children.Add(new TextBlock
                    {
                        Text = note,
                        FontSize = 10.5,
                        Foreground = palette.MutedBrush,
                        Margin = new Thickness(0, 2, 0, 0)
                    });
                }

                wrap.Children.Add(new Border
                {
                    Background = palette.SubtleSurfaceBrush,
                    BorderBrush = palette.BorderBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 8, 8),
                    MinWidth = 110,
                    Child = stack
                });
            }

            return new BlockUIContainer(new Border
            {
                Margin = new Thickness(0, 4, 0, 6),
                Child = wrap
            });
        }
        catch
        {
            return BuildCodeBlock("metrics", spec, palette);
        }
    }

    private static Block BuildCalloutJsonBlock(
        string spec,
        ThemePalette palette,
        bool isDarkTheme,
        Action<string>? onOpenLink)
    {
        try
        {
            using var doc = JsonDocument.Parse(spec, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;
            string kind = root.TryGetProperty("type", out var tp) ? (tp.GetString() ?? "NOTE").ToUpperInvariant() : "NOTE";
            string title = root.TryGetProperty("title", out var tl) ? tl.GetString() ?? kind : kind;
            string body = root.TryGetProperty("body", out var bp) ? bp.GetString() ?? "" : "";
            return BuildAlertCard(kind, title, body, palette, isDarkTheme, onOpenLink);
        }
        catch
        {
            return BuildCodeBlock("callout", spec, palette);
        }
    }

    private static Block BuildAlertCard(
        string kind,
        string title,
        string body,
        ThemePalette palette,
        bool isDarkTheme,
        Action<string>? onOpenLink)
    {
        Color color = kind switch
        {
            "TIP" => Color.FromRgb(34, 197, 94),
            "IMPORTANT" => Color.FromRgb(168, 85, 247),
            "WARNING" => Color.FromRgb(245, 158, 11),
            "CAUTION" => Color.FromRgb(239, 68, 68),
            _ => Color.FromRgb(56, 189, 248)
        };
        var accent = new SolidColorBrush(color);
        accent.Freeze();

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            Foreground = accent,
            Margin = new Thickness(0, 0, 0, 3)
        });

        var bodyTb = new TextBlock
        {
            FontSize = 12,
            Foreground = palette.TextBrush,
            TextWrapping = TextWrapping.Wrap
        };
        AppendInlines(bodyTb.Inlines, body, palette, isDarkTheme, onOpenLink);
        stack.Children.Add(bodyTb);

        return new BlockUIContainer(new Border
        {
            Background = CreateTintedBrush(accent, isDarkTheme ? 0.14 : 0.08),
            BorderBrush = accent,
            BorderThickness = new Thickness(3.5, 1, 1, 1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 4, 0, 8),
            Child = stack
        });
    }

    private static Brush ParseHexBrush(string? hex, Brush fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return fallback;
        }

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex.Trim());
            var br = new SolidColorBrush(color);
            br.Freeze();
            return br;
        }
        catch
        {
            return fallback;
        }
    }

    private static Brush CreateTintedBrush(Brush baseBrush, double opacity)
    {
        if (baseBrush is SolidColorBrush solid)
        {
            var br = new SolidColorBrush(Color.FromArgb(
                (byte)Math.Clamp((int)(opacity * 255), 0, 255),
                solid.Color.R,
                solid.Color.G,
                solid.Color.B));
            br.Freeze();
            return br;
        }

        return baseBrush;
    }

    internal static BitmapSource RenderChartFromSpec(string spec)
    {
        using var doc = JsonDocument.Parse(spec, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;

        string typeRaw = root.TryGetProperty("type", out var tp) ? (tp.GetString() ?? "bar").ToLowerInvariant() : "bar";
        string title = root.TryGetProperty("title", out var tl) ? (tl.GetString() ?? "Chart") : "Chart";
        string xLabel = root.TryGetProperty("xLabel", out var xl) ? (xl.GetString() ?? "") : "";
        string yLabel = root.TryGetProperty("yLabel", out var yl) ? (yl.GetString() ?? "") : "";

        if (typeRaw is "function" or "functionplot" or "graph")
        {
            string expr = root.TryGetProperty("expr", out var ex) ? (ex.GetString() ?? "x") :
                          root.TryGetProperty("expression", out var ex2) ? (ex2.GetString() ?? "x") : "x";
            double xMin = root.TryGetProperty("xMin", out var xminp) && xminp.TryGetDouble(out double xm1) ? xm1 : -10;
            double xMax = root.TryGetProperty("xMax", out var xmaxp) && xmaxp.TryGetDouble(out double xm2) ? xm2 : 10;
            double yMin = root.TryGetProperty("yMin", out var yminp) && yminp.TryGetDouble(out double ym1) ? ym1 : -5;
            double yMax = root.TryGetProperty("yMax", out var ymaxp) && ymaxp.TryGetDouble(out double ym2) ? ym2 : 5;
            return ChartRenderer.RenderFunctionGraph(expr, title, xMin, xMax, yMin, yMax, 560, 330);
        }

        var chartType = typeRaw switch
        {
            "line" => StudyChartType.Line,
            "pie" => StudyChartType.Pie,
            "scatter" => StudyChartType.Scatter,
            _ => StudyChartType.Bar
        };

        var points = new List<ChartDataPoint>();
        if (root.TryGetProperty("data", out var dataArr) && dataArr.ValueKind == JsonValueKind.Array)
        {
            int idx = 0;
            foreach (var item in dataArr.EnumerateArray())
            {
                idx++;
                string label = item.TryGetProperty("label", out var lb) ? (lb.GetString() ?? $"Item {idx}") : $"Item {idx}";
                double val = item.TryGetProperty("value", out var vp) && vp.TryGetDouble(out double v) ? v :
                             item.TryGetProperty("y", out var yp) && yp.TryGetDouble(out double yv) ? yv : 0;
                double xVal = item.TryGetProperty("x", out var xp) && xp.TryGetDouble(out double xv) ? xv : idx;
                points.Add(new ChartDataPoint { Label = label, Value = val, XValue = xVal });
            }
        }

        return ChartRenderer.RenderDataChart(chartType, title, xLabel, yLabel, points, 560, 330);
    }

    private static Block BuildCodeBlock(string lang, string code, ThemePalette palette)
    {
        var stack = new StackPanel();
        if (!string.IsNullOrWhiteSpace(lang))
        {
            stack.Children.Add(new TextBlock
            {
                Text = lang.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = palette.MutedBrush,
                Margin = new Thickness(0, 0, 0, 4)
            });
        }

        stack.Children.Add(new TextBlock
        {
            Text = code.TrimEnd(),
            FontFamily = new FontFamily("Consolas, Courier New"),
            FontSize = 12,
            Foreground = palette.CodeTextBrush,
            TextWrapping = TextWrapping.Wrap
        });

        return new BlockUIContainer(new Border
        {
            Background = palette.CodeBgBrush,
            BorderBrush = palette.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 4, 0, 8),
            Child = stack
        });
    }

    private static Block BuildTableBlock(
        List<string> tableLines,
        ThemePalette palette,
        bool isDarkTheme,
        Action<string>? onOpenLink)
    {
        var table = new Table
        {
            CellSpacing = 0,
            BorderBrush = palette.BorderBrush,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 4, 0, 8)
        };

        var rows = tableLines
            .Select(l => l.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList())
            .ToList();

        int cols = rows.Max(r => r.Count);
        for (int c = 0; c < cols; c++)
        {
            table.Columns.Add(new TableColumn());
        }

        var group = new TableRowGroup();
        for (int r = 0; r < rows.Count; r++)
        {
            var tr = new TableRow();
            if (r == 0)
            {
                tr.Background = palette.SubtleSurfaceBrush;
            }

            for (int c = 0; c < cols; c++)
            {
                string cellText = c < rows[r].Count ? rows[r][c] : string.Empty;
                var cellPara = new Paragraph { Margin = new Thickness(0), FontSize = 12 };
                if (r == 0)
                {
                    cellPara.FontWeight = FontWeights.SemiBold;
                }

                AppendInlines(cellPara.Inlines, cellText, palette, isDarkTheme, onOpenLink);

                tr.Cells.Add(new TableCell(cellPara)
                {
                    BorderBrush = palette.BorderBrush,
                    BorderThickness = new Thickness(0, 0, c < cols - 1 ? 1 : 0, r < rows.Count - 1 ? 1 : 0),
                    Padding = new Thickness(8, 5, 8, 5)
                });
            }

            group.Rows.Add(tr);
        }

        table.RowGroups.Add(group);
        return table;
    }

    private static Block BuildImageBlock(string alt, string source, ThemePalette palette)
    {
        var img = CreateImageElement(alt, source, maxW: 480);
        var stack = new StackPanel();
        stack.Children.Add(img);
        if (!string.IsNullOrWhiteSpace(alt))
        {
            stack.Children.Add(new TextBlock
            {
                Text = alt,
                FontSize = 11,
                Foreground = palette.MutedBrush,
                Margin = new Thickness(0, 3, 0, 0)
            });
        }

        return new BlockUIContainer(new Border
        {
            Margin = new Thickness(0, 6, 0, 8),
            Child = stack
        });
    }

    private static Image CreateImageElement(string alt, string source, double maxW)
    {
        var img = new Image
        {
            MaxWidth = maxW,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = string.IsNullOrWhiteSpace(alt) ? source : alt
        };

        string trimmed = source.Trim();
        try
        {
            if (trimmed.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                int comma = trimmed.IndexOf(',');
                if (comma > 0)
                {
                    byte[] bytes = Convert.FromBase64String(trimmed[(comma + 1)..]);
                    img.Source = LoadBitmapFromBytes(bytes);
                }
            }
            else if (File.Exists(trimmed))
            {
                img.Source = LoadBitmapFromBytes(File.ReadAllBytes(trimmed));
            }
            else if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                _ = LoadRemoteImageAsync(img, trimmed);
            }
        }
        catch
        {
        }

        return img;
    }

    private static async Task LoadRemoteImageAsync(Image target, string url)
    {
        try
        {
            byte[] bytes = await ImageHttpClient.GetByteArrayAsync(url);
            var bmp = LoadBitmapFromBytes(bytes);
            await target.Dispatcher.InvokeAsync(() => target.Source = bmp);
        }
        catch
        {
        }
    }

    private static BitmapImage LoadBitmapFromBytes(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private sealed record ThemePalette(
        Brush TextBrush,
        Brush MutedBrush,
        Brush AccentBrush,
        Brush LinkBrush,
        Brush CodeBgBrush,
        Brush CodeTextBrush,
        Brush BorderBrush,
        Brush SubtleSurfaceBrush)
    {
        public static ThemePalette For(bool isDark)
        {
            return isDark
                ? new(
                    Create(242, 244, 248),
                    Create(165, 174, 190),
                    Create(96, 165, 250),
                    Create(96, 165, 250),
                    Create(24, 27, 34),
                    Create(210, 224, 248),
                    Create(64, 70, 84),
                    Create(38, 42, 52))
                : new(
                    Create(24, 28, 36),
                    Create(92, 102, 118),
                    Create(37, 99, 235),
                    Create(37, 99, 235),
                    Create(240, 243, 249),
                    Create(30, 41, 59),
                    Create(210, 218, 230),
                    Create(243, 246, 252));
        }

        private static SolidColorBrush Create(byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromRgb(r, g, b));
            br.Freeze();
            return br;
        }
    }

    [GeneratedRegex(@"^\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\]\s*", RegexOptions.IgnoreCase)]
    private static partial Regex CalloutHeaderRegex();

    [GeneratedRegex(@"^(#{1,4})\s+(.+)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^[-*_]{3,}$")]
    private static partial Regex HorizontalRuleRegex();

    [GeneratedRegex(@"^!\[(.*?)\]\((.+?)\)$")]
    private static partial Regex StandaloneImageRegex();

    [GeneratedRegex(@"^\|[\s:-]+(?:\|[\s:-]+)*\|?$")]
    private static partial Regex TableSeparatorRegex();

    [GeneratedRegex(@"^[-*+]\s+(.+)$")]
    private static partial Regex BulletItemRegex();

    [GeneratedRegex(@"^\d+\.\s+(.+)$")]
    private static partial Regex NumberedItemRegex();

    [GeneratedRegex(
        @"(?<inlineimg>!\[(?<imgalt>[^\]]*)\]\((?<imgurl>[^)]+)\))" +
        @"|(?<link>\[(?<linktext>[^\]]+)\]\((?<linkurl>[^)]+)\))" +
        @"|(?<code>`(?<codebody>[^`]+)`)" +
        @"|(?<math>(?<!\$)\$(?!\$)(?<mathbody>[^$\n]+?)\$(?!\$)|\\\((?<parenmathbody>.+?)\\\))" +
        @"|(?<bolditalic>\*\*\*(?<bi>[^*]+?)\*\*\*)" +
        @"|(?<bold>\*\*(?<b>[^*]+?)\*\*)" +
        @"|(?<italic>(?<!\*)\*(?<it>[^*]+?)\*(?!\*))" +
        @"|(?<strike>~~(?<st>[^~]+?)~~)",
        RegexOptions.Singleline)]
    private static partial Regex InlineTokenRegex();
}

