using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StudySuit.PdfReader.Services;

/// <summary>
/// Renders LaTeX-style mathematical equations into crisp, textbook-styled BitmapSource images for embedding in notes.
/// Supports fractions (\frac{a}{b}), roots (\sqrt{x}), superscripts (^), subscripts (_), Greek letters, and operators.
/// </summary>
public static class MathEquationRenderer
{
    private static readonly Dictionary<string, string> SymbolMap = new(StringComparer.Ordinal)
    {
        ["\\alpha"] = "α",
        ["\\beta"] = "β",
        ["\\gamma"] = "γ",
        ["\\delta"] = "δ",
        ["\\epsilon"] = "ε",
        ["\\theta"] = "θ",
        ["\\lambda"] = "λ",
        ["\\mu"] = "μ",
        ["\\pi"] = "π",
        ["\\rho"] = "ρ",
        ["\\sigma"] = "σ",
        ["\\phi"] = "φ",
        ["\\omega"] = "ω",
        ["\\Delta"] = "Δ",
        ["\\Sigma"] = "Σ",
        ["\\Omega"] = "Ω",
        ["\\sum"] = "∑",
        ["\\prod"] = "∏",
        ["\\int"] = "∫",
        ["\\infty"] = "∞",
        ["\\pm"] = "±",
        ["\\times"] = "×",
        ["\\cdot"] = "·",
        ["\\div"] = "÷",
        ["\\leq"] = "≤",
        ["\\geq"] = "≥",
        ["\\neq"] = "≠",
        ["\\approx"] = "≈",
        ["\\equiv"] = "≡",
        ["\\rightarrow"] = "→",
        ["\\to"] = "→",
        ["\\leftarrow"] = "←",
        ["\\Rightarrow"] = "⇒",
        ["\\partial"] = "∂",
        ["\\nabla"] = "∇",
        ["\\forall"] = "∀",
        ["\\exists"] = "∃",
        ["\\in"] = "∈",
        ["\\notin"] = "∉",
        ["\\subset"] = "⊂",
        ["\\cup"] = "∪",
        ["\\cap"] = "∩",
        ["\\lim"] = "lim",
        ["\\sin"] = "sin",
        ["\\cos"] = "cos",
        ["\\tan"] = "tan",
        ["\\log"] = "log",
        ["\\ln"] = "ln"
    };

    public static BitmapSource RenderEquation(string latex, double fontSize = 24.0, bool darkBackground = false)
    {
        string source = string.IsNullOrWhiteSpace(latex) ? "x = y" : latex.Trim();
        Color textColor = darkBackground ? Colors.White : Color.FromRgb(24, 24, 32);
        Color bgColor = darkBackground ? Color.FromRgb(32, 34, 42) : Color.FromRgb(250, 251, 254);
        Color borderColor = darkBackground ? Color.FromRgb(75, 82, 98) : Color.FromRgb(215, 222, 234);

        var textBrush = new SolidColorBrush(textColor);
        var bgBrush = new SolidColorBrush(bgColor);
        var borderPen = new Pen(new SolidColorBrush(borderColor), 1.2);
        textBrush.Freeze();
        bgBrush.Freeze();
        borderPen.Freeze();

        var typeface = new Typeface(new FontFamily("Cambria Math, Cambria, Georgia"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var nodes = ParseSequence(source);
        var measured = MeasureSequence(nodes, typeface, fontSize, textBrush);

        const double padX = 18;
        const double padY = 12;
        double width = Math.Max(48, Math.Ceiling(measured.Width + padX * 2));
        double height = Math.Max(38, Math.Ceiling(measured.Height + padY * 2));

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(bgBrush, borderPen, new Rect(0.5, 0.5, width - 1, height - 1), 8, 8);
            DrawSequence(dc, measured, padX, padY + measured.BaselineY, textBrush);
        }

        const double scale = 2.0; // Render at 192 DPI for razor-sharp inline equation display
        var rtb = new RenderTargetBitmap(
            (int)Math.Ceiling(width * scale),
            (int)Math.Ceiling(height * scale),
            96 * scale,
            96 * scale,
            PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private abstract record MathNode;
    private sealed record TextNode(string Text) : MathNode;
    private sealed record FracNode(List<MathNode> Numerator, List<MathNode> Denominator) : MathNode;
    private sealed record SqrtNode(List<MathNode> Inner) : MathNode;
    private sealed record ScriptNode(MathNode Base, List<MathNode>? Sup, List<MathNode>? Sub) : MathNode;

    private sealed class MeasuredSequence
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public double BaselineY { get; set; }
        public List<MeasuredItem> Items { get; } = [];
    }

    private abstract record MeasuredItem(double OffsetX, double Width, double Ascent, double Descent);
    private sealed record MeasuredText(double OffsetX, double Width, double Ascent, double Descent, FormattedText Formatted)
        : MeasuredItem(OffsetX, Width, Ascent, Descent);
    private sealed record MeasuredFrac(double OffsetX, double Width, double Ascent, double Descent, MeasuredSequence Num, MeasuredSequence Den)
        : MeasuredItem(OffsetX, Width, Ascent, Descent);
    private sealed record MeasuredSqrt(double OffsetX, double Width, double Ascent, double Descent, MeasuredSequence Inner)
        : MeasuredItem(OffsetX, Width, Ascent, Descent);
    private sealed record MeasuredScript(
        double OffsetX,
        double Width,
        double Ascent,
        double Descent,
        MeasuredSequence BaseSeq,
        MeasuredSequence? SupSeq,
        MeasuredSequence? SubSeq,
        double FontSize)
        : MeasuredItem(OffsetX, Width, Ascent, Descent);

    private static List<MathNode> ParseSequence(string input)
    {
        int pos = 0;
        return ParseUntil(input, ref pos, stopChar: null);
    }

    private static List<MathNode> ParseUntil(string s, ref int pos, char? stopChar)
    {
        var nodes = new List<MathNode>();
        while (pos < s.Length)
        {
            char c = s[pos];
            if (stopChar.HasValue && c == stopChar.Value)
            {
                pos++;
                break;
            }

            if (char.IsWhiteSpace(c))
            {
                pos++;
                if (nodes.Count > 0 && nodes[^1] is TextNode tn && !tn.Text.EndsWith(' '))
                {
                    nodes[^1] = new TextNode(tn.Text + " ");
                }
                continue;
            }

            if (c is '^' or '_' && nodes.Count > 0)
            {
                MathNode baseNode = nodes[^1];
                nodes.RemoveAt(nodes.Count - 1);
                List<MathNode>? sup = null;
                List<MathNode>? sub = null;

                while (pos < s.Length && (s[pos] == '^' || s[pos] == '_'))
                {
                    bool isSup = s[pos] == '^';
                    pos++;
                    var scriptNodes = ParseGroupOrAtom(s, ref pos);
                    if (isSup)
                    {
                        sup = scriptNodes;
                    }
                    else
                    {
                        sub = scriptNodes;
                    }
                }

                nodes.Add(new ScriptNode(baseNode, sup, sub));
                continue;
            }

            if (c == '\\')
            {
                int start = pos++;
                while (pos < s.Length && char.IsLetter(s[pos]))
                {
                    pos++;
                }

                string cmd = s[start..pos];
                if (cmd == "\\frac")
                {
                    var num = ParseGroupOrAtom(s, ref pos);
                    var den = ParseGroupOrAtom(s, ref pos);
                    nodes.Add(new FracNode(num, den));
                }
                else if (cmd == "\\sqrt")
                {
                    var inner = ParseGroupOrAtom(s, ref pos);
                    nodes.Add(new SqrtNode(inner));
                }
                else if (SymbolMap.TryGetValue(cmd, out string? sym))
                {
                    AppendText(nodes, sym);
                }
                else
                {
                    AppendText(nodes, cmd.TrimStart('\\'));
                }

                continue;
            }

            if (c == '{')
            {
                pos++;
                nodes.AddRange(ParseUntil(s, ref pos, '}'));
                continue;
            }

            pos++;
            AppendText(nodes, c == '-' ? "−" : c.ToString());
        }

        return nodes;
    }

    private static List<MathNode> ParseGroupOrAtom(string s, ref int pos)
    {
        while (pos < s.Length && char.IsWhiteSpace(s[pos]))
        {
            pos++;
        }

        if (pos >= s.Length)
        {
            return [new TextNode(" ")];
        }

        if (s[pos] == '{')
        {
            pos++;
            return ParseUntil(s, ref pos, '}');
        }

        if (s[pos] == '\\')
        {
            int start = pos++;
            while (pos < s.Length && char.IsLetter(s[pos]))
            {
                pos++;
            }

            string cmd = s[start..pos];
            return SymbolMap.TryGetValue(cmd, out string? sym)
                ? [new TextNode(sym)]
                : [new TextNode(cmd.TrimStart('\\'))];
        }

        char ch = s[pos++];
        return [new TextNode(ch.ToString())];
    }

    private static void AppendText(List<MathNode> nodes, string text)
    {
        if (nodes.Count > 0 && nodes[^1] is TextNode prev)
        {
            nodes[^1] = new TextNode(prev.Text + text);
        }
        else
        {
            nodes.Add(new TextNode(text));
        }
    }

    private static MeasuredSequence MeasureSequence(List<MathNode> nodes, Typeface typeface, double fontSize, Brush brush)
    {
        var seq = new MeasuredSequence();
        double x = 0;
        double maxAscent = fontSize * 0.78;
        double maxDescent = fontSize * 0.25;

        foreach (var node in nodes)
        {
            MeasuredItem item = MeasureNode(node, typeface, fontSize, brush, x);
            seq.Items.Add(item);
            x += item.Width;
            maxAscent = Math.Max(maxAscent, item.Ascent);
            maxDescent = Math.Max(maxDescent, item.Descent);
        }

        seq.Width = x;
        seq.BaselineY = maxAscent;
        seq.Height = maxAscent + maxDescent;
        return seq;
    }

    private static MeasuredItem MeasureNode(MathNode node, Typeface typeface, double fontSize, Brush brush, double offsetX)
    {
        switch (node)
        {
            case TextNode t:
            {
                var ft = new FormattedText(
                    t.Text,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    brush,
                    1.0);
                double ascent = ft.Baseline;
                double descent = Math.Max(0, ft.Height - ft.Baseline);
                return new MeasuredText(offsetX, ft.WidthIncludingTrailingWhitespace, ascent, descent, ft);
            }

            case FracNode f:
            {
                double subSize = Math.Max(11, fontSize * 0.78);
                var num = MeasureSequence(f.Numerator, typeface, subSize, brush);
                var den = MeasureSequence(f.Denominator, typeface, subSize, brush);
                double w = Math.Max(num.Width, den.Width) + 10;
                double midShift = fontSize * 0.28;
                double ascent = num.Height + 4 + midShift;
                double descent = Math.Max(0, den.Height + 4 - midShift);
                return new MeasuredFrac(offsetX, w, ascent, descent, num, den);
            }

            case SqrtNode sq:
            {
                var inner = MeasureSequence(sq.Inner, typeface, fontSize, brush);
                double radicalW = Math.Max(12, fontSize * 0.52);
                double w = radicalW + inner.Width + 6;
                return new MeasuredSqrt(offsetX, w, inner.BaselineY + 4, inner.Height - inner.BaselineY + 2, inner);
            }

            case ScriptNode sc:
            {
                var baseSeq = MeasureSequence([sc.Base], typeface, fontSize, brush);
                double scriptSize = Math.Max(10, fontSize * 0.64);
                var supSeq = sc.Sup is not null ? MeasureSequence(sc.Sup, typeface, scriptSize, brush) : null;
                var subSeq = sc.Sub is not null ? MeasureSequence(sc.Sub, typeface, scriptSize, brush) : null;
                double scriptW = Math.Max(supSeq?.Width ?? 0, subSeq?.Width ?? 0);

                double ascent = baseSeq.BaselineY;
                if (supSeq is not null)
                {
                    ascent = Math.Max(ascent, baseSeq.BaselineY * 0.55 + supSeq.Height);
                }

                double descent = baseSeq.Height - baseSeq.BaselineY;
                if (subSeq is not null)
                {
                    descent = Math.Max(descent, fontSize * 0.18 + subSeq.Height);
                }

                return new MeasuredScript(offsetX, baseSeq.Width + scriptW + 2, ascent, descent, baseSeq, supSeq, subSeq, fontSize);
            }

            default:
                return new MeasuredText(
                    offsetX,
                    0,
                    fontSize * 0.7,
                    fontSize * 0.2,
                    new FormattedText(string.Empty, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, fontSize, brush, 1.0));
        }
    }

    private static void DrawSequence(DrawingContext dc, MeasuredSequence seq, double originX, double baselineY, Brush brush)
    {
        foreach (var item in seq.Items)
        {
            double x = originX + item.OffsetX;
            switch (item)
            {
                case MeasuredText mt:
                    dc.DrawText(mt.Formatted, new Point(x, baselineY - mt.Ascent));
                    break;

                case MeasuredFrac mf:
                {
                    double barY = baselineY - mf.Num.Height * 0.0 - 6;
                    // Align fraction bar cleanly near the math axis (middle of lowercase x-height)
                    barY = baselineY - mf.Num.BaselineY * 0.35;
                    double numX = x + (mf.Width - mf.Num.Width) / 2.0;
                    double denX = x + (mf.Width - mf.Den.Width) / 2.0;
                    DrawSequence(dc, mf.Num, numX, barY - 3 - (mf.Num.Height - mf.Num.BaselineY), brush);
                    dc.DrawLine(new Pen(brush, 1.5), new Point(x + 2, barY), new Point(x + mf.Width - 2, barY));
                    DrawSequence(dc, mf.Den, denX, barY + 3 + mf.Den.BaselineY, brush);
                    break;
                }

                case MeasuredSqrt ms:
                {
                    double radicalW = Math.Max(12, ms.Inner.BaselineY * 0.65);
                    double topY = baselineY - ms.Inner.BaselineY - 3;
                    double bottomY = baselineY + (ms.Inner.Height - ms.Inner.BaselineY);
                    var pen = new Pen(brush, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                    dc.DrawLine(pen, new Point(x + 2, baselineY - ms.Inner.BaselineY * 0.2), new Point(x + radicalW * 0.45, bottomY - 1));
                    dc.DrawLine(pen, new Point(x + radicalW * 0.45, bottomY - 1), new Point(x + radicalW, topY));
                    dc.DrawLine(pen, new Point(x + radicalW, topY), new Point(x + ms.Width - 2, topY));
                    DrawSequence(dc, ms.Inner, x + radicalW + 2, baselineY, brush);
                    break;
                }

                case MeasuredScript msc:
                {
                    DrawSequence(dc, msc.BaseSeq, x, baselineY, brush);
                    double sx = x + msc.BaseSeq.Width + 1;
                    if (msc.SupSeq is not null)
                    {
                        double supBaseline = baselineY - msc.FontSize * 0.42;
                        DrawSequence(dc, msc.SupSeq, sx, supBaseline, brush);
                    }

                    if (msc.SubSeq is not null)
                    {
                        double subBaseline = baselineY + msc.FontSize * 0.22;
                        DrawSequence(dc, msc.SubSeq, sx, subBaseline, brush);
                    }
                    break;
                }
            }
        }
    }
}

