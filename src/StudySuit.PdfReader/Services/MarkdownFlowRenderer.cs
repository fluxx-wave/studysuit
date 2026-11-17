using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
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
    private static readonly FontFamily DefaultAppleFontFamily = new("SF Pro Display, SF Pro Text, SF Pro, -apple-system, BlinkMacSystemFont, Segoe UI Variable Text, Segoe UI, Helvetica Neue, Arial, sans-serif");
    private static readonly HttpClient ImageHttpClient = CreateImageHttpClient();

    private static HttpClient CreateImageHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 6
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "image/jpeg,image/png,image/gif,image/webp,*/*;q=0.5");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        return client;
    }

    public static Func<string, Task<string?>>? FallbackImageResolver { get; set; }
    public static event Action<string, string>? RequestFullScreen3D;

    public static FlowDocument RenderToFlowDocument(
        string markdown,
        bool isDarkTheme,
        Action<string>? onOpenLink = null,
        bool isStreaming = false)
    {
        var palette = ThemePalette.For(isDarkTheme);
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(0),
            ColumnWidth = double.MaxValue,
            FontFamily = DefaultAppleFontFamily,
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
                string langClean = (lang ?? "").Trim().ToLowerInvariant();

                if (langClean is "chart" or "charts" or "graph" or "plot" or "barchart" or "bar-chart" or "linechart" or "line-chart" or "piechart" or "pie-chart" or "donut" or "doughnut" or "scatter" or "scatterplot")
                {
                    doc.Blocks.Add(BuildChartBlock(blockContent, palette, isDarkTheme));
                }
                else if (langClean is "mermaid" or "sequence" or "sequencediagram" or "graph-diagram" or "statediagram" or "classdiagram" or "erdiagram" or "gantt" or "gitgraph" ||
                         blockContent.TrimStart().StartsWith("graph ", StringComparison.OrdinalIgnoreCase) ||
                         blockContent.TrimStart().StartsWith("flowchart ", StringComparison.OrdinalIgnoreCase) ||
                         blockContent.TrimStart().StartsWith("sequenceDiagram", StringComparison.OrdinalIgnoreCase) ||
                         blockContent.TrimStart().StartsWith("classDiagram", StringComparison.OrdinalIgnoreCase))
                {
                    doc.Blocks.Add(BuildMermaidBlock(blockContent, palette, isDarkTheme));
                }
                else if (langClean is "sketchfab" or "3d" or "3d-model" or "model3d" or "sketchfab-model")
                {
                    doc.Blocks.Add(BuildSketchfab3DBlock(blockContent, palette, isDarkTheme, isStreaming: isStreaming));
                }
                else if (langClean is "card" or "cards" or "grid" or "card-grid" or "cards-grid" or "tile" or "tiles")
                {
                    doc.Blocks.Add(BuildCardsBlock(blockContent, palette, isDarkTheme, onOpenLink));
                }
                else if (langClean is "diagram" or "flow")
                {
                    doc.Blocks.Add(BuildDiagramBlock(blockContent, palette));
                }
                else if (langClean is "metrics" or "kpi" or "stats")
                {
                    doc.Blocks.Add(BuildMetricsBlock(blockContent, palette));
                }
                else if (langClean is "callout" or "alert")
                {
                    doc.Blocks.Add(BuildCalloutJsonBlock(blockContent, palette, isDarkTheme, onOpenLink));
                }
                else if (langClean is "math" or "latex" or "chem" or "chemistry" or "equation")
                {
                    doc.Blocks.Add(BuildEquationBlock(blockContent, isDarkTheme));
                }
                else if (langClean is "json" or "")
                {
                    // Auto-detect whether this JSON block is a chart, cards, diagram, or metrics specification:
                    if (TryBuildAutoDetectedJsonBlock(blockContent, palette, isDarkTheme, onOpenLink, out Block? autoBlock))
                    {
                        doc.Blocks.Add(autoBlock!);
                    }
                    else
                    {
                        doc.Blocks.Add(BuildCodeBlock(lang ?? "", blockContent, palette));
                    }
                }
                else
                {
                    doc.Blocks.Add(BuildCodeBlock(lang ?? "", blockContent, palette));
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

            // 3. Standalone Image line: ![alt](src) or [alt](src) where src is an image URL
            var imgMatch = StandaloneImageRegex().Match(trimmed);
            if (imgMatch.Success)
            {
                string rawLine = trimmed;
                string alt = imgMatch.Groups[1].Value;
                string src = imgMatch.Groups[2].Value;

                if (IsSketchfabOr3D(src, alt) || IsSketchfabOr3D(rawLine, alt))
                {
                    doc.Blocks.Add(BuildSketchfab3DBlock(src, palette, isDarkTheme, alt, isStreaming: isStreaming));
                    i++;
                    continue;
                }

                if (rawLine.StartsWith('!') || IsLikelyImageUrl(src))
                {
                    doc.Blocks.Add(BuildImageBlock(alt, src, palette, isDarkTheme));
                    i++;
                    continue;
                }
            }

            // 3b. Standalone Sketchfab 3D Model URL on a single line:
            if (trimmed.StartsWith("https://sketchfab.com/", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("http://sketchfab.com/", StringComparison.OrdinalIgnoreCase))
            {
                doc.Blocks.Add(BuildSketchfab3DBlock(trimmed, palette, isDarkTheme, isStreaming: isStreaming));
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
                if (IsLikelyImageUrl(url))
                {
                    var img = CreateImageElement(label, url, maxW: 460);
                    inlines.Add(new InlineUIContainer(img));
                }
                else
                {
                    var link = new Hyperlink(new Run(label))
                    {
                        Foreground = palette.LinkBrush,
                        ToolTip = url
                    };
                    link.Click += (_, _) => onOpenLink?.Invoke(url);
                    inlines.Add(link);
                }
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

    private static Block BuildMermaidBlock(string code, ThemePalette palette, bool isDarkTheme)
    {
        string cleanCode = code.Trim();
        if (string.IsNullOrWhiteSpace(cleanCode))
        {
            return BuildCodeBlock("mermaid", code, palette);
        }

        try
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(cleanCode);
            string base64 = Convert.ToBase64String(bytes);

            string bgParam = isDarkTheme ? "?bgColor=!0f172a" : "?bgColor=!ffffff";
            string imageUrl = $"https://mermaid.ink/img/{base64}{bgParam}";

            var outerBorder = new Border
            {
                Background = palette.CardBackgroundBrush,
                BorderBrush = palette.BorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(0, 8, 0, 10),
                Padding = new Thickness(12)
            };

            var stack = new StackPanel();

            var headerDock = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };

            var copyBtn = new Button
            {
                Content = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Copy24, FontSize = 12 },
                Padding = new Thickness(6, 3, 6, 3),
                ToolTip = "Copy Mermaid Code",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            copyBtn.Click += (_, _) =>
            {
                try { Clipboard.SetText(cleanCode); } catch { }
            };
            DockPanel.SetDock(copyBtn, Dock.Right);
            headerDock.Children.Add(copyBtn);

            var titleStack = new StackPanel { Orientation = Orientation.Horizontal };
            titleStack.Children.Add(new Wpf.Ui.Controls.SymbolIcon
            {
                Symbol = Wpf.Ui.Controls.SymbolRegular.BranchFork24,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = "Mermaid Diagram",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = palette.TextBrush,
                VerticalAlignment = VerticalAlignment.Center
            });
            headerDock.Children.Add(titleStack);
            stack.Children.Add(headerDock);

            var imgBorder = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = isDarkTheme ? new SolidColorBrush(Color.FromArgb(40, 15, 23, 42)) : new SolidColorBrush(Color.FromArgb(40, 241, 245, 249)),
                Padding = new Thickness(8),
                ClipToBounds = true
            };

            var img = new Image
            {
                MaxWidth = 580,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri(imageUrl);
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.EndInit();
            img.Source = bi;

            imgBorder.Child = img;
            stack.Children.Add(imgBorder);

            outerBorder.Child = stack;
            return new BlockUIContainer(outerBorder);
        }
        catch
        {
            return BuildCodeBlock("mermaid", code, palette);
        }
    }

    public static bool IsSketchfabOr3D(string source, string? alt = null)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return !string.IsNullOrWhiteSpace(alt) && (alt.Contains("3d model", StringComparison.OrdinalIgnoreCase) || alt.Contains("sketchfab", StringComparison.OrdinalIgnoreCase));
        }

        string s = source.ToLowerInvariant();
        string a = (alt ?? "").ToLowerInvariant();

        if (s.Contains("sketchfab.com") || s.Contains("sketchfab") || s.Contains("/models/") || s.Contains("sketchfab-model"))
        {
            return true;
        }

        if (s.Contains("3d-model") || s.Contains("model3d") || s.EndsWith(".glb") || s.EndsWith(".gltf"))
        {
            return true;
        }

        if (a.Contains("3d model") || a.Contains("sketchfab"))
        {
            return true;
        }

        return false;
    }

    private static Block BuildSketchfab3DBlock(string spec, ThemePalette palette, bool isDarkTheme, string? customTitle = null, bool isStreaming = false)
    {
        string raw = spec.Trim();
        string embedUrl = "";
        string title = !string.IsNullOrWhiteSpace(customTitle) ? customTitle : "Interactive 3D Model";

        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            embedUrl = raw;
        }
        else if (TryFlexibleParseJson(raw, out var doc))
        {
            using (doc)
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("embedUrl", out var ep)) embedUrl = ep.GetString() ?? "";
                else if (root.TryGetProperty("url", out var up)) embedUrl = up.GetString() ?? "";
                else if (root.TryGetProperty("uid", out var uip)) embedUrl = $"https://sketchfab.com/models/{uip.GetString()}/embed";
                if (string.IsNullOrWhiteSpace(customTitle))
                {
                    if (root.TryGetProperty("title", out var tp)) title = tp.GetString() ?? title;
                    else if (root.TryGetProperty("name", out var np)) title = np.GetString() ?? title;
                }
            }
        }
        else
        {
            var match = Regex.Match(raw, @"https://sketchfab\.com/models/([a-zA-Z0-9_-]+)(?:/embed)?");
            if (match.Success)
            {
                embedUrl = $"https://sketchfab.com/models/{match.Groups[1].Value}/embed";
            }
        }

        if (string.IsNullOrWhiteSpace(embedUrl))
        {
            embedUrl = "https://sketchfab.com/models/abaa9a651c834cdaa67072b32fb0024f/embed";
            if (string.IsNullOrWhiteSpace(customTitle))
            {
                title = "Eukaryote: Animal Cell";
            }
        }

        // Ensure embed URL has /embed format
        if (!embedUrl.Contains("/embed"))
        {
            int qIdx = embedUrl.IndexOf('?');
            if (qIdx > 0)
            {
                embedUrl = embedUrl[..qIdx].TrimEnd('/') + "/embed" + embedUrl[qIdx..];
            }
            else
            {
                embedUrl = embedUrl.TrimEnd('/') + "/embed";
            }
        }

        if (!embedUrl.Contains("autostart="))
        {
            embedUrl += (embedUrl.Contains("?") ? "&" : "?") + "autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1";
        }

        embedUrl = ResolveVerified3DUrl(embedUrl, title, raw);

        // While response is actively streaming tokens, DO NOT create or spin up live Chromium WebView2 processes:
        if (isStreaming)
        {
            var streamingPlaceholder = new Border
            {
                Background = palette.CardBackgroundBrush,
                BorderBrush = palette.BorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 8, 0, 10),
                Padding = new Thickness(14, 12, 14, 12),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MaxWidth = 650
            };

            var phDock = new DockPanel();
            var phIcon = new Wpf.Ui.Controls.SymbolIcon
            {
                Symbol = Wpf.Ui.Controls.SymbolRegular.Cube24,
                FontSize = 20,
                Foreground = palette.SecondaryTextBrush,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(phIcon, Dock.Left);
            phDock.Children.Add(phIcon);

            var phTextStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            phTextStack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = palette.TextBrush
            });
            phTextStack.Children.Add(new TextBlock
            {
                Text = "⚡ 3D model embedded • Interactive viewer will activate once response completes...",
                FontSize = 11,
                Foreground = palette.SecondaryTextBrush,
                Margin = new Thickness(0, 3, 0, 0)
            });
            phDock.Children.Add(phTextStack);

            streamingPlaceholder.Child = phDock;
            return new BlockUIContainer(streamingPlaceholder);
        }

        // 1. Check if we ALREADY have this 3D model active in the pool (prevents recreating on every token):
        if (Active3DViewPool.TryGetExistingBlock(embedUrl, out var existingOuter, out var existingBorder, out var existingWv))
        {
            if (existingBorder != null && existingWv != null)
            {
                Active3DViewRegistry.Register(existingBorder, existingWv);
            }
            return new BlockUIContainer(existingOuter!);
        }

        var outer = new Border
        {
            Background = palette.CardBackgroundBrush,
            BorderBrush = palette.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 8, 0, 10),
            Padding = new Thickness(10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxWidth = 650
        };

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };

        var headerDock = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };

        var openExternalBtn = new Button
        {
            Content = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Open24, FontSize = 12 },
            Padding = new Thickness(6, 4, 6, 4),
            ToolTip = "Open in Browser",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        openExternalBtn.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(embedUrl) { UseShellExecute = true }); } catch { }
        };
        DockPanel.SetDock(openExternalBtn, Dock.Right);
        headerDock.Children.Add(openExternalBtn);

        var fullScreenBtn = new Button
        {
            Content = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.FullScreenMaximize24, FontSize = 12 },
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = "Full Screen (In-App)",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        fullScreenBtn.Click += (_, _) =>
        {
            RequestFullScreen3D?.Invoke(embedUrl, title);
        };
        DockPanel.SetDock(fullScreenBtn, Dock.Right);
        headerDock.Children.Add(fullScreenBtn);

        var headerTitleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        headerTitleStack.Children.Add(new Wpf.Ui.Controls.SymbolIcon
        {
            Symbol = Wpf.Ui.Controls.SymbolRegular.Cube24,
            FontSize = 15,
            Foreground = palette.SecondaryTextBrush,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        });

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = palette.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 280
        };
        headerTitleStack.Children.Add(titleBlock);

        var badge = new Border
        {
            Background = palette.SubtleSurfaceBrush,
            BorderBrush = palette.BorderBrush,
            BorderThickness = new Thickness(0.5),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(5, 2, 5, 2),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "3D Interactive",
                FontSize = 9.5,
                FontWeight = FontWeights.Medium,
                Foreground = palette.SecondaryTextBrush
            }
        };
        headerTitleStack.Children.Add(badge);
        headerDock.Children.Add(headerTitleStack);
        stack.Children.Add(headerDock);

        var webViewBorder = new Border
        {
            Height = 420,
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            BorderBrush = palette.BorderBrush,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        try
        {
            var webView = new Microsoft.Web.WebView2.Wpf.WebView2
            {
                Tag = "AiChat3DWebView",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                DefaultBackgroundColor = System.Drawing.Color.FromArgb(15, 23, 42)
            };
            webViewBorder.Child = webView;
            Active3DViewRegistry.Register(webViewBorder, webView);

            _ = Task.Run(async () =>
            {
                try
                {
                    var env = await WebViewMemoryManager.GetSharedEnvironmentAsync().ConfigureAwait(false);
                    await webView.Dispatcher.InvokeAsync(async () =>
                    {
                        await webView.EnsureCoreWebView2Async(env);
                        if (webView.CoreWebView2 != null)
                        {
                            WebViewMemoryManager.ApplyLowMemoryTarget(webView.CoreWebView2);
                            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                            // Fallback if 404 or page load error occurs:
                            webView.NavigationCompleted += (s, e) =>
                            {
                                if (!e.IsSuccess || (e.HttpStatusCode >= 400 && e.HttpStatusCode < 600))
                                {
                                    string fbUrl = ResolveFallback3DUrl(title, raw);
                                    if (!string.Equals(embedUrl, fbUrl, StringComparison.OrdinalIgnoreCase))
                                    {
                                        try { webView.CoreWebView2?.Navigate(fbUrl); } catch { }
                                    }
                                }
                            };

                            webView.CoreWebView2.Navigate(embedUrl);
                        }
                        else
                        {
                            webView.Source = new Uri(embedUrl);
                        }
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[WebView2] 3D async init failed: {ex.Message}");
                    await webView.Dispatcher.InvokeAsync(() =>
                    {
                        try { webView.Source = new Uri(embedUrl); } catch { }
                    });
                }
            });

            Active3DViewPool.Register(embedUrl, outer, webViewBorder, webView);
        }
        catch
        {
            var fbBtn = new Button
            {
                Content = "Click to View 3D Interactive Model",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            fbBtn.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(embedUrl) { UseShellExecute = true }); } catch { }
            };
            webViewBorder.Child = fbBtn;
        }

        stack.Children.Add(webViewBorder);

        var footerDock = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };

        var fsLinkBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.FullScreenMaximize24, FontSize = 11, Margin = new Thickness(0, 0, 4, 0) },
                    new TextBlock { Text = "Full Screen", FontSize = 11, FontWeight = FontWeights.SemiBold }
                }
            },
            Padding = new Thickness(8, 2, 8, 2),
            Background = palette.SubtleSurfaceBrush,
            BorderBrush = palette.BorderBrush,
            BorderThickness = new Thickness(0.5),
            Cursor = Cursors.Hand,
            ToolTip = "Open interactive 3D model in full screen inside the app"
        };
        fsLinkBtn.Click += (_, _) => RequestFullScreen3D?.Invoke(embedUrl, title);
        DockPanel.SetDock(fsLinkBtn, Dock.Right);
        footerDock.Children.Add(fsLinkBtn);

        var footerHint = new TextBlock
        {
            Text = "Drag to Rotate 360° • Scroll to Zoom",
            FontSize = 10.5,
            Foreground = palette.SecondaryTextBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        footerDock.Children.Add(footerHint);
        stack.Children.Add(footerDock);

        outer.Child = stack;
        return new BlockUIContainer(outer);
    }

    private static string ResolveVerified3DUrl(string rawUrl, string title, string spec)
    {
        string text = (title + " " + spec + " " + rawUrl).ToLowerInvariant();

        if (rawUrl.Contains("f2662c1d0925482e99f57297e68fa707", StringComparison.OrdinalIgnoreCase) ||
            ((text.Contains("solar") || text.Contains("planetary")) && !rawUrl.Contains("f7896d085f474ef28631d88129268411")))
        {
            return "https://sketchfab.com/models/f7896d085f474ef28631d88129268411/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1";
        }

        return rawUrl;
    }

    private static string ResolveFallback3DUrl(string title, string spec)
    {
        string text = (title + " " + spec).ToLowerInvariant();
        if (text.Contains("solar") || text.Contains("planet") || text.Contains("sun") || text.Contains("orbit"))
        {
            return "https://sketchfab.com/models/f7896d085f474ef28631d88129268411/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1";
        }
        if (text.Contains("heart") || text.Contains("cardiac"))
        {
            return "https://sketchfab.com/models/775d6629622740de8a5ed61a959c7506/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1";
        }
        if (text.Contains("brain"))
        {
            return "https://sketchfab.com/models/d84f98de4a0a4d509d5df6e52936823c/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1";
        }
        if (text.Contains("dna") || text.Contains("helix"))
        {
            return "https://sketchfab.com/models/60e95170b37549e3b45ee490b74bb112/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1";
        }
        if (text.Contains("plant cell"))
        {
            return "https://sketchfab.com/models/06c34533b4f441569bfa207aff7c8a19/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1";
        }
        if (text.Contains("cell"))
        {
            return "https://sketchfab.com/models/abaa9a651c834cdaa67072b32fb0024f/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1";
        }
        if (text.Contains("atom") || text.Contains("bohr"))
        {
            return "https://sketchfab.com/models/0b15c013059844d7a26c1f16752f8b61/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1";
        }
        if (text.Contains("eye"))
        {
            return "https://sketchfab.com/models/6adbd6538cd146d484c9ad950be69aa5/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1";
        }
        if (text.Contains("skeleton") || text.Contains("skull") || text.Contains("bone"))
        {
            return "https://sketchfab.com/models/acf6b25134d945f8b0bc8ae5e04861df/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1";
        }
        if (text.Contains("engine") || text.Contains("motor"))
        {
            return "https://sketchfab.com/models/eea9d9252ab14298b50699a471dc2cee/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1";
        }
        return "https://sketchfab.com/models/abaa9a651c834cdaa67072b32fb0024f/embed?autostart=1&internal=1&tracking=0&ui_infos=0&ui_snapshots=1&ui_stop=0&ui_watermark=1";
    }

    private static Block BuildCardsBlock(
        string spec,
        ThemePalette palette,
        bool isDarkTheme,
        Action<string>? onOpenLink)
    {
        try
        {
            if (!TryFlexibleParseJson(spec, out var doc))
            {
                if (TryBuildFallbackCardsFromText(spec, palette, isDarkTheme, onOpenLink, out var fallback) && fallback is not null)
                {
                    return fallback;
                }
                return BuildLenientCardsFromRawText(spec, palette, isDarkTheme, onOpenLink);
            }

            using (doc)
            {
                var root = doc.RootElement;
                string? sectionTitle = null;
                int columns = 2;
                var cardElements = new List<JsonElement>();

                if (root.ValueKind == JsonValueKind.Array)
                {
                    // Root is directly an array of cards: [ {...}, {...} ]
                    cardElements.AddRange(root.EnumerateArray());
                    columns = Math.Clamp(cardElements.Count switch { 1 => 1, 2 => 2, 3 => 3, _ => 2 }, 1, 3);
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    if (TryGetArrayProperty(root, out var cardsArr, "cards", "grid", "items", "list", "elements", "content", "data"))
                    {
                        cardElements.AddRange(cardsArr.EnumerateArray());
                        sectionTitle = GetFirstStringProperty(root, "title", "header", "name", "heading");
                        if (TryGetPropertyCaseInsensitive(root, out var cp, "columns", "cols") && cp.TryGetInt32(out int cVal))
                        {
                            columns = Math.Clamp(cVal, 1, 3);
                        }
                        else
                        {
                            columns = Math.Clamp(cardElements.Count switch { 1 => 1, 2 => 2, 3 => 3, _ => 2 }, 1, 3);
                        }
                    }
                    else
                    {
                        // Root is a single card object: { "title": "...", "body": "..." }
                        cardElements.Add(root);
                        columns = 1;
                    }
                }

                if (cardElements.Count == 0)
                {
                    if (TryBuildFallbackCardsFromText(spec, palette, isDarkTheme, onOpenLink, out var fallback) && fallback is not null)
                    {
                        return fallback;
                    }
                    return BuildLenientCardsFromRawText(spec, palette, isDarkTheme, onOpenLink);
                }

                var outer = new StackPanel();
                if (!string.IsNullOrWhiteSpace(sectionTitle))
                {
                    outer.Children.Add(new TextBlock
                    {
                        Text = sectionTitle,
                        FontSize = 13.5,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = palette.TextBrush,
                        Margin = new Thickness(0, 0, 0, 8)
                    });
                }

                var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = columns };
                foreach (var card in cardElements)
                {
                    if (card.ValueKind != JsonValueKind.Object) continue;

                    string cardTitle = GetFirstStringProperty(card, "title", "header", "name", "heading") ?? "";
                    string badge = GetFirstStringProperty(card, "badge", "tag", "category", "label") ?? "";
                    string? colorHex = GetFirstStringProperty(card, "color", "accent", "accentColor");
                    Brush accent = ParseHexBrush(colorHex, palette.AccentBrush);

                    var cardStack = new StackPanel();
                    if (!string.IsNullOrWhiteSpace(cardTitle) || !string.IsNullOrWhiteSpace(badge))
                    {
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

                        if (!string.IsNullOrWhiteSpace(cardTitle))
                        {
                            header.Children.Add(new TextBlock
                            {
                                Text = cardTitle,
                                FontSize = 12.5,
                                FontWeight = FontWeights.SemiBold,
                                Foreground = palette.TextBrush,
                                TextWrapping = TextWrapping.Wrap
                            });
                        }

                        cardStack.Children.Add(header);
                    }

                    // Check for items/points/bullets/list
                    bool hasItems = false;
                    if (TryGetArrayProperty(card, out var itemsArr, "items", "points", "bullets", "list", "features"))
                    {
                        foreach (var item in itemsArr.EnumerateArray())
                        {
                            string itemText = item.ValueKind == JsonValueKind.String
                                ? (item.GetString() ?? "")
                                : item.ToString();

                            if (string.IsNullOrWhiteSpace(itemText)) continue;
                            hasItems = true;

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

                    if (!hasItems)
                    {
                        string? bodyText = GetFirstStringProperty(card, "body", "content", "description", "text", "details", "summary");
                        if (!string.IsNullOrWhiteSpace(bodyText))
                        {
                            var tb = new TextBlock
                            {
                                FontSize = 11.8,
                                Foreground = palette.TextBrush,
                                TextWrapping = TextWrapping.Wrap
                            };
                            AppendInlines(tb.Inlines, bodyText, palette, isDarkTheme, onOpenLink);
                            cardStack.Children.Add(tb);
                        }
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

                outer.Children.Add(grid);
                return new BlockUIContainer(new Border
                {
                    Margin = new Thickness(0, 4, 0, 8),
                    Child = outer
                });
            }
        }
        catch
        {
            if (TryBuildFallbackCardsFromText(spec, palette, isDarkTheme, onOpenLink, out var fallback) && fallback is not null)
            {
                return fallback;
            }
            return BuildLenientCardsFromRawText(spec, palette, isDarkTheme, onOpenLink);
        }
    }

    private static Block BuildDiagramBlock(string spec, ThemePalette palette)
    {
        try
        {
            if (!TryFlexibleParseJson(spec, out var doc))
            {
                return BuildCodeBlock("diagram", spec, palette);
            }

            using (doc)
            {
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
            if (!TryFlexibleParseJson(spec, out var doc))
            {
                return BuildCodeBlock("metrics", spec, palette);
            }

            using (doc)
            {
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
            if (!TryFlexibleParseJson(spec, out var doc))
            {
                return BuildCodeBlock("callout", spec, palette);
            }

            using (doc)
            {
                var root = doc.RootElement;
                string kind = root.TryGetProperty("type", out var tp) ? (tp.GetString() ?? "NOTE").ToUpperInvariant() : "NOTE";
                string title = root.TryGetProperty("title", out var tl) ? tl.GetString() ?? kind : kind;
                string body = root.TryGetProperty("body", out var bp) ? bp.GetString() ?? "" : "";
                return BuildAlertCard(kind, title, body, palette, isDarkTheme, onOpenLink);
            }
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
        if (!TryFlexibleParseJson(spec, out var doc))
        {
            throw new FormatException("Invalid chart JSON specification.");
        }

        using (doc)
        {
            var root = doc.RootElement;

        string typeRaw = (GetFirstStringProperty(root, "type", "chartType", "kind") ?? "bar").ToLowerInvariant();
        string title = GetFirstStringProperty(root, "title", "header", "name") ?? "Chart";
        string xLabel = GetFirstStringProperty(root, "xLabel", "xlabel", "xAxis", "x_axis") ?? "";
        string yLabel = GetFirstStringProperty(root, "yLabel", "ylabel", "yAxis", "y_axis") ?? "";

        if (typeRaw is "function" or "functionplot" or "graph" ||
            (typeRaw is "plot" && (root.TryGetProperty("expr", out _) || root.TryGetProperty("expression", out _))))
        {
            string expr = GetFirstStringProperty(root, "expr", "expression", "formula", "fn") ?? "x";
            double xMin = TryGetPropertyCaseInsensitive(root, out var xminp, "xMin", "xmin") ? ParseDoubleFlexibly(xminp, -10) : -10;
            double xMax = TryGetPropertyCaseInsensitive(root, out var xmaxp, "xMax", "xmax") ? ParseDoubleFlexibly(xmaxp, 10) : 10;
            double yMin = TryGetPropertyCaseInsensitive(root, out var yminp, "yMin", "ymin") ? ParseDoubleFlexibly(yminp, -5) : -5;
            double yMax = TryGetPropertyCaseInsensitive(root, out var ymaxp, "yMax", "ymax") ? ParseDoubleFlexibly(ymaxp, 5) : 5;
            return ChartRenderer.RenderFunctionGraph(expr, title, xMin, xMax, yMin, yMax, 560, 330);
        }

        var chartType = typeRaw switch
        {
            "line" or "linechart" or "line-chart" or "trend" or "timeseries" => StudyChartType.Line,
            "pie" or "piechart" or "pie-chart" or "donut" or "doughnut" => StudyChartType.Pie,
            "scatter" or "scatterplot" or "xy" => StudyChartType.Scatter,
            _ => StudyChartType.Bar
        };

        var points = new List<ChartDataPoint>();
        bool hasData = TryGetPropertyCaseInsensitive(root, out var dataProp, "data", "points", "values", "items", "series");
        if (hasData)
        {
            if (dataProp.ValueKind == JsonValueKind.Array)
            {
                int idx = 0;
                foreach (var item in dataProp.EnumerateArray())
                {
                    idx++;
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        string label = GetFirstStringProperty(item, "label", "name", "x", "key", "title", "category") ?? $"Item {idx}";
                        double val = 0;
                        if (TryGetPropertyCaseInsensitive(item, out var valProp, "value", "val", "y", "count", "amount", "score", "v"))
                        {
                            val = ParseDoubleFlexibly(valProp, 0);
                        }
                        double xVal = idx;
                        if (TryGetPropertyCaseInsensitive(item, out var xProp, "x", "xval", "xValue", "index"))
                        {
                            xVal = ParseDoubleFlexibly(xProp, idx);
                        }
                        points.Add(new ChartDataPoint { Label = label, Value = val, XValue = xVal });
                    }
                    else if (item.ValueKind is JsonValueKind.Number or JsonValueKind.String)
                    {
                        double val = ParseDoubleFlexibly(item, 0);
                        points.Add(new ChartDataPoint { Label = $"Item {idx}", Value = val, XValue = idx });
                    }
                }
            }
            else if (dataProp.ValueKind == JsonValueKind.Object)
            {
                int idx = 0;
                foreach (var prop in dataProp.EnumerateObject())
                {
                    idx++;
                    double val = ParseDoubleFlexibly(prop.Value, 0);
                    points.Add(new ChartDataPoint { Label = prop.Name, Value = val, XValue = idx });
                }
            }
        }

        return ChartRenderer.RenderDataChart(chartType, title, xLabel, yLabel, points, 560, 330);
        }
    }

    private static bool TryBuildAutoDetectedJsonBlock(
        string content,
        ThemePalette palette,
        bool isDarkTheme,
        Action<string>? onOpenLink,
        out Block? resultBlock)
    {
        resultBlock = null;
        string trimmed = content.Trim();
        if (!TryFlexibleParseJson(trimmed, out var doc))
        {
            return false;
        }

        try
        {
            using (doc)
            {
                var root = doc.RootElement;

                // 0. Sketchfab 3D Model detection:
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("sketchfab", out _) || root.TryGetProperty("embedUrl", out _) ||
                        (root.TryGetProperty("url", out var u) && (u.GetString() ?? "").Contains("sketchfab")) ||
                        (root.TryGetProperty("type", out var typ) && (typ.GetString() ?? "").Contains("3d")))
                    {
                        resultBlock = BuildSketchfab3DBlock(trimmed, palette, isDarkTheme);
                        return true;
                    }
                }

                // 1. Chart detection:
                // Check if root has "type" (bar/line/pie/etc) and ("data" or "points" or "expr" or "expression")
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("type", out var typeProp) &&
                        (root.TryGetProperty("data", out _) || root.TryGetProperty("points", out _) ||
                         root.TryGetProperty("expr", out _) || root.TryGetProperty("expression", out _) ||
                         root.TryGetProperty("values", out _)))
                    {
                        string t = (typeProp.GetString() ?? "").ToLowerInvariant();
                        if (t is "bar" or "column" or "line" or "pie" or "donut" or "scatter" or "function" or "graph" or "plot" ||
                            t.Contains("chart"))
                        {
                            resultBlock = BuildChartBlock(trimmed, palette, isDarkTheme);
                            return true;
                        }
                    }
                }

                // 2. Cards detection:
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("cards", out var cArr) && cArr.ValueKind == JsonValueKind.Array)
                {
                    resultBlock = BuildCardsBlock(trimmed, palette, isDarkTheme, onOpenLink);
                    return true;
                }

                if (root.ValueKind == JsonValueKind.Array)
                {
                    var first = root.EnumerateArray().FirstOrDefault();
                    if (first.ValueKind == JsonValueKind.Object)
                    {
                        bool hasCardMarkers = (first.TryGetProperty("title", out _) || first.TryGetProperty("header", out _) || first.TryGetProperty("name", out _)) &&
                                              (first.TryGetProperty("items", out _) || first.TryGetProperty("points", out _) || first.TryGetProperty("body", out _) || first.TryGetProperty("content", out _));
                        if (hasCardMarkers)
                        {
                            resultBlock = BuildCardsBlock(trimmed, palette, isDarkTheme, onOpenLink);
                            return true;
                        }

                        // Metrics detection: array of { "label": ..., "value": ... }
                        bool hasMetricMarkers = first.TryGetProperty("label", out _) && first.TryGetProperty("value", out _);
                        if (hasMetricMarkers)
                        {
                            resultBlock = BuildMetricsBlock(trimmed, palette);
                            return true;
                        }
                    }
                }

                // 3. Diagram detection:
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("steps", out var stepsArr) && stepsArr.ValueKind == JsonValueKind.Array)
                {
                    resultBlock = BuildDiagramBlock(trimmed, palette);
                    return true;
                }

                // 4. Metrics detection (object with "metrics" array):
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("metrics", out var metArr) && metArr.ValueKind == JsonValueKind.Array)
                {
                    resultBlock = BuildMetricsBlock(trimmed, palette);
                    return true;
                }
            }
        }
        catch
        {
            // Invalid JSON, fall through
        }

        return false;
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, out JsonElement value, params string[] propertyNames)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in propertyNames)
            {
                if (element.TryGetProperty(name, out value))
                {
                    return true;
                }
            }
            foreach (var prop in element.EnumerateObject())
            {
                foreach (var name in propertyNames)
                {
                    if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = prop.Value;
                        return true;
                    }
                }
            }
        }

        value = default;
        return false;
    }

    private static string? GetFirstStringProperty(JsonElement element, params string[] propertyNames)
    {
        if (TryGetPropertyCaseInsensitive(element, out var prop, propertyNames))
        {
            if (prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
            return prop.ToString();
        }
        return null;
    }

    private static bool TryGetArrayProperty(JsonElement element, out JsonElement arrayElement, params string[] propertyNames)
    {
        if (TryGetPropertyCaseInsensitive(element, out arrayElement, propertyNames) && arrayElement.ValueKind == JsonValueKind.Array)
        {
            return true;
        }
        return false;
    }

    private static bool TryFlexibleParseJson(string rawSpec, [NotNullWhen(true)] out JsonDocument? doc)
    {
        doc = null;
        if (string.IsNullOrWhiteSpace(rawSpec))
        {
            return false;
        }

        string trimmed = rawSpec.Trim();
        var options = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

        // 1. Fast direct parse
        try
        {
            doc = JsonDocument.Parse(trimmed, options);
            return true;
        }
        catch (JsonException)
        {
        }

        // 2. Strip stray markdown code fences inside spec (e.g. ```json ... ```)
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            int firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0)
            {
                trimmed = trimmed[(firstNewline + 1)..].Trim();
            }
            if (trimmed.EndsWith("```", StringComparison.Ordinal))
            {
                trimmed = trimmed[..^3].Trim();
            }
            try
            {
                doc = JsonDocument.Parse(trimmed, options);
                return true;
            }
            catch (JsonException)
            {
            }
        }

        // 3. Normalize quotes and common characters
        string cleaned = trimmed
            .Replace('“', '"')
            .Replace('”', '"')
            .Replace('‘', '\'')
            .Replace('’', '\'');

        // Extract substring between first '{' or '[' and last '}' or ']'
        int firstBrace = cleaned.IndexOfAny(['{', '[']);
        if (firstBrace >= 0)
        {
            int lastBrace = cleaned.LastIndexOfAny(['}', ']']);
            if (lastBrace > firstBrace)
            {
                cleaned = cleaned.Substring(firstBrace, lastBrace - firstBrace + 1).Trim();
            }
            else
            {
                cleaned = cleaned[firstBrace..].Trim();
            }
        }

        try
        {
            doc = JsonDocument.Parse(cleaned, options);
            return true;
        }
        catch (JsonException)
        {
        }

        // 4. Bracket/Brace balancing & string escape repair
        string repaired = RepairDelimitersAndStrings(cleaned);
        try
        {
            doc = JsonDocument.Parse(repaired, options);
            return true;
        }
        catch (JsonException)
        {
        }

        return false;
    }

    private static string RepairDelimitersAndStrings(string input)
    {
        var sb = new System.Text.StringBuilder(input.Length + 16);
        var stack = new Stack<char>();
        bool inString = false;
        bool escape = false;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    sb.Append(c);
                }
                else if (c == '\\')
                {
                    escape = true;
                    sb.Append(c);
                }
                else if (c == '"')
                {
                    inString = false;
                    sb.Append(c);
                }
                else if (c == '\n')
                {
                    sb.Append("\\n");
                }
                else if (c == '\r')
                {
                    // omit carriage return
                }
                else if (c == '\t')
                {
                    sb.Append("\\t");
                }
                else
                {
                    sb.Append(c);
                }
                continue;
            }

            if (c == '"')
            {
                inString = true;
                sb.Append(c);
                continue;
            }

            if (c is '{' or '[')
            {
                stack.Push(c);
                sb.Append(c);
            }
            else if (c == '}')
            {
                if (stack.Count > 0 && stack.Peek() == '{')
                {
                    stack.Pop();
                    sb.Append(c);
                }
                else if (stack.Contains('{'))
                {
                    while (stack.Count > 0 && stack.Peek() != '{')
                    {
                        char unclosed = stack.Pop();
                        sb.Append(unclosed == '[' ? ']' : '}');
                    }
                    if (stack.Count > 0 && stack.Peek() == '{')
                    {
                        stack.Pop();
                    }
                    sb.Append(c);
                }
                // If there was no matching '{' in the stack, drop this extra '}'
            }
            else if (c == ']')
            {
                if (stack.Count > 0 && stack.Peek() == '[')
                {
                    stack.Pop();
                    sb.Append(c);
                }
                else if (stack.Contains('['))
                {
                    while (stack.Count > 0 && stack.Peek() != '[')
                    {
                        char unclosed = stack.Pop();
                        sb.Append(unclosed == '{' ? '}' : ']');
                    }
                    if (stack.Count > 0 && stack.Peek() == '[')
                    {
                        stack.Pop();
                    }
                    sb.Append(c);
                }
                // If there was no matching '[' in the stack, drop this extra ']'
            }
            else
            {
                sb.Append(c);
            }
        }

        if (inString)
        {
            sb.Append('"');
        }

        while (stack.Count > 0)
        {
            char open = stack.Pop();
            sb.Append(open == '{' ? '}' : ']');
        }

        return sb.ToString();
    }

    private static bool TryBuildFallbackCardsFromText(
        string raw,
        ThemePalette palette,
        bool isDarkTheme,
        Action<string>? onOpenLink,
        out Block? fallbackBlock)
    {
        fallbackBlock = null;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        string? sectionTitle = null;
        var headerMatch = Regex.Match(raw, @"^\s*\{?\s*""title""\s*:\s*""(?<title>[^""]+)""\s*,\s*""(?:columns|cards|grid)""");
        if (headerMatch.Success)
        {
            sectionTitle = headerMatch.Groups["title"].Value;
        }

        var cards = new List<(string Title, string Badge, string Color, List<string> Items, string Body)>();
        foreach (Match m in Regex.Matches(raw, @"\{[^{}]*""title""\s*:[^{}]*\}"))
        {
            string block = m.Value;
            var tMatch = Regex.Match(block, @"""title""\s*:\s*""(?<t>[^""]+)""");
            if (!tMatch.Success) continue;
            string cardTitle = tMatch.Groups["t"].Value;
            if (string.Equals(cardTitle, sectionTitle, StringComparison.OrdinalIgnoreCase)) continue;

            string badge = Regex.Match(block, @"""(?:badge|tag|label)""\s*:\s*""(?<b>[^""]+)""").Groups["b"].Value;
            string color = Regex.Match(block, @"""(?:color|accent)""\s*:\s*""(?<c>#[0-9a-fA-F]{3,8})""").Groups["c"].Value;
            string body = Regex.Match(block, @"""(?:body|content|description|text)""\s*:\s*""(?<bd>[^""]+)""").Groups["bd"].Value;

            var items = new List<string>();
            var itemsMatch = Regex.Match(block, @"""(?:items|points|bullets|list)""\s*:\s*\[(?<it>[^\]]*)\]");
            if (itemsMatch.Success)
            {
                foreach (Match itemStr in Regex.Matches(itemsMatch.Groups["it"].Value, @"""(?<item>[^""]+)"""))
                {
                    string it = itemStr.Groups["item"].Value.Trim();
                    if (!string.IsNullOrEmpty(it)) items.Add(it);
                }
            }

            cards.Add((cardTitle, badge, color, items, body));
        }

        if (cards.Count == 0)
        {
            foreach (Match m in Regex.Matches(raw, @"""title""\s*:\s*""(?<title>[^""]+)"""))
            {
                string t = m.Groups["title"].Value;
                if (!string.Equals(t, sectionTitle, StringComparison.OrdinalIgnoreCase) && t.Length < 70)
                {
                    cards.Add((t, "", "", new List<string>(), ""));
                }
            }
        }

        if (cards.Count == 0)
        {
            var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var l in lines)
            {
                if (l.StartsWith("- ") || l.StartsWith("* ") || Regex.IsMatch(l, @"^\d+\.\s+"))
                {
                    string bullet = Regex.Replace(l, @"^[-*\d.]+\s+", "").Trim();
                    int colon = bullet.IndexOf(':');
                    if (colon > 0)
                    {
                        string cTitle = bullet[..colon].Trim();
                        string cBody = bullet[(colon + 1)..].Trim();
                        cards.Add((cTitle, "", "", new List<string>(), cBody));
                    }
                    else if (bullet.Length > 0 && bullet.Length < 60)
                    {
                        cards.Add((bullet, "", "", new List<string>(), ""));
                    }
                }
            }
        }

        if (cards.Count == 0) return false;

        int columns = Math.Clamp(cards.Count switch { 1 => 1, 2 => 2, 3 => 3, _ => 2 }, 1, 3);
        var outer = new StackPanel();
        if (!string.IsNullOrWhiteSpace(sectionTitle))
        {
            outer.Children.Add(new TextBlock
            {
                Text = sectionTitle,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = palette.TextBrush,
                Margin = new Thickness(0, 0, 0, 8)
            });
        }

        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = columns };
        foreach (var (cTitle, cBadge, cColor, cItems, cBody) in cards)
        {
            Brush accent = ParseHexBrush(cColor, palette.AccentBrush);
            var cardStack = new StackPanel();

            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            if (!string.IsNullOrWhiteSpace(cBadge))
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
                        Text = cBadge,
                        FontSize = 10,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = accent
                    }
                };
                DockPanel.SetDock(pill, Dock.Right);
                header.Children.Add(pill);
            }

            if (!string.IsNullOrWhiteSpace(cTitle))
            {
                header.Children.Add(new TextBlock
                {
                    Text = cTitle,
                    FontSize = 12.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = palette.TextBrush,
                    TextWrapping = TextWrapping.Wrap
                });
            }
            cardStack.Children.Add(header);

            if (cItems.Count > 0)
            {
                foreach (var itemText in cItems)
                {
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
            else if (!string.IsNullOrWhiteSpace(cBody))
            {
                var tb = new TextBlock
                {
                    FontSize = 11.8,
                    Foreground = palette.TextBrush,
                    TextWrapping = TextWrapping.Wrap
                };
                AppendInlines(tb.Inlines, cBody, palette, isDarkTheme, onOpenLink);
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

        outer.Children.Add(grid);
        fallbackBlock = new BlockUIContainer(new Border
        {
            Margin = new Thickness(0, 4, 0, 8),
            Child = outer
        });
        return true;
    }

    private static Block BuildLenientCardsFromRawText(
        string raw,
        ThemePalette palette,
        bool isDarkTheme,
        Action<string>? onOpenLink)
    {
        var outer = new StackPanel();
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };

        // Clean out outer fences or tags if present
        string cleaned = Regex.Replace(raw, @"^```[a-zA-Z0-9_-]*\s*", "", RegexOptions.Multiline);
        cleaned = Regex.Replace(cleaned, @"\s*```$", "", RegexOptions.Multiline).Trim();

        // Extract title if present
        var titleMatch = Regex.Match(cleaned, @"""title""\s*:\s*""(?<t>[^""]+)""", RegexOptions.IgnoreCase);
        string? sectionTitle = titleMatch.Success ? titleMatch.Groups["t"].Value : null;

        if (!string.IsNullOrWhiteSpace(sectionTitle))
        {
            outer.Children.Add(new TextBlock
            {
                Text = sectionTitle,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = palette.TextBrush,
                Margin = new Thickness(0, 0, 0, 8)
            });
        }

        Color[] defaultAccents =
        [
            Color.FromRgb(59, 130, 246),  // Blue
            Color.FromRgb(16, 185, 129),  // Emerald
            Color.FromRgb(239, 68, 68),   // Red
            Color.FromRgb(245, 158, 11),  // Amber
            Color.FromRgb(168, 85, 247),  // Purple
            Color.FromRgb(6, 182, 212)    // Cyan
        ];

        // 1. Try regex extraction of cards with title and items/body
        var matches = Regex.Matches(cleaned, @"""title""\s*:\s*""(?<t>[^""]+)""[\s\S]*?(?:""items""\s*:\s*\[(?<it>[^\]]*)\]|""body""\s*:\s*""(?<bd>[^""]*)"")?", RegexOptions.IgnoreCase);
        int cardIndex = 0;

        foreach (Match m in matches)
        {
            string cTitle = m.Groups["t"].Value.Trim();
            if (string.Equals(cTitle, sectionTitle, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrWhiteSpace(cTitle)) continue;

            Brush accent = new SolidColorBrush(defaultAccents[cardIndex % defaultAccents.Length]);
            cardIndex++;

            var cardStack = new StackPanel();
            cardStack.Children.Add(new TextBlock
            {
                Text = cTitle,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = palette.TextBrush,
                Margin = new Thickness(0, 0, 0, 6),
                TextWrapping = TextWrapping.Wrap
            });

            if (m.Groups["it"].Success)
            {
                foreach (Match itemStr in Regex.Matches(m.Groups["it"].Value, @"""(?<item>[^""]+)"""))
                {
                    string it = itemStr.Groups["item"].Value.Trim();
                    if (string.IsNullOrWhiteSpace(it)) continue;
                    var tb = new TextBlock
                    {
                        FontSize = 11.8,
                        Foreground = palette.TextBrush,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 1.5, 0, 1.5)
                    };
                    tb.Inlines.Add(new Run("• ") { Foreground = accent, FontWeight = FontWeights.Bold });
                    AppendInlines(tb.Inlines, it, palette, isDarkTheme, onOpenLink);
                    cardStack.Children.Add(tb);
                }
            }
            else if (m.Groups["bd"].Success && !string.IsNullOrWhiteSpace(m.Groups["bd"].Value))
            {
                var tb = new TextBlock
                {
                    FontSize = 11.8,
                    Foreground = palette.TextBrush,
                    TextWrapping = TextWrapping.Wrap
                };
                AppendInlines(tb.Inlines, m.Groups["bd"].Value.Trim(), palette, isDarkTheme, onOpenLink);
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

        if (grid.Children.Count > 0)
        {
            outer.Children.Add(grid);
            return new BlockUIContainer(new Border
            {
                Margin = new Thickness(0, 4, 0, 8),
                Child = outer
            });
        }

        // 2. Fallback: split by lines/bullets and create styled card items
        var lines = cleaned.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            if (line.StartsWith('{') || line.StartsWith('}') || line.StartsWith('[') || line.StartsWith(']')) continue;
            string cleanLine = Regex.Replace(line, @"^[-*\d.]+\s*", "").Trim();
            cleanLine = cleanLine.Trim('"', ',', ' ');
            if (cleanLine.Length < 3) continue;

            Brush accent = new SolidColorBrush(defaultAccents[cardIndex % defaultAccents.Length]);
            cardIndex++;

            var cardBorder = new Border
            {
                Background = palette.SubtleSurfaceBrush,
                BorderBrush = palette.BorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(10, 8, 10, 8),
                Child = new TextBlock
                {
                    Text = cleanLine,
                    FontSize = 12,
                    Foreground = palette.TextBrush,
                    TextWrapping = TextWrapping.Wrap
                }
            };
            grid.Children.Add(cardBorder);
        }

        if (grid.Children.Count > 0)
        {
            outer.Children.Add(grid);
            return new BlockUIContainer(new Border
            {
                Margin = new Thickness(0, 4, 0, 8),
                Child = outer
            });
        }

        // If completely empty, render a minimal subtle notice
        return new BlockUIContainer(new Border
        {
            Background = palette.SubtleSurfaceBrush,
            BorderBrush = palette.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 4, 0, 8),
            Child = new TextBlock { Text = cleaned, FontSize = 12, Foreground = palette.TextBrush, TextWrapping = TextWrapping.Wrap }
        });
    }

    private static double ParseDoubleFlexibly(JsonElement element, double fallback = 0.0)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out double d))
        {
            return d;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            string s = element.GetString() ?? "";
            s = s.Trim().Replace("%", "").Replace(",", "").Trim();
            if (double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsed))
            {
                return parsed;
            }
        }

        return fallback;
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

    private static Block BuildImageBlock(string alt, string source, ThemePalette palette, bool isDarkTheme = true)
    {
        if (IsSketchfabOr3D(source, alt))
        {
            return BuildSketchfab3DBlock(source, palette, isDarkTheme, alt);
        }

        var stack = new StackPanel();
        var statusText = new TextBlock
        {
            Text = "Visual reference (Click to open)",
            FontSize = 11,
            Foreground = palette.MutedBrush,
            Margin = new Thickness(0, 3, 0, 0),
            Visibility = Visibility.Collapsed
        };

        var card = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = palette.BorderBrush,
            Background = palette.SubtleSurfaceBrush,
            Padding = new Thickness(4),
            Margin = new Thickness(0, 4, 0, 4),
            ClipToBounds = true,
            Visibility = Visibility.Collapsed // Collapsed by default so no flat empty 8px border is visible!
        };

        var img = CreateImageElement(alt, source, card, statusText, maxW: 520);
        card.Child = img;

        stack.Children.Add(card);
        stack.Children.Add(statusText);

        if (!string.IsNullOrWhiteSpace(alt))
        {
            stack.Children.Add(new TextBlock
            {
                Text = alt,
                FontSize = 11,
                Foreground = palette.MutedBrush,
                Margin = new Thickness(2, 2, 2, 0),
                TextWrapping = TextWrapping.Wrap
            });
        }

        return new BlockUIContainer(new Border
        {
            Margin = new Thickness(0, 6, 0, 8),
            Child = stack
        });
    }

    private static Image CreateImageElement(string alt, string source, Border? card = null, TextBlock? statusText = null, double maxW = 520)
    {
        var img = new Image
        {
            MaxWidth = maxW,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = string.IsNullOrWhiteSpace(alt) ? source : $"{alt}\n({source})",
            Cursor = Cursors.Hand
        };

        img.MouseLeftButtonUp += (_, _) =>
        {
            try
            {
                if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(source) { UseShellExecute = true });
                }
            }
            catch
            {
            }
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
                    if (img.Source != null && card != null) card.Visibility = Visibility.Visible;
                }
            }
            else if (File.Exists(trimmed))
            {
                img.Source = LoadBitmapFromBytes(File.ReadAllBytes(trimmed));
                if (img.Source != null && card != null) card.Visibility = Visibility.Visible;
            }
            else if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                _ = LoadRemoteImageAsync(card, img, trimmed, alt, statusText);
            }
        }
        catch
        {
        }

        return img;
    }

    private static async Task<byte[]?> TryFetchImageBytesAsync(string url)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            req.Headers.TryAddWithoutValidation("Accept", "image/jpeg,image/png,image/gif,image/webp,*/*;q=0.5");
            if (Uri.TryCreate(url, UriKind.Absolute, out var parsedUri))
            {
                req.Headers.Referrer = new Uri($"{parsedUri.Scheme}://{parsedUri.Host}/");
            }
            using var resp = await ImageHttpClient.SendAsync(req, HttpCompletionOption.ResponseContentRead).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                return await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }
        catch { }

        // Secondary attempt with Google referrer for CDNs that block blank or direct hotlinking
        try
        {
            using var req2 = new HttpRequestMessage(HttpMethod.Get, url);
            req2.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            req2.Headers.TryAddWithoutValidation("Accept", "image/jpeg,image/png,image/gif,image/webp,*/*;q=0.5");
            req2.Headers.Referrer = new Uri("https://www.google.com/");
            using var resp2 = await ImageHttpClient.SendAsync(req2, HttpCompletionOption.ResponseContentRead).ConfigureAwait(false);
            if (resp2.IsSuccessStatusCode)
            {
                return await resp2.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }
        catch { }

        return null;
    }

    private static async Task LoadRemoteImageAsync(Border? card, Image target, string url, string alt, TextBlock? statusText)
    {
        try
        {
            byte[]? bytes = await TryFetchImageBytesAsync(url).ConfigureAwait(false);
            if (bytes is not null && bytes.Length > 0)
            {
                var bmp = LoadBitmapFromBytes(bytes);
                await target.Dispatcher.InvokeAsync(() =>
                {
                    target.Source = bmp;
                    if (card != null) card.Visibility = Visibility.Visible;
                    if (statusText != null) statusText.Visibility = Visibility.Collapsed;
                });
                return;
            }

            // Fallback attempt via FallbackImageResolver if direct image URL fails/404s
            if (FallbackImageResolver is not null)
            {
                string searchTopic = !string.IsNullOrWhiteSpace(alt)
                    ? ExtractSearchKeywords(alt)
                    : ExtractKeywordsFromUrl(url);

                if (!string.IsNullOrWhiteSpace(searchTopic))
                {
                    try
                    {
                        string? fallbackUrl = await FallbackImageResolver(searchTopic).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(fallbackUrl) && !string.Equals(fallbackUrl, url, StringComparison.OrdinalIgnoreCase))
                        {
                            byte[]? fbBytes = await TryFetchImageBytesAsync(fallbackUrl).ConfigureAwait(false);
                            if (fbBytes is not null && fbBytes.Length > 0)
                            {
                                var bmp = LoadBitmapFromBytes(fbBytes);
                                await target.Dispatcher.InvokeAsync(() =>
                                {
                                    target.Source = bmp;
                                    if (card != null) card.Visibility = Visibility.Visible;
                                    if (statusText != null) statusText.Visibility = Visibility.Collapsed;
                                });
                                return;
                            }
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        // If all attempts fail, never leave an empty flat border visible
        if (card != null)
        {
            await card.Dispatcher.InvokeAsync(() => card.Visibility = Visibility.Collapsed);
        }

        if (statusText != null)
        {
            await statusText.Dispatcher.InvokeAsync(() =>
            {
                string topic = !string.IsNullOrWhiteSpace(alt) ? alt : ExtractKeywordsFromUrl(url);
                statusText.Text = $"🔍 View visual reference: \"{topic}\"";
                statusText.Cursor = Cursors.Hand;
                statusText.Visibility = Visibility.Visible;
                statusText.MouseLeftButtonUp += (_, _) =>
                {
                    try
                    {
                        string searchUrl = $"https://www.google.com/search?tbm=isch&q={Uri.EscapeDataString(topic)}";
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(searchUrl) { UseShellExecute = true });
                    }
                    catch { }
                };
            });
        }
    }

    public static string ExtractSearchKeywords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var cleaned = Regex.Replace(text, @"^(?:a|an|the|this|that|these|those|picture of|image of|photo of|showing its|showing|visual of|diagram of)\s+", "", RegexOptions.IgnoreCase);
        int comma = cleaned.IndexOfAny([';', ':', '(', '\n']);
        if (comma > 3) cleaned = cleaned[..comma];
        return cleaned.Trim();
    }

    private static string ExtractKeywordsFromUrl(string url)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                string file = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
                string clean = Regex.Replace(file, @"[_\-+]+", " ");
                clean = Regex.Replace(clean, @"\b\d+\b", "").Trim();
                if (clean.Length > 3) return clean;
            }
        }
        catch { }
        return "Educational illustration";
    }

    private static BitmapSource LoadBitmapFromBytes(byte[] bytes)
    {
        // 1. Try native WPF BitmapImage (fastest for standard JPEG, PNG, GIF, BMP)
        try
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
        catch
        {
            // 2. SkiaSharp fallback (decodes WebP, AVIF, modern color profiles, exotic encodings)
            try
            {
                using var skBmp = SkiaSharp.SKBitmap.Decode(bytes);
                if (skBmp != null)
                {
                    using var img = SkiaSharp.SKImage.FromBitmap(skBmp);
                    using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                    if (data != null)
                    {
                        using var pngMs = new MemoryStream();
                        data.SaveTo(pngMs);
                        pngMs.Position = 0;
                        var wpfBmp = new BitmapImage();
                        wpfBmp.BeginInit();
                        wpfBmp.CacheOption = BitmapCacheOption.OnLoad;
                        wpfBmp.StreamSource = pngMs;
                        wpfBmp.EndInit();
                        wpfBmp.Freeze();
                        return wpfBmp;
                    }
                }
            }
            catch
            {
            }
            throw;
        }
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
        public Brush CardBackgroundBrush => SubtleSurfaceBrush;
        public Brush SecondaryTextBrush => MutedBrush;

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

    [GeneratedRegex(@"^!?\[(.*?)\]\((https?://[^\s)]+)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex StandaloneImageRegex();

    public static bool IsLikelyImageUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        string u = url.Trim();
        if (u.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return true;

        string path = u;
        int qIdx = path.IndexOf('?');
        if (qIdx >= 0) path = path[..qIdx];
        int fIdx = path.IndexOf('#');
        if (fIdx >= 0) path = path[..fIdx];

        if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string lower = u.ToLowerInvariant();
        return lower.Contains("mm.bing.net/th") ||
               lower.Contains("upload.wikimedia.org") ||
               lower.Contains("images.unsplash.com") ||
               lower.Contains("images.pexels.com") ||
               lower.Contains("img.freepik.com") ||
               lower.Contains("/photo/") ||
               lower.Contains("/photos/") ||
               lower.Contains("/image/") ||
               lower.Contains("/images/") ||
               lower.Contains("format=jpg") ||
               lower.Contains("format=png") ||
               lower.Contains("format=webp");
    }

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

public sealed record Active3DViewRegistration(
    WeakReference<Border> ContainerBorder,
    WeakReference<Microsoft.Web.WebView2.Wpf.WebView2> WebView);

public static class Active3DViewRegistry
{
    private static readonly List<Active3DViewRegistration> _registrations = new();

    public static void Register(Border border, Microsoft.Web.WebView2.Wpf.WebView2 webView)
    {
        lock (_registrations)
        {
            _registrations.RemoveAll(r => !r.ContainerBorder.TryGetTarget(out _) || !r.WebView.TryGetTarget(out _));
            _registrations.Add(new Active3DViewRegistration(
                new WeakReference<Border>(border),
                new WeakReference<Microsoft.Web.WebView2.Wpf.WebView2>(webView)));
        }
    }

    public static List<(Border Border, Microsoft.Web.WebView2.Wpf.WebView2 WebView)> GetActiveViews()
    {
        lock (_registrations)
        {
            _registrations.RemoveAll(r => !r.ContainerBorder.TryGetTarget(out _) || !r.WebView.TryGetTarget(out _));
            var list = new List<(Border Border, Microsoft.Web.WebView2.Wpf.WebView2 WebView)>(_registrations.Count);
            foreach (var r in _registrations)
            {
                if (r.ContainerBorder.TryGetTarget(out var b) && r.WebView.TryGetTarget(out var wv))
                {
                    list.Add((b, wv));
                }
            }
            return list;
        }
    }
}

