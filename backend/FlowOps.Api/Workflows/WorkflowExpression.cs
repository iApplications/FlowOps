using System.Globalization;

namespace FlowOps.Api.Workflows;

// Syntax checker for Condition expressions. FlowOps never executes user code (PROJECT_RULES §14): expressions
// are parsed against this small grammar and nothing else is accepted. Phase 3 validates syntax only;
// evaluation and variable typing arrive with conditional routing in Phase 6.
//
//   expression := or
//   or         := and ( "||" and )*
//   and        := unary ( "&&" unary )*
//   unary      := "!" unary | comparison
//   comparison := operand ( ( "==" | "!=" | "<" | "<=" | ">" | ">=" ) operand )?
//   operand    := number | "-" number | string | "true" | "false" | path | "(" expression ")"
//   path       := identifier ( "." identifier )*        identifier := [A-Za-z_][A-Za-z0-9_]*
//
// Function calls, indexing, assignment, statements and any other syntax are rejected.
public static class WorkflowExpression
{
    public const int MaxTokens = 200;
    public const int MaxDepth = 32;
    private static readonly string[] Comparisons = ["==", "!=", "<=", ">=", "<", ">"];

    private enum Kind { Number, String, Identifier, Symbol, End }
    private readonly record struct Token(Kind Kind, string Text, int Position);

    /// <summary>Returns null when the expression is valid, otherwise a message naming the problem and its position.</summary>
    public static string? Validate(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return "Enter an expression.";
        var (tokens, error) = Tokenize(expression);
        if (error is not null) return error;
        if (tokens.Count > MaxTokens) return $"Expressions can have at most {MaxTokens} parts.";
        var parser = new Parser(tokens);
        return parser.Parse();
    }

    private static (List<Token> Tokens, string? Error) Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            var start = i;
            if (char.IsAsciiDigit(c))
            {
                while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
                if (i < text.Length && text[i] == '.')
                {
                    i++;
                    if (i >= text.Length || !char.IsAsciiDigit(text[i])) return (tokens, $"Incomplete number at position {start + 1}.");
                    while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
                }
                if (i < text.Length && (char.IsAsciiLetter(text[i]) || text[i] == '_')) return (tokens, $"Invalid number at position {start + 1}.");
                var number = text[start..i];
                if (!decimal.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _)) return (tokens, $"Number too large at position {start + 1}.");
                tokens.Add(new(Kind.Number, number, start + 1));
                continue;
            }
            if (char.IsAsciiLetter(c) || c == '_')
            {
                while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '_')) i++;
                tokens.Add(new(Kind.Identifier, text[start..i], start + 1));
                continue;
            }
            if (c is '"' or '\'')
            {
                i++;
                var closed = false;
                while (i < text.Length)
                {
                    if (text[i] == '\\')
                    {
                        if (i + 1 >= text.Length || text[i + 1] is not ('\\' or '"' or '\'')) return (tokens, $"Unsupported escape in text at position {i + 1}.");
                        i += 2;
                        continue;
                    }
                    if (text[i] == c) { closed = true; i++; break; }
                    if (char.IsControl(text[i])) return (tokens, $"Text cannot contain line breaks or control characters (position {i + 1}).");
                    i++;
                }
                if (!closed) return (tokens, $"Text starting at position {start + 1} is missing its closing quote.");
                tokens.Add(new(Kind.String, text[start..i], start + 1));
                continue;
            }
            var two = i + 1 < text.Length ? text.Substring(i, 2) : null;
            if (two is "&&" or "||" or "==" or "!=" or "<=" or ">=") { tokens.Add(new(Kind.Symbol, two, start + 1)); i += 2; continue; }
            if (c is '!' or '<' or '>' or '(' or ')' or '.' or '-') { tokens.Add(new(Kind.Symbol, c.ToString(), start + 1)); i++; continue; }
            if (c == '=') return (tokens, $"Use == to compare values (position {start + 1}).");
            if (c is '&' or '|') return (tokens, $"Use {c}{c} to combine conditions (position {start + 1}).");
            return (tokens, $"Unsupported character '{c}' at position {start + 1}.");
        }
        tokens.Add(new(Kind.End, "", text.Length + 1));
        return (tokens, null);
    }

    private sealed class Parser(List<Token> tokens)
    {
        private int index;
        private int depth;
        private Token Current => tokens[index];

        public string? Parse()
        {
            var error = Or();
            if (error is not null) return error;
            return Current.Kind == Kind.End ? null : Unexpected();
        }

        private string Unexpected() => Current.Kind == Kind.End
            ? "The expression ends too early."
            : $"Unexpected '{Current.Text}' at position {Current.Position}.";

        private bool Accept(string symbol)
        {
            if (Current.Kind != Kind.Symbol || Current.Text != symbol) return false;
            index++;
            return true;
        }

        private string? Or()
        {
            if (And() is string error) return error;
            while (Accept("||")) if (And() is string next) return next;
            return null;
        }

        private string? And()
        {
            if (Unary() is string error) return error;
            while (Accept("&&")) if (Unary() is string next) return next;
            return null;
        }

        private string? Unary()
        {
            if (Accept("!"))
            {
                if (++depth > MaxDepth) return "The expression is nested too deeply.";
                var error = Unary();
                depth--;
                return error;
            }
            return Comparison();
        }

        private string? Comparison()
        {
            if (Operand() is string error) return error;
            if (Current.Kind == Kind.Symbol && Comparisons.Contains(Current.Text))
            {
                index++;
                if (Operand() is string right) return right;
                if (Current.Kind == Kind.Symbol && Comparisons.Contains(Current.Text))
                    return $"Combine comparisons with && or || (position {Current.Position}).";
            }
            return null;
        }

        private string? Operand()
        {
            var token = Current;
            switch (token.Kind)
            {
                case Kind.Number or Kind.String:
                    index++;
                    return null;
                case Kind.Identifier:
                    index++;
                    if (token.Text is "true" or "false") return null;
                    while (Accept("."))
                    {
                        if (Current.Kind != Kind.Identifier) return $"Expected a name after '.' at position {Current.Position}.";
                        index++;
                    }
                    if (Current.Kind == Kind.Symbol && Current.Text == "(")
                        return $"Function calls are not allowed (position {Current.Position}).";
                    return null;
                case Kind.Symbol when token.Text == "-":
                    index++;
                    if (Current.Kind != Kind.Number) return $"'-' must be followed by a number (position {token.Position}).";
                    index++;
                    return null;
                case Kind.Symbol when token.Text == "(":
                    index++;
                    if (++depth > MaxDepth) return "The expression is nested too deeply.";
                    if (Or() is string inner) return inner;
                    depth--;
                    return Accept(")") ? null : Current.Kind == Kind.End ? "A '(' is missing its closing ')'." : Unexpected();
                default:
                    return Unexpected();
            }
        }
    }
}
