using System.Globalization;

namespace FlowOps.Api.Workflows;

// Condition expressions. FlowOps never executes user code (PROJECT_RULES §14): expressions are parsed
// against this small grammar and nothing else is accepted. Validate checks syntax for the designer; Evaluate
// runs a valid expression against a run's context (Phase 4). Variable typing arrives in Phase 6.
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

    public sealed record ExpressionResult(bool Value, string? ErrorCode, string? ErrorMessage)
    {
        public static ExpressionResult Fail(string code, string message) => new(false, code, message);
    }

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

    /// <summary>
    /// Evaluates a valid expression against the run context. Paths read nested context objects
    /// (<c>request.amount</c>). A missing variable, a non-boolean result or comparing different types is an error,
    /// never a silent false. <c>&amp;&amp;</c> and <c>||</c> short-circuit, so the unevaluated side is not looked up.
    /// </summary>
    public static ExpressionResult Evaluate(string? expression, System.Text.Json.Nodes.JsonObject context)
    {
        if (Validate(expression) is string error) return ExpressionResult.Fail("expression_invalid", error);
        var (tokens, _) = Tokenize(expression!);
        try
        {
            var value = new Evaluator(tokens, context).Evaluate();
            return value.Kind == ValueKind.Boolean ? new(value.Boolean, null, null)
                : ExpressionResult.Fail("type_mismatch", $"The expression must result in true or false, not {Describe(value)}.");
        }
        catch (EvaluationException exception) { return ExpressionResult.Fail(exception.Code, exception.Message); }
    }

    private enum ValueKind { Null, Boolean, Number, String }
    private readonly record struct Value(ValueKind Kind, bool Boolean = false, decimal Number = 0, string Text = "");
    private sealed class EvaluationException(string code, string message) : Exception(message) { public string Code { get; } = code; }

    private static string Describe(Value value) => value.Kind switch
    {
        ValueKind.Null => "an empty value",
        ValueKind.Boolean => "true/false",
        ValueKind.Number => "a number",
        _ => "text"
    };

    // Evaluates the token list produced for an already validated expression; the grammar matches Parser.
    // While skipping (the short-circuited side) it walks the same tokens without reading the context.
    private sealed class Evaluator(List<Token> tokens, System.Text.Json.Nodes.JsonObject context)
    {
        private int index;
        private Token Current => tokens[index];
        private static readonly Value Skipped = new(ValueKind.Boolean);

        public Value Evaluate() => Or(skip: false);

        private bool Accept(string symbol)
        {
            if (Current.Kind != Kind.Symbol || Current.Text != symbol) return false;
            index++;
            return true;
        }

        private Value Or(bool skip)
        {
            var left = And(skip);
            while (Accept("||"))
            {
                var decided = !skip && Boolean(left, "||");
                var right = And(skip || decided);
                if (!skip && !decided) left = new(ValueKind.Boolean, Boolean(right, "||"));
                else if (!skip) left = new(ValueKind.Boolean, true);
            }
            return left;
        }

        private Value And(bool skip)
        {
            var left = Unary(skip);
            while (Accept("&&"))
            {
                var decided = !skip && !Boolean(left, "&&");
                var right = Unary(skip || decided);
                if (!skip && !decided) left = new(ValueKind.Boolean, Boolean(right, "&&"));
                else if (!skip) left = new(ValueKind.Boolean, false);
            }
            return left;
        }

        private Value Unary(bool skip)
        {
            if (!Accept("!")) return Comparison(skip);
            var operand = Unary(skip);
            return skip ? Skipped : new(ValueKind.Boolean, !Boolean(operand, "!"));
        }

        private static bool Boolean(Value value, string symbol) => value.Kind == ValueKind.Boolean ? value.Boolean
            : throw new EvaluationException("type_mismatch", $"'{symbol}' needs true/false values, not {Describe(value)}.");

        private Value Comparison(bool skip)
        {
            var left = Operand(skip);
            if (Current.Kind != Kind.Symbol || !Comparisons.Contains(Current.Text)) return left;
            var symbol = Current.Text;
            index++;
            var right = Operand(skip);
            return skip ? Skipped : new(ValueKind.Boolean, Compare(left, symbol, right));
        }

        private static bool Compare(Value left, string symbol, Value right)
        {
            if (symbol is "==" or "!=")
            {
                // An empty value can be tested for equality with anything; other types must match.
                bool equal;
                if (left.Kind == ValueKind.Null || right.Kind == ValueKind.Null) equal = left.Kind == right.Kind;
                else if (left.Kind != right.Kind) throw Mismatch(left, symbol, right);
                else equal = left.Kind switch
                {
                    ValueKind.Boolean => left.Boolean == right.Boolean,
                    ValueKind.Number => left.Number == right.Number,
                    _ => string.Equals(left.Text, right.Text, StringComparison.Ordinal)
                };
                return symbol == "==" ? equal : !equal;
            }
            int order;
            if (left.Kind == ValueKind.Number && right.Kind == ValueKind.Number) order = left.Number.CompareTo(right.Number);
            else if (left.Kind == ValueKind.String && right.Kind == ValueKind.String) order = string.CompareOrdinal(left.Text, right.Text);
            else throw Mismatch(left, symbol, right);
            return symbol switch { "<" => order < 0, "<=" => order <= 0, ">" => order > 0, _ => order >= 0 };
        }

        private static EvaluationException Mismatch(Value left, string symbol, Value right) =>
            new("type_mismatch", $"Cannot compare {Describe(left)} {symbol} {Describe(right)}.");

        private Value Operand(bool skip)
        {
            var token = Current;
            index++;
            switch (token.Kind)
            {
                case Kind.Number:
                    return new(ValueKind.Number, Number: decimal.Parse(token.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture));
                case Kind.String:
                    return new(ValueKind.String, Text: Unescape(token.Text));
                case Kind.Symbol when token.Text == "-":
                    var number = Current;
                    index++;
                    return new(ValueKind.Number, Number: -decimal.Parse(number.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture));
                case Kind.Symbol:
                    // "(" expression ")"
                    var inner = Or(skip);
                    index++;
                    return inner;
                default:
                    if (token.Text is "true" or "false") return new(ValueKind.Boolean, token.Text == "true");
                    var path = new List<string> { token.Text };
                    while (Accept(".")) { path.Add(Current.Text); index++; }
                    return skip ? Skipped : Lookup(path);
            }
        }

        private Value Lookup(List<string> path)
        {
            System.Text.Json.Nodes.JsonNode? node = context;
            foreach (var segment in path)
            {
                if (node is not System.Text.Json.Nodes.JsonObject current || !current.TryGetPropertyValue(segment, out node))
                    throw new EvaluationException("variable_missing", $"The variable '{string.Join('.', path)}' has no value in this run.");
            }
            if (node is null) return new(ValueKind.Null);
            var name = string.Join('.', path);
            return node.GetValueKind() switch
            {
                System.Text.Json.JsonValueKind.True => new(ValueKind.Boolean, true),
                System.Text.Json.JsonValueKind.False => new(ValueKind.Boolean, false),
                System.Text.Json.JsonValueKind.String => new(ValueKind.String, Text: node.GetValue<string>()),
                System.Text.Json.JsonValueKind.Number when decimal.TryParse(node.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    => new(ValueKind.Number, Number: number),
                System.Text.Json.JsonValueKind.Object or System.Text.Json.JsonValueKind.Array
                    => throw new EvaluationException("type_mismatch", $"The variable '{name}' is a group of values and cannot be compared."),
                _ => throw new EvaluationException("type_mismatch", $"The variable '{name}' is not a number, text or true/false.")
            };
        }

        private static string Unescape(string quoted)
        {
            var builder = new System.Text.StringBuilder(quoted.Length);
            for (var i = 1; i < quoted.Length - 1; i++)
                builder.Append(quoted[i] == '\\' ? quoted[++i] : quoted[i]);
            return builder.ToString();
        }
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
