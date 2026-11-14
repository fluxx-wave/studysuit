using System.Globalization;

namespace StudySuit.PdfReader.Services;

/// <summary>
/// Safe recursive-descent evaluator for single-variable mathematical expressions f(x) used in Function Graph plotting.
/// </summary>
public static class MathExpressionEvaluator
{
    public static double Evaluate(string expression, double x)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return double.NaN;
        }

        var parser = new Parser(expression, x);
        double value = parser.ParseExpression();
        parser.SkipWhitespace();
        if (!parser.IsAtEnd)
        {
            throw new FormatException($"Unexpected token near '{parser.Remaining}'");
        }

        return value;
    }

    private ref struct Parser
    {
        private readonly string _text;
        private readonly double _x;
        private int _pos;

        public Parser(string text, double x)
        {
            _text = text;
            _x = x;
            _pos = 0;
        }

        public bool IsAtEnd => _pos >= _text.Length;
        public string Remaining => _pos < _text.Length ? _text[_pos..] : string.Empty;

        public void SkipWhitespace()
        {
            while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
            {
                _pos++;
            }
        }

        public double ParseExpression()
        {
            double left = ParseTerm();
            while (true)
            {
                SkipWhitespace();
                if (Match('+'))
                {
                    left += ParseTerm();
                }
                else if (Match('-'))
                {
                    left -= ParseTerm();
                }
                else
                {
                    break;
                }
            }

            return left;
        }

        private double ParseTerm()
        {
            double left = ParsePower();
            while (true)
            {
                SkipWhitespace();
                if (Match('*'))
                {
                    left *= ParsePower();
                }
                else if (Match('/'))
                {
                    double right = ParsePower();
                    left = Math.Abs(right) < 1e-15 ? double.NaN : left / right;
                }
                else
                {
                    break;
                }
            }

            return left;
        }

        private double ParsePower()
        {
            double baseVal = ParseUnary();
            SkipWhitespace();
            if (Match('^'))
            {
                double exponent = ParsePower();
                return Math.Pow(baseVal, exponent);
            }

            return baseVal;
        }

        private double ParseUnary()
        {
            SkipWhitespace();
            if (Match('+'))
            {
                return ParseUnary();
            }

            if (Match('-'))
            {
                return -ParseUnary();
            }

            return ParsePrimary();
        }

        private double ParsePrimary()
        {
            SkipWhitespace();
            if (Match('('))
            {
                double inner = ParseExpression();
                SkipWhitespace();
                if (!Match(')'))
                {
                    throw new FormatException("Missing closing parenthesis ')'.");
                }

                return inner;
            }

            if (_pos < _text.Length && (char.IsDigit(_text[_pos]) || _text[_pos] == '.'))
            {
                int start = _pos;
                while (_pos < _text.Length && (char.IsDigit(_text[_pos]) || _text[_pos] == '.'))
                {
                    _pos++;
                }

                string numStr = _text[start.._pos];
                if (!double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                {
                    throw new FormatException($"Invalid number '{numStr}'.");
                }

                // Support implicit multiplication like 2x or 3sin(x)
                SkipWhitespace();
                if (_pos < _text.Length && (char.IsLetter(_text[_pos]) || _text[_pos] == '('))
                {
                    return number * ParsePower();
                }

                return number;
            }

            if (_pos < _text.Length && char.IsLetter(_text[_pos]))
            {
                int start = _pos;
                while (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] == '_'))
                {
                    _pos++;
                }

                string ident = _text[start.._pos].ToLowerInvariant();
                SkipWhitespace();

                if (Match('('))
                {
                    double arg = ParseExpression();
                    SkipWhitespace();
                    if (!Match(')'))
                    {
                        throw new FormatException($"Missing ')' after function '{ident}'.");
                    }

                    return ident switch
                    {
                        "sin" => Math.Sin(arg),
                        "cos" => Math.Cos(arg),
                        "tan" => Math.Tan(arg),
                        "asin" => Math.Asin(arg),
                        "acos" => Math.Acos(arg),
                        "atan" => Math.Atan(arg),
                        "sqrt" => arg < 0 ? double.NaN : Math.Sqrt(arg),
                        "abs" => Math.Abs(arg),
                        "ln" => arg <= 0 ? double.NaN : Math.Log(arg),
                        "log" => arg <= 0 ? double.NaN : Math.Log10(arg),
                        "exp" => Math.Exp(arg),
                        "floor" => Math.Floor(arg),
                        "ceil" => Math.Ceiling(arg),
                        _ => throw new FormatException($"Unknown function '{ident}'.")
                    };
                }

                return ident switch
                {
                    "x" => _x,
                    "pi" => Math.PI,
                    "e" => Math.E,
                    _ => throw new FormatException($"Unknown identifier '{ident}'.")
                };
            }

            throw new FormatException($"Unexpected character at position {_pos}.");
        }

        private bool Match(char c)
        {
            if (_pos < _text.Length && _text[_pos] == c)
            {
                _pos++;
                return true;
            }

            return false;
        }
    }
}

