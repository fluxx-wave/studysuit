using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StudySuit.PdfReader.Services;

/// <summary>
/// Renders LaTeX-style mathematical, physical, and chemical equations into crisp, textbook-styled BitmapSource images.
/// Supports chemical equations, reaction arrows (\xrightarrow[\Delta]{...}), fractions (\frac{a}{b}), roots (\sqrt[n]{x}),
/// superscripts (^), subscripts (_), vectors (\vec), unit vectors (\hat), Greek letters, and standard scientific operators.
/// </summary>
public static partial class MathEquationRenderer
{
    private static readonly Dictionary<string, string> SymbolMap = new(StringComparer.Ordinal)
    {
        // Greek lowercase
        ["\\alpha"] = "α",
        ["\\beta"] = "β",
        ["\\gamma"] = "γ",
        ["\\delta"] = "δ",
        ["\\epsilon"] = "ε",
        ["\\varepsilon"] = "ε",
        ["\\zeta"] = "ζ",
        ["\\eta"] = "η",
        ["\\theta"] = "θ",
        ["\\vartheta"] = "ϑ",
        ["\\iota"] = "ι",
        ["\\kappa"] = "κ",
        ["\\lambda"] = "λ",
        ["\\mu"] = "μ",
        ["\\nu"] = "ν",
        ["\\xi"] = "ξ",
        ["\\pi"] = "π",
        ["\\varpi"] = "ϖ",
        ["\\rho"] = "ρ",
        ["\\varrho"] = "ϱ",
        ["\\sigma"] = "σ",
        ["\\varsigma"] = "ς",
        ["\\tau"] = "τ",
        ["\\upsilon"] = "υ",
        ["\\phi"] = "φ",
        ["\\varphi"] = "ϕ",
        ["\\chi"] = "χ",
        ["\\psi"] = "ψ",
        ["\\omega"] = "ω",

        // Greek uppercase
        ["\\Gamma"] = "Γ",
        ["\\Delta"] = "Δ",
        ["\\Theta"] = "Θ",
        ["\\Lambda"] = "Λ",
        ["\\Xi"] = "Ξ",
        ["\\Pi"] = "Π",
        ["\\Sigma"] = "Σ",
        ["\\Upsilon"] = "Υ",
        ["\\Phi"] = "Φ",
        ["\\Psi"] = "Ψ",
        ["\\Omega"] = "Ω",

        // Science units & special symbols
        ["\\degree"] = "°",
        ["\\circ"] = "°",
        ["\\angstrom"] = "Å",
        ["\\AA"] = "Å",
        ["\\hbar"] = "ℏ",
        ["\\ell"] = "ℓ",
        ["\\mho"] = "℧",
        ["\\micro"] = "µ",
        ["\\celsius"] = "°C",

        // Chemistry & physics arrows
        ["\\rightarrow"] = "→",
        ["\\to"] = "→",
        ["\\leftarrow"] = "←",
        ["\\gets"] = "←",
        ["\\leftrightarrow"] = "↔",
        ["\\longrightarrow"] = "⟶",
        ["\\longleftarrow"] = "⟵",
        ["\\longleftrightarrow"] = "⟷",
        ["\\Rightarrow"] = "⇒",
        ["\\implies"] = "⇒",
        ["\\Leftarrow"] = "⇐",
        ["\\Leftrightarrow"] = "⇔",
        ["\\iff"] = "⇔",
        ["\\rightleftharpoons"] = "⇌",
        ["\\rightharpoonup"] = "⇀",
        ["\\leftharpoondown"] = "↽",
        ["\\uparrow"] = "↑",
        ["\\downarrow"] = "↓",
        ["\\updownarrow"] = "↕",
        ["\\nearrow"] = "↗",
        ["\\searrow"] = "↘",
        ["\\nwarrow"] = "↖",
        ["\\swarrow"] = "↙",
        ["\\mapsto"] = "↦",

        // Arithmetic & algebra operators
        ["\\pm"] = "±",
        ["\\mp"] = "∓",
        ["\\times"] = "×",
        ["\\cdot"] = "·",
        ["\\div"] = "÷",
        ["\\ast"] = "∗",
        ["\\star"] = "⋆",
        ["\\bullet"] = "•",
        ["\\oplus"] = "⊕",
        ["\\otimes"] = "⊗",
        ["\\odot"] = "⊙",

        // Relations
        ["\\leq"] = "≤",
        ["\\le"] = "≤",
        ["\\geq"] = "≥",
        ["\\ge"] = "≥",
        ["\\neq"] = "≠",
        ["\\ne"] = "≠",
        ["\\approx"] = "≈",
        ["\\equiv"] = "≡",
        ["\\sim"] = "∼",
        ["\\simeq"] = "≃",
        ["\\cong"] = "≅",
        ["\\propto"] = "∝",
        ["\\ll"] = "≪",
        ["\\gg"] = "≫",
        ["\\parallel"] = "∥",
        ["\\perp"] = "⊥",
        ["\\angle"] = "∠",
        ["\\triangle"] = "△",

        // Calculus & analysis
        ["\\sum"] = "∑",
        ["\\prod"] = "∏",
        ["\\coprod"] = "∐",
        ["\\int"] = "∫",
        ["\\iint"] = "∬",
        ["\\iiint"] = "∭",
        ["\\oint"] = "∮",
        ["\\partial"] = "∂",
        ["\\nabla"] = "∇",
        ["\\infty"] = "∞",

        // Set theory & logic
        ["\\forall"] = "∀",
        ["\\exists"] = "∃",
        ["\\nexists"] = "∄",
        ["\\in"] = "∈",
        ["\\notin"] = "∉",
        ["\\ni"] = "∋",
        ["\\subset"] = "⊂",
        ["\\supset"] = "⊃",
        ["\\subseteq"] = "⊆",
        ["\\supseteq"] = "⊇",
        ["\\cup"] = "∪",
        ["\\cap"] = "∩",
        ["\\setminus"] = "∖",
        ["\\emptyset"] = "∅",
        ["\\varnothing"] = "∅",
        ["\\land"] = "∧",
        ["\\lor"] = "∨",
        ["\\neg"] = "¬",

        // Ellipses
        ["\\cdots"] = "⋯",
        ["\\ldots"] = "…",
        ["\\vdots"] = "⋮",
        ["\\ddots"] = "⋱",

        // Spacing macros
        ["\\quad"] = "    ",
        ["\\qquad"] = "        ",

        // Functions
        ["\\sin"] = "sin",
        ["\\cos"] = "cos",
        ["\\tan"] = "tan",
        ["\\cot"] = "cot",
        ["\\sec"] = "sec",
        ["\\csc"] = "csc",
        ["\\arcsin"] = "arcsin",
        ["\\arccos"] = "arccos",
        ["\\arctan"] = "arctan",
        ["\\sinh"] = "sinh",
        ["\\cosh"] = "cosh",
        ["\\tanh"] = "tanh",
        ["\\log"] = "log",
        ["\\ln"] = "ln",
        ["\\exp"] = "exp",
        ["\\lim"] = "lim",
        ["\\max"] = "max",
        ["\\min"] = "min",
        ["\\det"] = "det",
        ["\\gcd"] = "gcd",
        ["\\deg"] = "deg"
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
        var lines = ParseLines(source);
        var measuredLines = lines.Select(l => MeasureSequence(l, typeface, fontSize, textBrush)).ToList();

        const double padX = 18;
        const double padY = 12;
        const double lineSpacing = 8;

        double maxLineWidth = measuredLines.Count > 0 ? measuredLines.Max(l => l.Width) : 0;
        double totalHeight = 0;
        for (int i = 0; i < measuredLines.Count; i++)
        {
            totalHeight += measuredLines[i].Height;
            if (i > 0) totalHeight += lineSpacing;
        }

        double width = Math.Max(48, Math.Ceiling(maxLineWidth + padX * 2));
        double height = Math.Max(38, Math.Ceiling(totalHeight + padY * 2));

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(bgBrush, borderPen, new Rect(0.5, 0.5, width - 1, height - 1), 8, 8);

            double currentY = padY;
            foreach (var line in measuredLines)
            {
                double drawY = currentY + line.BaselineY;
                DrawSequence(dc, line, padX, drawY, textBrush);
                currentY += line.Height + lineSpacing;
            }
        }

        const double scale = 2.0; // Render at 192 DPI for razor-sharp display
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
    private sealed record SqrtNode(List<MathNode> Inner, List<MathNode>? Degree = null) : MathNode;
    private sealed record ScriptNode(MathNode Base, List<MathNode>? Sup, List<MathNode>? Sub) : MathNode;
    private sealed record ArrowNode(List<MathNode>? Above, List<MathNode>? Below, ArrowType Type = ArrowType.Right) : MathNode;
    private sealed record BinomNode(List<MathNode> Top, List<MathNode> Bottom) : MathNode;
    private sealed record LineBreakNode : MathNode;

    private enum ArrowType
    {
        Right,
        Left,
        Bidirectional,
        Equilibrium
    }

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
    private sealed record MeasuredSqrt(double OffsetX, double Width, double Ascent, double Descent, MeasuredSequence Inner, MeasuredSequence? DegSeq)
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
    private sealed record MeasuredArrow(
        double OffsetX,
        double Width,
        double Ascent,
        double Descent,
        MeasuredSequence? AboveSeq,
        MeasuredSequence? BelowSeq,
        ArrowType Type,
        double FontSize)
        : MeasuredItem(OffsetX, Width, Ascent, Descent);
    private sealed record MeasuredBinom(
        double OffsetX,
        double Width,
        double Ascent,
        double Descent,
        MeasuredSequence TopSeq,
        MeasuredSequence BottomSeq,
        double FontSize)
        : MeasuredItem(OffsetX, Width, Ascent, Descent);

    private static string PreprocessLatex(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        string s = input.Trim();

        // 1. Strip display math delimiters if present: \[ ... \], $$ ... $$, $ ... $
        if (s.StartsWith("$$", StringComparison.Ordinal) && s.EndsWith("$$", StringComparison.Ordinal) && s.Length >= 4)
        {
            s = s[2..^2].Trim();
        }
        else if (s.StartsWith(@"\[", StringComparison.Ordinal) && s.EndsWith(@"\]", StringComparison.Ordinal) && s.Length >= 4)
        {
            s = s[2..^2].Trim();
        }
        else if (s.StartsWith('$') && s.EndsWith('$') && s.Length >= 2 && !s.StartsWith("$$", StringComparison.Ordinal))
        {
            s = s[1..^1].Trim();
        }

        // 2. Strip environment wrappers: \begin{aligned} ... \end{aligned}, \begin{equation} ... \end{equation}, etc.
        s = Regex.Replace(s, @"\\begin\{(?:aligned|align\*?|equation\*?|gather\*?|split|matrix)\}", string.Empty);
        s = Regex.Replace(s, @"\\end\{(?:aligned|align\*?|equation\*?|gather\*?|split|matrix)\}", string.Empty);

        // 3. Handle \ce{...} (mhchem syntax)
        s = PreprocessChemicalFormulas(s);

        return s.Trim();
    }

    private static string PreprocessChemicalFormulas(string input)
    {
        if (!input.Contains(@"\ce{", StringComparison.Ordinal))
        {
            return input;
        }

        return Regex.Replace(input, @"\\ce\{([^}]+)\}", match =>
        {
            string body = match.Groups[1].Value.Trim();

            // Convert reaction arrows with conditions: ->[condition] to \xrightarrow{condition}
            body = Regex.Replace(body, @"->\[([^\]]*)\]", @"\xrightarrow{$1}");
            // Convert simple reaction arrow -> to \rightarrow
            body = Regex.Replace(body, @"->", @"\rightarrow ");
            // Convert equilibrium <=> to \rightleftharpoons
            body = Regex.Replace(body, @"<=>", @"\rightleftharpoons ");
            // Convert left arrow <- to \leftarrow
            body = Regex.Replace(body, @"<-", @"\leftarrow ");

            // Convert gas ^ and precipitate v
            body = Regex.Replace(body, @"\s+\^\s*", @" \uparrow ");
            body = Regex.Replace(body, @"\s+v\s*", @" \downarrow ");

            // Convert chemical formula subscripts: e.g. H2O -> H_{2}O, Fe2O3 -> Fe_{2}O_{3}, SO4 -> SO_{4}
            body = Regex.Replace(body, @"([A-Z][a-z]?)([0-9]+)", @"$1_{$2}");

            // Convert state indicators (s), (l), (g), (aq)
            body = Regex.Replace(body, @"\((s|l|g|aq)\)", @"\text{($1)}");

            return body;
        });
    }

    private static List<List<MathNode>> ParseLines(string source)
    {
        string preprocessed = PreprocessLatex(source);
        var allNodes = ParseSequence(preprocessed);

        var lines = new List<List<MathNode>>();
        var currentLine = new List<MathNode>();

        foreach (var node in allNodes)
        {
            if (node is LineBreakNode)
            {
                if (currentLine.Count > 0)
                {
                    lines.Add(currentLine);
                    currentLine = [];
                }
            }
            else
            {
                currentLine.Add(node);
            }
        }

        if (currentLine.Count > 0 || lines.Count == 0)
        {
            lines.Add(currentLine);
        }

        return lines;
    }

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
                if (nodes.Count > 0 && nodes[^1] is TextNode tn)
                {
                    if (!tn.Text.EndsWith(' '))
                    {
                        nodes[^1] = new TextNode(tn.Text + " ");
                    }
                }
                else
                {
                    nodes.Add(new TextNode(" "));
                }
                continue;
            }

            if (c is '^' or '_')
            {
                if (nodes.Count > 0 && nodes[^1] is ScriptNode existingScript)
                {
                    bool isSup = c == '^';
                    pos++;
                    var scriptNodes = ParseGroupOrAtom(s, ref pos);
                    if (isSup && existingScript.Sup is null)
                    {
                        nodes[^1] = existingScript with { Sup = scriptNodes };
                        continue;
                    }
                    if (!isSup && existingScript.Sub is null)
                    {
                        nodes[^1] = existingScript with { Sub = scriptNodes };
                        continue;
                    }
                    nodes[^1] = isSup
                        ? new ScriptNode(existingScript, scriptNodes, null)
                        : new ScriptNode(existingScript, null, scriptNodes);
                    continue;
                }

                MathNode baseNode = nodes.Count > 0 ? nodes[^1] : new TextNode(string.Empty);
                if (nodes.Count > 0)
                {
                    nodes.RemoveAt(nodes.Count - 1);
                }

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
                int cmdStart = pos++;
                if (pos < s.Length && !char.IsLetter(s[pos]))
                {
                    char esc = s[pos++];
                    if (esc is '{' or '}' or '%' or '&' or '_' or '#')
                    {
                        AppendText(nodes, esc.ToString());
                        continue;
                    }
                    if (esc is ',' or ';' or ':' or ' ')
                    {
                        AppendText(nodes, " ");
                        continue;
                    }
                    if (esc == '!')
                    {
                        continue; // skip negative thin space
                    }
                    if (esc == '\\')
                    {
                        nodes.Add(new LineBreakNode());
                        continue;
                    }
                    AppendText(nodes, esc.ToString());
                    continue;
                }

                while (pos < s.Length && char.IsLetter(s[pos]))
                {
                    pos++;
                }

                string cmd = s[cmdStart..pos];

                if (cmd is "\\text" or "\\mathrm" or "\\mathbf" or "\\mathit" or "\\textbf" or "\\textit"
                    or "\\textnormal" or "\\operatorname" or "\\sf" or "\\rm" or "\\bf" or "\\it"
                    or "\\mathbb" or "\\mathcal" or "\\mathfrak" or "\\mathsf" or "\\mathtt")
                {
                    var content = ParseGroupOrAtom(s, ref pos);
                    nodes.AddRange(content);
                    continue;
                }

                if (cmd is "\\frac" or "\\dfrac" or "\\tfrac")
                {
                    var num = ParseGroupOrAtom(s, ref pos);
                    var den = ParseGroupOrAtom(s, ref pos);
                    nodes.Add(new FracNode(num, den));
                    continue;
                }

                if (cmd == "\\sqrt")
                {
                    List<MathNode>? deg = null;
                    while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
                    if (pos < s.Length && s[pos] == '[')
                    {
                        pos++;
                        deg = ParseUntil(s, ref pos, ']');
                        while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
                    }
                    var inner = ParseGroupOrAtom(s, ref pos);
                    nodes.Add(new SqrtNode(inner, deg));
                    continue;
                }

                if (cmd == "\\binom")
                {
                    var top = ParseGroupOrAtom(s, ref pos);
                    var bot = ParseGroupOrAtom(s, ref pos);
                    nodes.Add(new BinomNode(top, bot));
                    continue;
                }

                if (cmd is "\\xrightarrow" or "\\xleftarrow" or "\\xrightleftharpoons" or "\\xleftrightharpoons")
                {
                    while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
                    List<MathNode>? below = null;
                    if (pos < s.Length && s[pos] == '[')
                    {
                        pos++;
                        below = ParseUntil(s, ref pos, ']');
                        while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
                    }

                    List<MathNode>? above = null;
                    if (pos < s.Length && s[pos] != '}')
                    {
                        above = ParseGroupOrAtom(s, ref pos);
                    }

                    var arrowType = cmd switch
                    {
                        "\\xleftarrow" => ArrowType.Left,
                        "\\xrightleftharpoons" or "\\xleftrightharpoons" => ArrowType.Equilibrium,
                        _ => ArrowType.Right
                    };
                    nodes.Add(new ArrowNode(above, below, arrowType));
                    continue;
                }

                string? accentChar = cmd switch
                {
                    "\\vec" => "\u20D7",
                    "\\hat" => "\u0302",
                    "\\bar" or "\\overline" => "\u0304",
                    "\\dot" => "\u0307",
                    "\\ddot" => "\u0308",
                    "\\tilde" => "\u0303",
                    _ => null
                };
                if (accentChar is not null)
                {
                    var target = ParseGroupOrAtom(s, ref pos);
                    foreach (var node in target)
                    {
                        if (node is TextNode tn)
                        {
                            var sb = new StringBuilder();
                            foreach (char ch in tn.Text)
                            {
                                sb.Append(ch);
                                if (char.IsLetterOrDigit(ch))
                                {
                                    sb.Append(accentChar);
                                }
                            }
                            nodes.Add(new TextNode(sb.ToString()));
                        }
                        else
                        {
                            nodes.Add(node);
                        }
                    }
                    continue;
                }

                if (cmd is "\\left" or "\\right")
                {
                    while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
                    if (pos < s.Length)
                    {
                        if (s[pos] == '.')
                        {
                            pos++;
                        }
                        else if (s[pos] == '\\' && pos + 1 < s.Length)
                        {
                            pos++;
                            char esc = s[pos++];
                            if (esc is '{' or '}' or '|')
                            {
                                AppendText(nodes, esc.ToString());
                            }
                        }
                        else
                        {
                            AppendText(nodes, s[pos++].ToString());
                        }
                    }
                    continue;
                }

                if (cmd is "\\begin" or "\\end" or "\\label" or "\\tag" or "\\nonumber")
                {
                    while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
                    if (pos < s.Length && s[pos] == '{')
                    {
                        pos++;
                        while (pos < s.Length && s[pos] != '}') pos++;
                        if (pos < s.Length && s[pos] == '}') pos++;
                    }
                    continue;
                }

                if (cmd is "\\limits" or "\\nolimits" or "\\displaystyle" or "\\textstyle"
                    or "\\small" or "\\large" or "\\normalsize")
                {
                    continue;
                }

                if (SymbolMap.TryGetValue(cmd, out string? sym))
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

            if (c == '&')
            {
                pos++;
                AppendText(nodes, " ");
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
            return [new TextNode(string.Empty)];
        }

        if (s[pos] == '{')
        {
            pos++;
            return ParseUntil(s, ref pos, '}');
        }

        if (s[pos] == '\\')
        {
            int cmdStart = pos++;
            if (pos < s.Length && !char.IsLetter(s[pos]))
            {
                char esc = s[pos++];
                if (esc is '{' or '}' or '%' or '&' or '_' or '#')
                {
                    return [new TextNode(esc.ToString())];
                }
                if (esc is ',' or ';' or ':' or ' ')
                {
                    return [new TextNode(" ")];
                }
                if (esc == '!')
                {
                    return [];
                }
                if (esc == '\\')
                {
                    return [new LineBreakNode()];
                }
                return [new TextNode(esc.ToString())];
            }

            while (pos < s.Length && char.IsLetter(s[pos]))
            {
                pos++;
            }

            string cmd = s[cmdStart..pos];

            if (cmd is "\\text" or "\\mathrm" or "\\mathbf" or "\\mathit" or "\\textbf" or "\\textit"
                or "\\textnormal" or "\\operatorname" or "\\sf" or "\\rm" or "\\bf" or "\\it"
                or "\\mathbb" or "\\mathcal" or "\\mathfrak" or "\\mathsf" or "\\mathtt")
            {
                return ParseGroupOrAtom(s, ref pos);
            }

            if (cmd is "\\frac" or "\\dfrac" or "\\tfrac")
            {
                var num = ParseGroupOrAtom(s, ref pos);
                var den = ParseGroupOrAtom(s, ref pos);
                return [new FracNode(num, den)];
            }

            if (cmd == "\\sqrt")
            {
                List<MathNode>? deg = null;
                while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
                if (pos < s.Length && s[pos] == '[')
                {
                    pos++;
                    deg = ParseUntil(s, ref pos, ']');
                    while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
                }
                var inner = ParseGroupOrAtom(s, ref pos);
                return [new SqrtNode(inner, deg)];
            }

            if (cmd == "\\binom")
            {
                var top = ParseGroupOrAtom(s, ref pos);
                var bot = ParseGroupOrAtom(s, ref pos);
                return [new BinomNode(top, bot)];
            }

            if (cmd is "\\xrightarrow" or "\\xleftarrow" or "\\xrightleftharpoons" or "\\xleftrightharpoons")
            {
                while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
                List<MathNode>? below = null;
                if (pos < s.Length && s[pos] == '[')
                {
                    pos++;
                    below = ParseUntil(s, ref pos, ']');
                    while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
                }

                List<MathNode>? above = null;
                if (pos < s.Length && s[pos] != '}')
                {
                    above = ParseGroupOrAtom(s, ref pos);
                }

                var arrowType = cmd switch
                {
                    "\\xleftarrow" => ArrowType.Left,
                    "\\xrightleftharpoons" or "\\xleftrightharpoons" => ArrowType.Equilibrium,
                    _ => ArrowType.Right
                };
                return [new ArrowNode(above, below, arrowType)];
            }

            string? accentChar = cmd switch
            {
                "\\vec" => "\u20D7",
                "\\hat" => "\u0302",
                "\\bar" or "\\overline" => "\u0304",
                "\\dot" => "\u0307",
                "\\ddot" => "\u0308",
                "\\tilde" => "\u0303",
                _ => null
            };
            if (accentChar is not null)
            {
                var target = ParseGroupOrAtom(s, ref pos);
                var accented = new List<MathNode>();
                foreach (var node in target)
                {
                    if (node is TextNode tn)
                    {
                        var sb = new StringBuilder();
                        foreach (char c in tn.Text)
                        {
                            sb.Append(c);
                            if (char.IsLetterOrDigit(c))
                            {
                                sb.Append(accentChar);
                            }
                        }
                        accented.Add(new TextNode(sb.ToString()));
                    }
                    else
                    {
                        accented.Add(node);
                    }
                }
                return accented;
            }

            if (SymbolMap.TryGetValue(cmd, out string? sym))
            {
                return [new TextNode(sym)];
            }

            if (cmd is "\\limits" or "\\nolimits" or "\\displaystyle" or "\\textstyle"
                or "\\small" or "\\large" or "\\normalsize" or "\\left" or "\\right")
            {
                return [];
            }

            return [new TextNode(cmd.TrimStart('\\'))];
        }

        char ch = s[pos++];
        return [new TextNode(ch == '-' ? "−" : ch.ToString())];
    }

    private static void AppendText(List<MathNode> nodes, string text)
    {
        if (string.IsNullOrEmpty(text)) return;

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
                double degreeW = 0;
                MeasuredSequence? degSeq = null;
                if (sq.Degree is { Count: > 0 })
                {
                    degSeq = MeasureSequence(sq.Degree, typeface, Math.Max(9, fontSize * 0.52), brush);
                    degreeW = degSeq.Width * 0.6;
                }
                double radicalW = Math.Max(12, fontSize * 0.52);
                double w = degreeW + radicalW + inner.Width + 6;
                return new MeasuredSqrt(offsetX, w, inner.BaselineY + 5, inner.Height - inner.BaselineY + 2, inner, degSeq);
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

            case ArrowNode a:
            {
                double labelFontSize = Math.Max(10, fontSize * 0.65);
                MeasuredSequence? aboveSeq = a.Above is { Count: > 0 }
                    ? MeasureSequence(a.Above, typeface, labelFontSize, brush)
                    : null;
                MeasuredSequence? belowSeq = a.Below is { Count: > 0 }
                    ? MeasureSequence(a.Below, typeface, labelFontSize, brush)
                    : null;

                double labelWidth = Math.Max(aboveSeq?.Width ?? 0, belowSeq?.Width ?? 0);
                double minArrowWidth = Math.Max(38, fontSize * 1.75);
                double arrowWidth = Math.Max(minArrowWidth, labelWidth + 18);

                double mathAxis = fontSize * 0.28;
                double aboveHeight = aboveSeq?.Height ?? 0;
                double belowHeight = belowSeq?.Height ?? 0;

                double ascent = mathAxis + (aboveHeight > 0 ? aboveHeight + 3 : fontSize * 0.25);
                double descent = (belowHeight > 0 ? belowHeight + 3 - mathAxis : fontSize * 0.2);

                return new MeasuredArrow(offsetX, arrowWidth, ascent, descent, aboveSeq, belowSeq, a.Type, fontSize);
            }

            case BinomNode bn:
            {
                double subSize = Math.Max(11, fontSize * 0.78);
                var top = MeasureSequence(bn.Top, typeface, subSize, brush);
                var bot = MeasureSequence(bn.Bottom, typeface, subSize, brush);
                double contentW = Math.Max(top.Width, bot.Width);
                double parenW = fontSize * 0.35;
                double totalW = contentW + parenW * 2 + 6;
                double midShift = fontSize * 0.28;
                double ascent = top.Height + 3 + midShift;
                double descent = Math.Max(0, bot.Height + 3 - midShift);
                return new MeasuredBinom(offsetX, totalW, ascent, descent, top, bot, fontSize);
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
                    double barY = baselineY - mf.Num.BaselineY * 0.35;
                    double numX = x + (mf.Width - mf.Num.Width) / 2.0;
                    double denX = x + (mf.Width - mf.Den.Width) / 2.0;
                    DrawSequence(dc, mf.Num, numX, barY - 3 - (mf.Num.Height - mf.Num.BaselineY), brush);
                    var fracPen = new Pen(brush, 1.5);
                    fracPen.Freeze();
                    dc.DrawLine(fracPen, new Point(x + 2, barY), new Point(x + mf.Width - 2, barY));
                    DrawSequence(dc, mf.Den, denX, barY + 3 + mf.Den.BaselineY, brush);
                    break;
                }

                case MeasuredSqrt ms:
                {
                    double degW = ms.DegSeq?.Width ?? 0;
                    double radicalW = Math.Max(12, ms.Inner.BaselineY * 0.65);
                    double topY = baselineY - ms.Inner.BaselineY - 3;
                    double bottomY = baselineY + (ms.Inner.Height - ms.Inner.BaselineY);
                    var pen = new Pen(brush, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                    pen.Freeze();

                    if (ms.DegSeq is not null)
                    {
                        double degX = x;
                        double degY = topY + ms.DegSeq.BaselineY + 2;
                        DrawSequence(dc, ms.DegSeq, degX, degY, brush);
                    }

                    double radX = x + (degW > 0 ? degW * 0.6 : 0);
                    dc.DrawLine(pen, new Point(radX + 2, baselineY - ms.Inner.BaselineY * 0.2), new Point(radX + radicalW * 0.45, bottomY - 1));
                    dc.DrawLine(pen, new Point(radX + radicalW * 0.45, bottomY - 1), new Point(radX + radicalW, topY));
                    dc.DrawLine(pen, new Point(radX + radicalW, topY), new Point(x + ms.Width - 2, topY));
                    DrawSequence(dc, ms.Inner, radX + radicalW + 2, baselineY, brush);
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

                case MeasuredArrow ma:
                {
                    double axisY = baselineY - ma.FontSize * 0.28;
                    double shaftStart = x + 3;
                    double shaftEnd = x + ma.Width - 3;
                    double penThickness = Math.Max(1.5, ma.FontSize * 0.075);
                    var arrowPen = new Pen(brush, penThickness)
                    {
                        StartLineCap = PenLineCap.Round,
                        EndLineCap = PenLineCap.Round
                    };
                    arrowPen.Freeze();

                    if (ma.Type == ArrowType.Equilibrium)
                    {
                        double gap = ma.FontSize * 0.14;
                        double topY = axisY - gap;
                        double botY = axisY + gap;
                        double barbLen = Math.Max(6, ma.FontSize * 0.26);
                        double barbH = Math.Max(3.5, ma.FontSize * 0.16);

                        dc.DrawLine(arrowPen, new Point(shaftStart, topY), new Point(shaftEnd, topY));
                        dc.DrawLine(arrowPen, new Point(shaftEnd, topY), new Point(shaftEnd - barbLen, topY - barbH));

                        dc.DrawLine(arrowPen, new Point(shaftStart, botY), new Point(shaftEnd, botY));
                        dc.DrawLine(arrowPen, new Point(shaftStart, botY), new Point(shaftStart + barbLen, botY + barbH));
                    }
                    else
                    {
                        dc.DrawLine(arrowPen, new Point(shaftStart, axisY), new Point(shaftEnd, axisY));
                        double headLen = Math.Max(6.5, ma.FontSize * 0.28);
                        double headH = Math.Max(4, ma.FontSize * 0.18);

                        if (ma.Type is ArrowType.Right or ArrowType.Bidirectional)
                        {
                            dc.DrawLine(arrowPen, new Point(shaftEnd, axisY), new Point(shaftEnd - headLen, axisY - headH));
                            dc.DrawLine(arrowPen, new Point(shaftEnd, axisY), new Point(shaftEnd - headLen, axisY + headH));
                        }

                        if (ma.Type is ArrowType.Left or ArrowType.Bidirectional)
                        {
                            dc.DrawLine(arrowPen, new Point(shaftStart, axisY), new Point(shaftStart + headLen, axisY - headH));
                            dc.DrawLine(arrowPen, new Point(shaftStart, axisY), new Point(shaftStart + headLen, axisY + headH));
                        }
                    }

                    if (ma.AboveSeq is not null)
                    {
                        double aboveX = x + (ma.Width - ma.AboveSeq.Width) / 2.0;
                        double aboveBaseline = axisY - 3 - (ma.AboveSeq.Height - ma.AboveSeq.BaselineY);
                        DrawSequence(dc, ma.AboveSeq, aboveX, aboveBaseline, brush);
                    }

                    if (ma.BelowSeq is not null)
                    {
                        double belowX = x + (ma.Width - ma.BelowSeq.Width) / 2.0;
                        double belowBaseline = axisY + 4 + ma.BelowSeq.BaselineY;
                        DrawSequence(dc, ma.BelowSeq, belowX, belowBaseline, brush);
                    }
                    break;
                }

                case MeasuredBinom mb:
                {
                    double axisY = baselineY - mb.FontSize * 0.28;
                    double contentW = Math.Max(mb.TopSeq.Width, mb.BottomSeq.Width);
                    double parenW = mb.FontSize * 0.35;
                    double topX = x + parenW + 3 + (contentW - mb.TopSeq.Width) / 2.0;
                    double botX = x + parenW + 3 + (contentW - mb.BottomSeq.Width) / 2.0;

                    DrawSequence(dc, mb.TopSeq, topX, axisY - 2 - (mb.TopSeq.Height - mb.TopSeq.BaselineY), brush);
                    DrawSequence(dc, mb.BottomSeq, botX, axisY + 4 + mb.BottomSeq.BaselineY, brush);

                    var parenPen = new Pen(brush, Math.Max(1.4, mb.FontSize * 0.07));
                    parenPen.Freeze();
                    double topY = baselineY - mb.Ascent + 2;
                    double botY = baselineY + mb.Descent - 2;

                    var leftGeo = new PathGeometry();
                    var leftFig = new PathFigure { StartPoint = new Point(x + parenW, topY) };
                    leftFig.Segments.Add(new BezierSegment(
                        new Point(x + 2, axisY - (axisY - topY) * 0.5),
                        new Point(x + 2, axisY + (botY - axisY) * 0.5),
                        new Point(x + parenW, botY),
                        true));
                    leftGeo.Figures.Add(leftFig);
                    leftGeo.Freeze();
                    dc.DrawGeometry(null, parenPen, leftGeo);

                    double rightX = x + mb.Width - parenW;
                    var rightGeo = new PathGeometry();
                    var rightFig = new PathFigure { StartPoint = new Point(rightX, topY) };
                    rightFig.Segments.Add(new BezierSegment(
                        new Point(x + mb.Width - 2, axisY - (axisY - topY) * 0.5),
                        new Point(x + mb.Width - 2, axisY + (botY - axisY) * 0.5),
                        new Point(rightX, botY),
                        true));
                    rightGeo.Figures.Add(rightFig);
                    rightGeo.Freeze();
                    dc.DrawGeometry(null, parenPen, rightGeo);
                    break;
                }
            }
        }
    }
}
