using System.Globalization;
using System.Text;

namespace Blazejewicz.A2Ui.Core.Functions;

/// <summary>
/// Discriminated union for expression tokens produced by <see cref="ExpressionParser"/>.
/// </summary>
public abstract record ExpressionToken;

/// <summary>Literal text token.</summary>
public sealed record LiteralToken(string Value) : ExpressionToken;

/// <summary>Data model JSON Pointer path token.</summary>
public sealed record PathToken(string Path) : ExpressionToken;

/// <summary>Boolean literal token.</summary>
public sealed record BoolToken(bool Value) : ExpressionToken;

/// <summary>Numeric literal token.</summary>
public sealed record NumberToken(double Value) : ExpressionToken;

/// <summary>Function call token with named arguments.</summary>
public sealed record FunctionCallToken(string Name, IReadOnlyDictionary<string, ExpressionToken> Args)
    : ExpressionToken;

/// <summary>
/// Recursive-descent parser that interpolates <c>${...}</c> expressions in strings,
/// supporting nested expressions, path bindings, function calls, and escape sequences.
/// </summary>
public sealed class ExpressionParser
{
    /// <summary>Maximum allowed recursion depth to prevent stack overflows.</summary>
    private const int MaxDepth = 100;

    /// <summary>
    /// Parse an input string into a list of expression tokens.
    /// Literal text between <c>${...}</c> markers becomes <see cref="LiteralToken"/>;
    /// interpolations are recursively parsed into typed tokens.
    /// </summary>
    public IReadOnlyList<ExpressionToken> Parse(string input, int depth = 0)
    {
        if (depth > MaxDepth)
        {
            throw new A2UiExpressionException("Max recursion depth reached in parse");
        }

        if (string.IsNullOrEmpty(input))
        {
            return [];
        }

        if (!input.Contains("${", StringComparison.Ordinal))
        {
            return [new LiteralToken(input)];
        }

        var parts = new List<ExpressionToken>();
        var scanner = new Scanner(input);

        while (!scanner.IsAtEnd)
        {
            if (scanner.Matches("${"))
            {
                scanner.Advance(2);
                string content = ExtractInterpolationContent(scanner);
                ExpressionToken parsed = this.ParseExpression(content, depth + 1);
                AddTokenOrMergeLiteral(parts, parsed);
            }
            else if (scanner.Peek() == '\\' && scanner.Peek(1) == '$' && scanner.Peek(2) == '{')
            {
                scanner.Advance(); // skip backslash
                AddTokenOrMergeLiteral(parts, new LiteralToken("${"));
                scanner.Advance(2); // skip ${
            }
            else
            {
                int start = scanner.Position;
                while (!scanner.IsAtEnd)
                {
                    if (scanner.Matches("${"))
                    {
                        break;
                    }

                    if (scanner.Peek() == '\\' && scanner.Peek(1) == '$' && scanner.Peek(2) == '{')
                    {
                        break;
                    }

                    scanner.Advance();
                }

                string literal = input[start..scanner.Position];
                if (literal.Length > 0)
                {
                    AddTokenOrMergeLiteral(parts, new LiteralToken(literal));
                }
            }
        }

        return parts;
    }

    private static void AddTokenOrMergeLiteral(List<ExpressionToken> parts, ExpressionToken token)
    {
        if (token is LiteralToken { Value.Length: 0 })
        {
            return;
        }

        if (token is LiteralToken lit && parts.Count > 0 && parts[^1] is LiteralToken prevLit)
        {
            parts[^1] = new LiteralToken(prevLit.Value + lit.Value);
            return;
        }

        parts.Add(token);
    }

    /// <summary>
    /// Parse a single expression string (the content inside <c>${...}</c>) into a token.
    /// </summary>
    public ExpressionToken ParseExpression(string expr, int depth = 0)
    {
        if (depth > MaxDepth)
        {
            throw new A2UiExpressionException("Max recursion depth reached in parse");
        }

        expr = expr.Trim();
        if (expr.Length == 0)
        {
            return new LiteralToken("");
        }

        var scanner = new Scanner(expr);
        ExpressionToken result = this.ParseExpressionInternal(scanner, depth);
        scanner.SkipWhitespace();

        if (!scanner.IsAtEnd)
        {
            throw new A2UiExpressionException(
                $"Unexpected characters at end of expression: '{expr[scanner.Position..]}'"
            );
        }

        return result;
    }

    private ExpressionToken ParseExpressionInternal(Scanner scanner, int depth)
    {
        scanner.SkipWhitespace();
        if (scanner.IsAtEnd)
        {
            return new LiteralToken("");
        }

        // 0. Nested interpolation
        if (scanner.Matches("${"))
        {
            scanner.Advance(2);
            string content = ExtractInterpolationContent(scanner);
            return this.ParseExpression(content, depth + 1);
        }

        // 1. String literals
        char ch = scanner.Peek();
        if (ch is '\'' or '"')
        {
            return ParseStringLiteral(scanner);
        }

        // 2. Number literals (digit, leading dot before digit, or sign before digit/dot-digit)
        if (IsNumberStart(scanner))
        {
            return ParseNumberLiteral(scanner);
        }

        // 3. Keywords
        if (scanner.MatchesKeyword("true"))
        {
            return new BoolToken(true);
        }

        if (scanner.MatchesKeyword("false"))
        {
            return new BoolToken(false);
        }

        if (scanner.MatchesKeyword("null"))
        {
            return new LiteralToken("");
        }

        // 4. Identifier or path → possibly a function call
        string token = ScanPathOrIdentifier(scanner);
        scanner.SkipWhitespace();

        if (!scanner.IsAtEnd && scanner.Peek() == '(')
        {
            return this.ParseFunctionCall(token, scanner, depth);
        }

        if (token.Length == 0)
        {
            throw new A2UiExpressionException(
                $"Unexpected characters at end of expression: '{scanner.Input[scanner.Position..]}'"
            );
        }

        return new PathToken(token);
    }

    private static bool IsNumberStart(Scanner scanner)
    {
        char ch = scanner.Peek();
        if (IsDigit(ch))
        {
            return true;
        }

        if (ch == '.' && IsDigit(scanner.Peek(1)))
        {
            return true;
        }

        if (ch is '+' or '-' && (IsDigit(scanner.Peek(1)) || (scanner.Peek(1) == '.' && IsDigit(scanner.Peek(2)))))
        {
            return true;
        }

        return false;
    }

    private static string ScanPathOrIdentifier(Scanner scanner)
    {
        int start = scanner.Position;
        while (!scanner.IsAtEnd)
        {
            char c = scanner.Peek();
            if (c is '/' or '.' or '-' or '_')
            {
                scanner.Advance();
            }
            else if (
                Rune.TryGetRuneAt(scanner.Input, scanner.Position, out Rune rune) && Scanner.IsXidContinue(rune)
            )
            {
                scanner.Advance(rune.Utf16SequenceLength);
            }
            else
            {
                break;
            }
        }

        return scanner.Input[start..scanner.Position];
    }

    private static string ScanIdentifier(Scanner scanner)
    {
        int start = scanner.Position;
        while (!scanner.IsAtEnd)
        {
            if (Rune.TryGetRuneAt(scanner.Input, scanner.Position, out Rune rune) && Scanner.IsXidContinue(rune))
            {
                scanner.Advance(rune.Utf16SequenceLength);
            }
            else
            {
                break;
            }
        }

        return scanner.Input[start..scanner.Position];
    }

    private FunctionCallToken ParseFunctionCall(string funcName, Scanner scanner, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new A2UiExpressionException("Max recursion depth reached in parse");
        }

        scanner.Match('(');
        scanner.SkipWhitespace();

        var args = new Dictionary<string, ExpressionToken>();

        while (!scanner.IsAtEnd && scanner.Peek() != ')')
        {
            string argName = ScanIdentifier(scanner);
            scanner.SkipWhitespace();

            if (argName.Length == 0 || !scanner.Match(':'))
            {
                throw new A2UiExpressionException(
                    $"Expected ':' after argument name '{argName}' in function '{funcName}'"
                );
            }

            scanner.SkipWhitespace();
            args[argName] = this.ParseExpressionInternal(scanner, depth + 1);
            scanner.SkipWhitespace();

            if (!scanner.IsAtEnd && scanner.Peek() == ',')
            {
                scanner.Advance();
                scanner.SkipWhitespace();
            }
        }

        if (!scanner.Match(')'))
        {
            throw new A2UiExpressionException($"Expected ')' after function arguments for '{funcName}'");
        }

        return new FunctionCallToken(funcName, args);
    }

    private static LiteralToken ParseStringLiteral(Scanner scanner)
    {
        char quote = scanner.Advance();
        var sb = new StringBuilder();
        bool closed = false;

        while (!scanner.IsAtEnd)
        {
            char c = scanner.Advance();
            if (c == '\\')
            {
                if (scanner.IsAtEnd)
                {
                    break; // trailing backslash at end-of-input
                }

                char next = scanner.Advance();
                sb.Append(
                    next switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        'r' => '\r',
                        _ => next,
                    }
                );
            }
            else if (c == quote)
            {
                closed = true;
                break;
            }
            else
            {
                sb.Append(c);
            }
        }

        if (!closed)
        {
            throw new A2UiExpressionException($"Unterminated string literal: missing closing '{quote}'");
        }

        return new LiteralToken(sb.ToString());
    }

    private static NumberToken ParseNumberLiteral(Scanner scanner)
    {
        int start = scanner.Position;
        if (scanner.Peek() is '+' or '-')
        {
            scanner.Advance();
        }

        bool seenDot = false;
        while (!scanner.IsAtEnd)
        {
            char c = scanner.Peek();
            if (IsDigit(c))
            {
                scanner.Advance();
            }
            else if (c == '.')
            {
                if (seenDot)
                {
                    scanner.Advance();
                    while (!scanner.IsAtEnd && (IsDigit(scanner.Peek()) || scanner.Peek() == '.'))
                    {
                        scanner.Advance();
                    }

                    throw new A2UiExpressionException(
                        $"Invalid number literal: '{scanner.Input[start..scanner.Position]}'"
                    );
                }

                seenDot = true;
                scanner.Advance();
            }
            else
            {
                break;
            }
        }

        if (!scanner.IsAtEnd && scanner.Peek() is 'e' or 'E')
        {
            scanner.Advance();
            if (!scanner.IsAtEnd && scanner.Peek() is '+' or '-')
            {
                scanner.Advance();
            }

            int expDigits = 0;
            while (!scanner.IsAtEnd && IsDigit(scanner.Peek()))
            {
                scanner.Advance();
                expDigits++;
            }

            if (expDigits == 0 || (!scanner.IsAtEnd && scanner.Peek() == '.'))
            {
                throw new A2UiExpressionException(
                    $"Invalid number literal: '{scanner.Input[start..scanner.Position]}'"
                );
            }
        }

        string text = scanner.Input[start..scanner.Position];
        if (
            !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            || double.IsInfinity(value)
            || double.IsNaN(value)
        )
        {
            throw new A2UiExpressionException($"Number literal out of range: '{text}'");
        }

        if (value == 0.0)
        {
            value = 0.0;
        }

        return new NumberToken(value);
    }

    private static string ExtractInterpolationContent(Scanner scanner)
    {
        int start = scanner.Position;
        int braceBalance = 1;

        while (!scanner.IsAtEnd && braceBalance > 0)
        {
            char c = scanner.Advance();
            if (c == '{')
            {
                braceBalance++;
            }
            else if (c == '}')
            {
                braceBalance--;
            }
            else if (c is '\'' or '"')
            {
                // Skip over quoted strings so we don't count braces inside them
                char quoteChar = c;
                while (!scanner.IsAtEnd)
                {
                    char sc = scanner.Advance();
                    if (sc == '\\')
                    {
                        scanner.Advance();
                    }
                    else if (sc == quoteChar)
                    {
                        break;
                    }
                }
            }
        }

        if (braceBalance > 0)
        {
            throw new A2UiExpressionException("Unclosed interpolation: missing '}'");
        }

        // Exclude the closing brace from the content
        return scanner.Input[start..(scanner.Position - 1)];
    }

    private static bool IsDigit(char c) => c is >= '0' and <= '9';
}

/// <summary>
/// Position-based string scanner used by <see cref="ExpressionParser"/>.
/// </summary>
internal sealed class Scanner(string input)
{
    public string Input { get; } = input;
    public int Position { get; private set; }

    public bool IsAtEnd => this.Position >= this.Input.Length;

    public char Peek(int offset = 0)
    {
        int idx = this.Position + offset;
        return idx < this.Input.Length ? this.Input[idx] : '\0';
    }

    public char Advance(int count = 1)
    {
        char c = this.Position < this.Input.Length ? this.Input[this.Position] : '\0';
        this.Position = Math.Min(this.Position + count, this.Input.Length);
        return c;
    }

    public bool Match(char expected)
    {
        if (this.Peek() == expected)
        {
            this.Advance();
            return true;
        }
        return false;
    }

    public bool Matches(string expected) => this.Input.AsSpan(this.Position).StartsWith(expected);

    public bool MatchesKeyword(string keyword)
    {
        if (!this.Input.AsSpan(this.Position).StartsWith(keyword))
        {
            return false;
        }

        int afterEnd = this.Position + keyword.Length;
        if (afterEnd < this.Input.Length)
        {
            char next = this.Input[afterEnd];
            if (
                next is '/' or '.' or '-' or '_'
                || (Rune.TryGetRuneAt(this.Input, afterEnd, out Rune nextRune) && IsXidContinue(nextRune))
            )
            {
                return false;
            }
        }

        this.Position += keyword.Length;
        return true;
    }

    public static bool IsXidContinue(Rune rune) =>
        Rune.GetUnicodeCategory(rune)
            is UnicodeCategory.UppercaseLetter
                or UnicodeCategory.LowercaseLetter
                or UnicodeCategory.TitlecaseLetter
                or UnicodeCategory.ModifierLetter
                or UnicodeCategory.OtherLetter
                or UnicodeCategory.LetterNumber
                or UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.DecimalDigitNumber
                or UnicodeCategory.ConnectorPunctuation;

    public void SkipWhitespace()
    {
        while (!this.IsAtEnd && char.IsWhiteSpace(this.Peek()))
        {
            this.Advance();
        }
    }
}

/// <summary>
/// Exception thrown when an A2UI expression cannot be parsed.
/// </summary>
public sealed class A2UiExpressionException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="A2UiExpressionException"/> class.</summary>
    public A2UiExpressionException() { }

    /// <summary>Initializes a new instance with a message.</summary>
    /// <param name="message">The error message.</param>
    public A2UiExpressionException(string message)
        : base(message) { }

    /// <summary>Initializes a new instance with a message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public A2UiExpressionException(string message, Exception innerException)
        : base(message, innerException) { }
}
