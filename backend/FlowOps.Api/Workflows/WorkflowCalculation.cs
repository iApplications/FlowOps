using System.Globalization;
using System.Text.Json.Nodes;

namespace FlowOps.Api.Workflows;

// Calculation expressions (PHASE6). Same safety rules as conditions: no code is executed, only this grammar is accepted.
//
//   calculation := additive
//   additive    := term ( ( "+" | "-" ) term )*
//   term        := factor ( ( "*" | "/" ) factor )*
//   factor      := "-" factor | number | string | "true" | "false" | path | "(" calculation ")"
//
// Arithmetic needs numbers. A lone value, such as request.amount or "Urgent", is copied as it is, which is how data is
// mapped into a variable. Results are exact decimals; division by zero and overflow are structured errors.
public static partial class WorkflowExpression
{
    public sealed record CalculationResult(JsonNode? Value, string? Type, string? ErrorCode, string? ErrorMessage)
    {
        public static CalculationResult Fail(string code, string message) => new(null, null, code, message);
    }

    private sealed class CalculationSyntaxException(string message) : Exception(message);

    /// <summary>Returns null when the calculation is valid syntax, otherwise a message naming the problem and its position.</summary>
    public static string? ValidateCalculation(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return "Enter an expression.";
        var (tokens, error) = Tokenize(expression);
        if (error is not null) return error;
        if (tokens.Count > MaxTokens) return $"Expressions can have at most {MaxTokens} parts.";
        try { new CalculationEvaluator(tokens, _ => new(ValueKind.Number, Number: 1)).Run(); }
        catch (CalculationSyntaxException exception) { return exception.Message; }
        catch (EvaluationException) { /* Runtime problems such as division by zero are not syntax errors. */ }
        return null;
    }

    /// <summary>Evaluates a calculation against the run context. The result is a JSON number, text or boolean with its type name.</summary>
    public static CalculationResult Calculate(string? expression, JsonObject context)
    {
        if (ValidateCalculation(expression) is string error) return CalculationResult.Fail("expression_invalid", error);
        var (tokens, _) = Tokenize(expression!);
        try
        {
            var value = new CalculationEvaluator(tokens, path => ReadPath(context, path)).Run();
            return value.Kind switch
            {
                ValueKind.Number => new(JsonValue.Create(value.Number), "Number", null, null),
                ValueKind.String => new(JsonValue.Create(value.Text), "Text", null, null),
                ValueKind.Boolean => new(JsonValue.Create(value.Boolean), "Boolean", null, null),
                _ => CalculationResult.Fail("variable_missing", "The calculation has no value.")
            };
        }
        catch (EvaluationException exception) { return CalculationResult.Fail(exception.Code, exception.Message); }
        catch (CalculationSyntaxException exception) { return CalculationResult.Fail("expression_invalid", exception.Message); }
    }

    /// <summary>The dotted variable paths an expression reads (for example <c>vars.total</c>), in order of appearance.</summary>
    public static IReadOnlyList<string> Paths(string? expression)
    {
        var paths = new List<string>();
        if (string.IsNullOrWhiteSpace(expression)) return paths;
        var (tokens, error) = Tokenize(expression);
        if (error is not null) return paths;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != Kind.Identifier || tokens[i].Text is "true" or "false") continue;
            if (i > 0 && tokens[i - 1].Kind == Kind.Symbol && tokens[i - 1].Text == ".") continue;
            var path = tokens[i].Text;
            while (i + 2 < tokens.Count && tokens[i + 1].Kind == Kind.Symbol && tokens[i + 1].Text == "." && tokens[i + 2].Kind == Kind.Identifier)
            {
                path += "." + tokens[i + 2].Text;
                i += 2;
            }
            paths.Add(path);
        }
        return paths;
    }

    private sealed class CalculationEvaluator(List<Token> tokens, Func<List<string>, Value> lookup)
    {
        private int index;
        private int depth;
        private Token Current => tokens[index];

        public Value Run()
        {
            var value = Additive();
            if (Current.Kind != Kind.End) throw new CalculationSyntaxException(Unexpected());
            return value;
        }

        private string Unexpected() => Current.Kind == Kind.End
            ? "The expression ends too early."
            : $"Unexpected '{Current.Text}' at position {Current.Position}.";

        private bool AcceptSymbol(params string[] symbols)
        {
            if (Current.Kind != Kind.Symbol || !symbols.Contains(Current.Text)) return false;
            index++;
            return true;
        }

        private Value Additive()
        {
            var left = Term();
            while (Current.Kind == Kind.Symbol && Current.Text is "+" or "-")
            {
                var symbol = Current.Text;
                index++;
                left = Arithmetic(left, symbol, Term());
            }
            return left;
        }

        private Value Term()
        {
            var left = Factor();
            while (Current.Kind == Kind.Symbol && Current.Text is "*" or "/")
            {
                var symbol = Current.Text;
                index++;
                left = Arithmetic(left, symbol, Factor());
            }
            return left;
        }

        private static Value Arithmetic(Value left, string symbol, Value right)
        {
            if (left.Kind != ValueKind.Number || right.Kind != ValueKind.Number)
                throw new EvaluationException("type_mismatch", $"'{symbol}' needs numbers, not {Describe(left.Kind == ValueKind.Number ? right : left)}.");
            try
            {
                return symbol switch
                {
                    "+" => new(ValueKind.Number, Number: left.Number + right.Number),
                    "-" => new(ValueKind.Number, Number: left.Number - right.Number),
                    "*" => new(ValueKind.Number, Number: left.Number * right.Number),
                    _ when right.Number == 0 => throw new EvaluationException("division_by_zero", "The calculation divides by zero."),
                    _ => new(ValueKind.Number, Number: left.Number / right.Number)
                };
            }
            catch (OverflowException) { throw new EvaluationException("calculation_overflow", "The calculation result is too large."); }
        }

        private Value Factor()
        {
            var token = Current;
            switch (token.Kind)
            {
                case Kind.Number:
                    index++;
                    return new(ValueKind.Number, Number: decimal.Parse(token.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture));
                case Kind.String:
                    index++;
                    return new(ValueKind.String, Text: UnescapeText(token.Text));
                case Kind.Identifier:
                    index++;
                    if (token.Text is "true" or "false") return new(ValueKind.Boolean, token.Text == "true");
                    var path = new List<string> { token.Text };
                    while (AcceptSymbol("."))
                    {
                        if (Current.Kind != Kind.Identifier) throw new CalculationSyntaxException($"Expected a name after '.' at position {Current.Position}.");
                        path.Add(Current.Text);
                        index++;
                    }
                    if (Current.Kind == Kind.Symbol && Current.Text == "(")
                        throw new CalculationSyntaxException($"Function calls are not allowed (position {Current.Position}).");
                    return lookup(path);
                case Kind.Symbol when token.Text == "-":
                    index++;
                    var operand = Factor();
                    if (operand.Kind != ValueKind.Number) throw new EvaluationException("type_mismatch", $"'-' needs a number, not {Describe(operand)}.");
                    return new(ValueKind.Number, Number: -operand.Number);
                case Kind.Symbol when token.Text == "(":
                    index++;
                    if (++depth > MaxDepth) throw new CalculationSyntaxException("The expression is nested too deeply.");
                    var inner = Additive();
                    depth--;
                    if (!AcceptSymbol(")")) throw new CalculationSyntaxException(Current.Kind == Kind.End ? "A '(' is missing its closing ')'." : Unexpected());
                    return inner;
                default:
                    throw new CalculationSyntaxException(Unexpected());
            }
        }
    }

    private static string UnescapeText(string quoted)
    {
        var builder = new System.Text.StringBuilder(quoted.Length);
        for (var i = 1; i < quoted.Length - 1; i++)
            builder.Append(quoted[i] == '\\' ? quoted[++i] : quoted[i]);
        return builder.ToString();
    }
}
