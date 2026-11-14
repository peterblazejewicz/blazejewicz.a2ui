using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Blazejewicz.A2Ui.Core.Functions;

/// <summary>
/// Closed inclusive Unicode scalar range <c>[Start, End]</c>.
/// </summary>
public readonly record struct CodePointRange(int Start, int End);

/// <summary>
/// Zero-width anchor kinds supported by the A2UI regex dialect.
/// </summary>
public enum AnchorKind
{
    /// <summary>Start of input (^).</summary>
    Start,
    /// <summary>End of input ($).</summary>
    End,
    /// <summary>ASCII word boundary (\b).</summary>
    WordBoundary,
    /// <summary>ASCII not-word boundary (\B).</summary>
    NotWordBoundary,
}

/// <summary>
/// Abstract syntax tree node for the A2UI portable regex subset.
/// </summary>
public abstract record RegexNode;

/// <summary>Empty pattern node.</summary>
public sealed record EmptyNode : RegexNode;

/// <summary>Character set node containing normalized Unicode code point ranges.</summary>
public sealed record CharSetNode(IReadOnlyList<CodePointRange> Ranges) : RegexNode;

/// <summary>Group node wrapping a child expression.</summary>
public sealed record GroupNode(bool Capturing, RegexNode Child) : RegexNode;

/// <summary>Lookahead assertion node.</summary>
public sealed record LookaheadNode(bool Negated, RegexNode Child) : RegexNode;

/// <summary>Zero-width anchor node.</summary>
public sealed record AnchorNode(AnchorKind Anchor) : RegexNode;

/// <summary>Concatenation of sequential expressions.</summary>
public sealed record ConcatNode(IReadOnlyList<RegexNode> Items) : RegexNode;

/// <summary>Alternation of branches.</summary>
public sealed record AltNode(IReadOnlyList<RegexNode> Branches) : RegexNode;

/// <summary>Quantified repetition node.</summary>
public sealed record RepeatNode(RegexNode Child, int Min, int? Max, bool Lazy) : RegexNode;

/// <summary>
/// Implements the portable A2UI regex subset parser, linear-time static safety guard,
/// dialect translator, and evaluation engine per upstream specification.
/// </summary>
public static class RegexSubset
{
    /// <summary>Maximum allowed pattern length in Unicode code points.</summary>
    public const int MaxRegexPatternCodePoints = 256;

    /// <summary>Maximum allowed input value length in Unicode code points.</summary>
    public const int MaxRegexValueCodePoints = 4096;

    private const int SurrogateStart = 0xd800;
    private const int SurrogateEnd = 0xdfff;
    private const int MaxCodePoint = 0x10ffff;
    private const int MaxQuantifierBound = 65535;
    private const int MaxCacheEntries = 256;

    private static readonly CodePointRange s_surrogateRange = new(SurrogateStart, SurrogateEnd);

    private static readonly CodePointRange[] s_digitRanges = [new(0x30, 0x39)];

    private static readonly CodePointRange[] s_wordRanges =
    [
        new(0x30, 0x39),
        new(0x41, 0x5a),
        new(0x5f, 0x5f),
        new(0x61, 0x7a),
    ];

    private static readonly CodePointRange[] s_spaceRanges =
    [
        new(0x09, 0x0d),
        new(0x20, 0x20),
        new(0xa0, 0xa0),
        new(0x1680, 0x1680),
        new(0x2000, 0x200a),
        new(0x2028, 0x2029),
        new(0x202f, 0x202f),
        new(0x205f, 0x205f),
        new(0x3000, 0x3000),
        new(0xfeff, 0xfeff),
    ];

    private static readonly CodePointRange[] s_lineTerminatorRanges =
    [
        new(0x0a, 0x0a),
        new(0x0d, 0x0d),
        new(0x2028, 0x2029),
    ];

    private static readonly FrozenSet<int> s_syntaxChars = FrozenSet.ToFrozenSet(
        [
            0x5e, // ^
            0x24, // $
            0x5c, // \
            0x2e, // .
            0x2a, // *
            0x2b, // +
            0x3f, // ?
            0x28, // (
            0x29, // )
            0x5b, // [
            0x5d, // ]
            0x7b, // {
            0x7d, // }
            0x7c, // |
            0x2f, // /
        ]
    );

    private static readonly Dictionary<int, int> s_controlEscapes = new()
    {
        [0x74] = 0x09, // \t
        [0x6e] = 0x0a, // \n
        [0x76] = 0x0b, // \v
        [0x66] = 0x0c, // \f
        [0x72] = 0x0d, // \r
    };

    private static readonly List<CodePointRange> s_notDigitRanges = ComplementRanges(s_digitRanges);
    private static readonly List<CodePointRange> s_notWordRanges = ComplementRanges(s_wordRanges);
    private static readonly List<CodePointRange> s_notSpaceRanges = ComplementRanges(s_spaceRanges);
    private static readonly List<CodePointRange> s_dotRanges = ComplementRanges(s_lineTerminatorRanges);

    private static readonly Dictionary<int, IReadOnlyList<CodePointRange>> s_classEscapes = new()
    {
        [0x64] = s_digitRanges,
        [0x44] = s_notDigitRanges,
        [0x77] = s_wordRanges,
        [0x57] = s_notWordRanges,
        [0x73] = s_spaceRanges,
        [0x53] = s_notSpaceRanges,
    };

    private static readonly ConcurrentDictionary<string, Regex> s_regexCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Counts Unicode code points (scalar values) in the given input string.
    /// </summary>
    /// <param name="input">The string whose code points to count.</param>
    /// <returns>The number of Unicode code points.</returns>
    public static int CountCodePoints(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        int count = 0;
        for (int i = 0; i < input.Length; i++)
        {
            char cu = input[i];
            if (char.IsHighSurrogate(cu) && i + 1 < input.Length && char.IsLowSurrogate(input[i + 1]))
            {
                i++;
            }

            count++;
        }

        return count;
    }

    /// <summary>
    /// Normalizes, sorts, merges, and strips UTF-16 surrogates from code-point intervals.
    /// </summary>
    public static List<CodePointRange> NormalizeRanges(IEnumerable<CodePointRange> ranges)
    {
        var split = new List<CodePointRange>();
        foreach (var r in ranges)
        {
            split.AddRange(ExcludeSurrogates(r));
        }

        if (split.Count <= 1)
        {
            return split;
        }

        split.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End));

        var merged = new List<CodePointRange> { split[0] };
        for (int i = 1; i < split.Count; i++)
        {
            var prev = merged[^1];
            var curr = split[i];
            if (curr.Start <= prev.End + 1)
            {
                merged[^1] = new CodePointRange(prev.Start, Math.Max(prev.End, curr.End));
            }
            else
            {
                merged.Add(curr);
            }
        }

        return merged;
    }

    /// <summary>
    /// Computes the complement of ranges over all Unicode scalar values (U+0000..U+10FFFF excluding surrogates).
    /// </summary>
    public static List<CodePointRange> ComplementRanges(IReadOnlyList<CodePointRange> ranges)
    {
        var withSurrogates = new List<CodePointRange>(ranges) { s_surrogateRange };
        var excluded = NormalizeRanges(withSurrogates);
        var result = new List<CodePointRange>();
        int cursor = 0;

        foreach (var r in excluded)
        {
            if (r.Start > cursor)
            {
                result.Add(new CodePointRange(cursor, r.Start - 1));
            }

            cursor = r.End + 1;
        }

        if (cursor <= MaxCodePoint)
        {
            result.Add(new CodePointRange(cursor, MaxCodePoint));
        }

        var finalResult = new List<CodePointRange>();
        foreach (var r in result)
        {
            finalResult.AddRange(ExcludeSurrogates(r));
        }

        return finalResult;
    }

    /// <summary>
    /// Returns true if two normalized CodePointRange lists share at least one code point.
    /// </summary>
    public static bool RangesOverlap(IReadOnlyList<CodePointRange> a, IReadOnlyList<CodePointRange> b)
    {
        int i = 0;
        int j = 0;
        while (i < a.Count && j < b.Count)
        {
            if (a[i].End < b[j].Start)
            {
                i++;
            }
            else if (b[j].End < a[i].Start)
            {
                j++;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    private static List<CodePointRange> ExcludeSurrogates(CodePointRange r)
    {
        if (r.Start > r.End)
        {
            return [];
        }

        if (r.End < SurrogateStart || r.Start > SurrogateEnd)
        {
            return [r];
        }

        var outList = new List<CodePointRange>();
        if (r.Start < SurrogateStart)
        {
            outList.Add(new CodePointRange(r.Start, SurrogateStart - 1));
        }

        if (r.End > SurrogateEnd)
        {
            outList.Add(new CodePointRange(SurrogateEnd + 1, r.End));
        }

        return outList;
    }

    private static int[] ToCodePoints(string pattern)
    {
        var cps = new List<int>();
        for (int i = 0; i < pattern.Length; i++)
        {
            char cu = pattern[i];
            if (char.IsHighSurrogate(cu))
            {
                char low = i + 1 < pattern.Length ? pattern[i + 1] : '\0';
                if (!char.IsLowSurrogate(low))
                {
                    throw new RegexSyntaxException("Lone high surrogate in pattern", unsupported: true);
                }

                cps.Add(char.ConvertToUtf32(cu, low));
                i++;
            }
            else if (char.IsLowSurrogate(cu))
            {
                throw new RegexSyntaxException("Lone low surrogate in pattern", unsupported: true);
            }
            else
            {
                cps.Add(cu);
            }
        }

        return [.. cps];
    }

    private sealed class Scanner(int[] cps)
    {
        private readonly int[] _cps = cps;
        private int _pos;

        public bool Eof => this._pos >= this._cps.Length;

        public int Peek(int offset = 0)
        {
            int idx = this._pos + offset;
            return idx >= 0 && idx < this._cps.Length ? this._cps[idx] : -1;
        }

        public int Next()
        {
            if (this._pos >= this._cps.Length)
            {
                throw new RegexSyntaxException("Unexpected end of pattern", unsupported: false);
            }

            return this._cps[this._pos++];
        }

        public bool Eat(int cp)
        {
            if (this._pos < this._cps.Length && this._cps[this._pos] == cp)
            {
                this._pos++;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Exception thrown when an A2UI regex pattern violates subset syntax rules.
    /// </summary>
    public sealed class RegexSyntaxException : Exception
    {
        /// <summary>Whether the syntax error represents unsupported regex syntax.</summary>
        public bool Unsupported { get; }

        /// <summary>Initializes a new instance of <see cref="RegexSyntaxException"/>.</summary>
        public RegexSyntaxException()
        {
        }

        /// <summary>Initializes a new instance of <see cref="RegexSyntaxException"/> with a message.</summary>
        public RegexSyntaxException(string message) : base(message)
        {
        }

        /// <summary>Initializes a new instance of <see cref="RegexSyntaxException"/> with a message and inner exception.</summary>
        public RegexSyntaxException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>Initializes a new instance of <see cref="RegexSyntaxException"/> with unsupported flag.</summary>
        public RegexSyntaxException(string message, bool unsupported) : base(message)
        {
            this.Unsupported = unsupported;
        }
    }

    private static bool IsAsciiDigit(int cp) => cp is >= 0x30 and <= 0x39;

    private static int HexValue(int cp) => cp switch
    {
        >= 0x30 and <= 0x39 => cp - 0x30,
        >= 0x41 and <= 0x46 => cp - 0x41 + 10,
        >= 0x61 and <= 0x66 => cp - 0x61 + 10,
        _ => -1,
    };

    private static int ParseFixedHex(Scanner s, int digits)
    {
        int value = 0;
        for (int i = 0; i < digits; i++)
        {
            int hv = HexValue(s.Peek());
            if (hv < 0)
            {
                throw new RegexSyntaxException("Invalid hexadecimal escape", unsupported: false);
            }

            s.Next();
            value = (value << 4) | hv;
        }

        return value;
    }

    private static int ParseUnicodeEscape(Scanner s)
    {
        if (!s.Eat(0x7b)) // '{'
        {
            int cp = ParseFixedHex(s, 4);
            if (cp is >= SurrogateStart and <= SurrogateEnd)
            {
                throw new RegexSyntaxException("Surrogate code point escape is not supported", unsupported: true);
            }

            return cp;
        }

        int value = 0;
        int count = 0;
        while (!s.Eof && s.Peek() != 0x7d) // '}'
        {
            int hv = HexValue(s.Peek());
            if (hv < 0)
            {
                throw new RegexSyntaxException("Invalid braced Unicode escape", unsupported: false);
            }

            s.Next();
            value = (value * 16) + hv;
            count++;
            if (value > MaxCodePoint)
            {
                throw new RegexSyntaxException("Unicode escape exceeds U+10FFFF", unsupported: false);
            }
        }

        if (count == 0 || !s.Eat(0x7d))
        {
            throw new RegexSyntaxException("Malformed braced Unicode escape", unsupported: false);
        }

        if (value is >= SurrogateStart and <= SurrogateEnd)
        {
            throw new RegexSyntaxException("Surrogate code point escape is not supported", unsupported: true);
        }

        return value;
    }

    private readonly record struct EscapeResult
    {
        public int? Single { get; init; }
        public IReadOnlyList<CodePointRange>? Ranges { get; init; }
        public AnchorKind? Anchor { get; init; }
    }

    private static int? ParseNumericOrHexEscape(Scanner s, int cp)
    {
        if (cp == 0x30) // '0'
        {
            if (IsAsciiDigit(s.Peek()))
            {
                throw new RegexSyntaxException("Octal escape is not supported", unsupported: true);
            }

            return 0x00;
        }

        if (cp is >= 0x31 and <= 0x39)
        {
            throw new RegexSyntaxException("Backreference or octal escape is not supported", unsupported: true);
        }

        if (cp == 0x78) // 'x'
        {
            return ParseFixedHex(s, 2);
        }

        if (cp == 0x75) // 'u'
        {
            return ParseUnicodeEscape(s);
        }

        return null;
    }

    private static EscapeResult ParseEscape(Scanner s, bool inClass)
    {
        if (s.Eof)
        {
            throw new RegexSyntaxException("Trailing backslash in pattern", unsupported: false);
        }

        int cp = s.Next();
        if (cp == 0x62) // 'b'
        {
            return inClass
                ? new EscapeResult { Single = 0x08 }
                : new EscapeResult { Anchor = AnchorKind.WordBoundary };
        }

        if (cp == 0x42) // 'B'
        {
            if (inClass)
            {
                throw new RegexSyntaxException(@"\B is not supported inside a character class", unsupported: true);
            }

            return new EscapeResult { Anchor = AnchorKind.NotWordBoundary };
        }

        if (s_classEscapes.TryGetValue(cp, out var classRanges))
        {
            return new EscapeResult { Ranges = classRanges };
        }

        if (s_controlEscapes.TryGetValue(cp, out int controlCp))
        {
            return new EscapeResult { Single = controlCp };
        }

        int? numOrHex = ParseNumericOrHexEscape(s, cp);
        if (numOrHex.HasValue)
        {
            return new EscapeResult { Single = numOrHex.Value };
        }

        if (s_syntaxChars.Contains(cp) || (inClass && cp == 0x2d))
        {
            return new EscapeResult { Single = cp };
        }

        throw new RegexSyntaxException("Unsupported escape sequence", unsupported: true);
    }

    private readonly record struct ClassAtom(int? Single, IReadOnlyList<CodePointRange> Ranges);

    private static ClassAtom ParseClassAtom(Scanner s)
    {
        int cp = s.Peek();
        if (cp == 0x5c) // '\'
        {
            s.Next();
            EscapeResult esc = ParseEscape(s, inClass: true);
            if (esc.Single.HasValue)
            {
                return new ClassAtom(esc.Single.Value, [new CodePointRange(esc.Single.Value, esc.Single.Value)]);
            }

            if (esc.Ranges is not null)
            {
                return new ClassAtom(null, esc.Ranges);
            }

            throw new RegexSyntaxException("Assertions are not valid in a character class", unsupported: true);
        }

        if (cp == 0x5b) // '['
        {
            throw new RegexSyntaxException("Nested character class is not supported", unsupported: true);
        }

        if (cp == 0x26 && s.Peek(1) == 0x26) // '&&'
        {
            throw new RegexSyntaxException("Character class intersection is not supported", unsupported: true);
        }

        int lit = s.Next();
        return new ClassAtom(lit, [new CodePointRange(lit, lit)]);
    }

    private static IReadOnlyList<CodePointRange> ParseClassItem(Scanner s)
    {
        ClassAtom firstAtom = ParseClassAtom(s);
        if (s.Peek() != 0x2d || s.Peek(1) == 0x5d) // '-' followed by ']'
        {
            return firstAtom.Ranges;
        }

        s.Next(); // consume '-'
        if (s.Peek() == 0x2d) // '--'
        {
            throw new RegexSyntaxException("Character class subtraction is not supported", unsupported: true);
        }

        ClassAtom secondAtom = ParseClassAtom(s);
        if (!firstAtom.Single.HasValue || !secondAtom.Single.HasValue)
        {
            throw new RegexSyntaxException("Character class escape cannot form a range", unsupported: false);
        }

        if (firstAtom.Single.Value > secondAtom.Single.Value)
        {
            throw new RegexSyntaxException("Character class range out of order", unsupported: false);
        }

        return [new CodePointRange(firstAtom.Single.Value, secondAtom.Single.Value)];
    }

    private static CharSetNode ParseClass(Scanner s)
    {
        bool negated = s.Eat(0x5e); // '^'
        var ranges = new List<CodePointRange>();
        while (!s.Eof && s.Peek() != 0x5d) // ']'
        {
            ranges.AddRange(ParseClassItem(s));
        }

        if (!s.Eat(0x5d))
        {
            throw new RegexSyntaxException("Unclosed character class", unsupported: false);
        }

        if (ranges.Count == 0)
        {
            throw new RegexSyntaxException("Empty character class", unsupported: false);
        }

        var normalized = NormalizeRanges(ranges);
        return new CharSetNode(negated ? ComplementRanges(normalized) : normalized);
    }

    private static int? ParseDecimalInt(Scanner s)
    {
        if (!IsAsciiDigit(s.Peek()))
        {
            return null;
        }

        long value = 0;
        while (IsAsciiDigit(s.Peek()))
        {
            value = (value * 10) + (s.Next() - 0x30);
            if (value > MaxQuantifierBound + 1)
            {
                value = MaxQuantifierBound + 1;
            }
        }

        return (int)value;
    }

    private static (int Min, int? Max) ParseBracedBounds(Scanner s)
    {
        int? min = ParseDecimalInt(s);
        if (!min.HasValue)
        {
            throw new RegexSyntaxException("Malformed brace quantifier", unsupported: true);
        }

        if (min.Value > MaxQuantifierBound)
        {
            throw new RegexSyntaxException($"Quantifier lower bound {min.Value} exceeds {MaxQuantifierBound}", unsupported: false);
        }

        if (s.Eat(0x7d)) // '}'
        {
            return (min.Value, min.Value);
        }

        if (!s.Eat(0x2c)) // ','
        {
            throw new RegexSyntaxException("Malformed brace quantifier", unsupported: true);
        }

        if (s.Eat(0x7d)) // '}'
        {
            return (min.Value, null);
        }

        int? max = ParseDecimalInt(s);
        if (!max.HasValue || !s.Eat(0x7d))
        {
            throw new RegexSyntaxException("Malformed brace quantifier", unsupported: true);
        }

        if (max.Value > MaxQuantifierBound)
        {
            throw new RegexSyntaxException($"Quantifier upper bound {max.Value} exceeds {MaxQuantifierBound}", unsupported: false);
        }

        if (max.Value < min.Value)
        {
            throw new RegexSyntaxException("Quantifier minimum exceeds maximum", unsupported: false);
        }

        return (min.Value, max.Value);
    }

    private static bool IsQuantifierStart(int cp) => cp is 0x2a or 0x2b or 0x3f or 0x7b; // '*', '+', '?', '{'

    private static RepeatNode ParseQuantifier(Scanner s, RegexNode child)
    {
        int q = s.Next();
        int min;
        int? max;

        if (q == 0x2a) // '*'
        {
            min = 0;
            max = null;
        }
        else if (q == 0x2b) // '+'
        {
            min = 1;
            max = null;
        }
        else if (q == 0x3f) // '?'
        {
            min = 0;
            max = 1;
        }
        else
        {
            (min, max) = ParseBracedBounds(s);
        }

        bool lazy = s.Eat(0x3f);
        if (IsQuantifierStart(s.Peek()))
        {
            throw new RegexSyntaxException("Possessive or chained quantifiers are not supported", unsupported: true);
        }

        return new RepeatNode(child, min, max, lazy);
    }

    private static RegexNode ParseGroup(Scanner s)
    {
        if (!s.Eat(0x3f)) // '?'
        {
            RegexNode body = ParseDisjunction(s);
            if (!s.Eat(0x29)) // ')'
            {
                throw new RegexSyntaxException("Unclosed group", unsupported: false);
            }

            return new GroupNode(Capturing: true, body);
        }

        int modifier = s.Peek();
        if (modifier is not (0x3a or 0x3d or 0x21)) // ':', '=', '!'
        {
            throw new RegexSyntaxException("Unsupported group extension", unsupported: true);
        }

        s.Next();
        RegexNode childBody = ParseDisjunction(s);
        if (!s.Eat(0x29))
        {
            throw new RegexSyntaxException("Unclosed group", unsupported: false);
        }

        if (modifier == 0x3a) // ':'
        {
            return new GroupNode(Capturing: false, childBody);
        }

        return new LookaheadNode(Negated: modifier == 0x21, childBody);
    }

    private static RegexNode ParseAtom(Scanner s)
    {
        int cp = s.Next();
        if (cp == 0x5e)
        {
            return new AnchorNode(AnchorKind.Start);
        }

        if (cp == 0x24)
        {
            return new AnchorNode(AnchorKind.End);
        }

        if (cp == 0x2e)
        {
            return new CharSetNode(s_dotRanges);
        }

        if (cp == 0x5b)
        {
            return ParseClass(s);
        }

        if (cp == 0x28)
        {
            return ParseGroup(s);
        }

        if (cp == 0x5c)
        {
            EscapeResult esc = ParseEscape(s, inClass: false);
            if (esc.Anchor.HasValue)
            {
                return new AnchorNode(esc.Anchor.Value);
            }

            if (esc.Ranges is not null)
            {
                return new CharSetNode(esc.Ranges);
            }

            return new CharSetNode([new CodePointRange(esc.Single!.Value, esc.Single.Value)]);
        }

        if (cp is 0x7b or 0x7d or 0x5d) // '{', '}', ']'
        {
            throw new RegexSyntaxException("Unescaped brace or bracket is not supported", unsupported: true);
        }

        if (cp is 0x2a or 0x2b or 0x3f) // '*', '+', '?'
        {
            throw new RegexSyntaxException("Nothing to repeat", unsupported: false);
        }

        return new CharSetNode([new CodePointRange(cp, cp)]);
    }

    private static RegexNode ParseTerm(Scanner s)
    {
        RegexNode atom = ParseAtom(s);
        if (!IsQuantifierStart(s.Peek()))
        {
            return atom;
        }

        if (atom is AnchorNode or LookaheadNode)
        {
            throw new RegexSyntaxException("Quantifier on an assertion is not supported", unsupported: true);
        }

        return ParseQuantifier(s, atom);
    }

    private static RegexNode ParseAlternative(Scanner s)
    {
        var items = new List<RegexNode>();
        while (!s.Eof && s.Peek() != 0x7c && s.Peek() != 0x29) // '|', ')'
        {
            items.Add(ParseTerm(s));
        }

        if (items.Count == 0)
        {
            return new EmptyNode();
        }

        if (items.Count == 1)
        {
            return items[0];
        }

        return new ConcatNode(items);
    }

    private static RegexNode ParseDisjunction(Scanner s)
    {
        var branches = new List<RegexNode> { ParseAlternative(s) };
        while (s.Eat(0x7c)) // '|'
        {
            branches.Add(ParseAlternative(s));
        }

        if (branches.Count == 1)
        {
            return branches[0];
        }

        return new AltNode(branches);
    }

    /// <summary>
    /// Parses an A2UI regex pattern into a <see cref="RegexNode"/> AST.
    /// </summary>
    public static RegexNode ParseRegexSubset(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var s = new Scanner(ToCodePoints(pattern));
        RegexNode tree = ParseDisjunction(s);
        if (!s.Eof)
        {
            throw new RegexSyntaxException("Unmatched closing parenthesis", unsupported: false);
        }

        return tree;
    }

    /// <summary>
    /// Strips outer non-capturing or capturing group wrappers from node.
    /// </summary>
    public static RegexNode Unwrap(RegexNode node)
    {
        RegexNode curr = node;
        while (curr is GroupNode g)
        {
            curr = g.Child;
        }

        return curr;
    }

    /// <summary>
    /// Returns true if node can match the empty string ("").
    /// </summary>
    public static bool Nullable(RegexNode node) => node switch
    {
        EmptyNode or AnchorNode or LookaheadNode => true,
        CharSetNode => false,
        GroupNode g => Nullable(g.Child),
        RepeatNode r => r.Min == 0 || Nullable(r.Child),
        ConcatNode c => c.Items.All(Nullable),
        AltNode a => a.Branches.Any(Nullable),
        _ => false,
    };

    /// <summary>
    /// Returns the normalized set of code points that can start a non-empty match of node.
    /// </summary>
    public static List<CodePointRange> First(RegexNode node) => node switch
    {
        EmptyNode or AnchorNode or LookaheadNode => [],
        CharSetNode cs => [.. cs.Ranges],
        GroupNode g => First(g.Child),
        RepeatNode r => r.Max == 0 ? [] : First(r.Child),
        AltNode a => NormalizeRanges(a.Branches.SelectMany(First)),
        ConcatNode c => FirstConcat(c.Items),
        _ => [],
    };

    private static List<CodePointRange> FirstConcat(IReadOnlyList<RegexNode> items)
    {
        var acc = new List<CodePointRange>();
        foreach (var item in items)
        {
            acc.AddRange(First(item));
            if (!Nullable(item))
            {
                break;
            }
        }

        return NormalizeRanges(acc);
    }

    /// <summary>
    /// Returns the normalized union of all character sets consumed in node outside lookaheads.
    /// </summary>
    public static List<CodePointRange> Chars(RegexNode node) => node switch
    {
        EmptyNode or AnchorNode or LookaheadNode => [],
        CharSetNode cs => [.. cs.Ranges],
        GroupNode g => Chars(g.Child),
        RepeatNode r => r.Max == 0 ? [] : Chars(r.Child),
        ConcatNode c => NormalizeRanges(c.Items.SelectMany(Chars)),
        AltNode a => NormalizeRanges(a.Branches.SelectMany(Chars)),
        _ => [],
    };

    /// <summary>
    /// Returns true if node or any descendant contains an unbounded quantifier (Max == null).
    /// </summary>
    public static bool HasUnbounded(RegexNode node) => node switch
    {
        EmptyNode or CharSetNode or AnchorNode => false,
        GroupNode g => HasUnbounded(g.Child),
        LookaheadNode l => HasUnbounded(l.Child),
        RepeatNode r => r.Max is null || HasUnbounded(r.Child),
        ConcatNode c => c.Items.Any(HasUnbounded),
        AltNode a => a.Branches.Any(HasUnbounded),
        _ => false,
    };

    private static bool HasNestedQuantifier(RegexNode node) => node switch
    {
        EmptyNode or CharSetNode or AnchorNode => false,
        GroupNode g => HasNestedQuantifier(g.Child),
        LookaheadNode l => HasNestedQuantifier(l.Child),
        RepeatNode r => ((r.Max is null || r.Max > 1) && HasUnbounded(r.Child)) || HasNestedQuantifier(r.Child),
        ConcatNode c => c.Items.Any(HasNestedQuantifier),
        AltNode a => a.Branches.Any(HasNestedQuantifier),
        _ => false,
    };

    private static bool BranchesOverlap(IReadOnlyList<RegexNode> branches)
    {
        var firstSets = branches.Select(b => First(Unwrap(b))).ToList();
        for (int i = 0; i < firstSets.Count; i++)
        {
            for (int j = i + 1; j < firstSets.Count; j++)
            {
                if (RangesOverlap(firstSets[i], firstSets[j]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasOverlappingAlternation(RegexNode node) => node switch
    {
        EmptyNode or CharSetNode or AnchorNode => false,
        GroupNode g => HasOverlappingAlternation(g.Child),
        LookaheadNode l => HasOverlappingAlternation(l.Child),
        RepeatNode r => (r.Max is null && Unwrap(r.Child) is AltNode alt && BranchesOverlap(alt.Branches))
            || HasOverlappingAlternation(r.Child),
        ConcatNode c => c.Items.Any(HasOverlappingAlternation),
        AltNode a => a.Branches.Any(HasOverlappingAlternation),
        _ => false,
    };

    private static bool ConcatHasAdjacentOverlap(IReadOnlyList<RegexNode> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            RegexNode ux = Unwrap(items[i]);
            if (ux is not RepeatNode rx || rx.Max is not null)
            {
                continue;
            }

            var xChars = Chars(rx.Child);
            for (int j = i + 1; j < items.Count; j++)
            {
                RegexNode uy = Unwrap(items[j]);
                if (uy is RepeatNode ry && ry.Max is null && RangesOverlap(xChars, Chars(ry.Child)))
                {
                    return true;
                }

                if (!Nullable(items[j]))
                {
                    break;
                }
            }
        }

        return false;
    }

    private static bool HasAdjacentOverlap(RegexNode node) => node switch
    {
        EmptyNode or CharSetNode or AnchorNode => false,
        GroupNode g => HasAdjacentOverlap(g.Child),
        LookaheadNode l => HasAdjacentOverlap(l.Child),
        RepeatNode r => HasAdjacentOverlap(r.Child),
        ConcatNode c => ConcatHasAdjacentOverlap(c.Items) || c.Items.Any(HasAdjacentOverlap),
        AltNode a => a.Branches.Any(HasAdjacentOverlap),
        _ => false,
    };

    /// <summary>
    /// Checks the 4 static safety rules on a parsed RegexNode AST.
    /// </summary>
    /// <returns>Rejection token if unsafe, or null if safe.</returns>
    public static string? CheckRegexSafety(string pattern, RegexNode tree)
    {
        if (CountCodePoints(pattern) > MaxRegexPatternCodePoints)
        {
            return "PATTERN_TOO_LONG";
        }

        if (HasNestedQuantifier(tree))
        {
            return "NESTED_QUANTIFIER";
        }

        if (HasOverlappingAlternation(tree))
        {
            return "OVERLAPPING_ALTERNATION";
        }

        if (HasAdjacentOverlap(tree))
        {
            return "ADJACENT_OVERLAP";
        }

        return null;
    }

    /// <summary>
    /// Validates an A2UI regex pattern against maximum pattern length, supported syntax,
    /// and the 4 linear safety rules.
    /// </summary>
    /// <exception cref="A2UiEvaluationException">
    /// Thrown with appropriate rejection tokens (PATTERN_TOO_LONG, UNSUPPORTED_SYNTAX,
    /// NESTED_QUANTIFIER, OVERLAPPING_ALTERNATION, ADJACENT_OVERLAP) if invalid.
    /// </exception>
    public static void ValidatePattern(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        if (CountCodePoints(pattern) > MaxRegexPatternCodePoints)
        {
            throw new A2UiEvaluationException("Regex pattern exceeds maximum length (PATTERN_TOO_LONG).");
        }

        RegexNode tree;
        try
        {
            tree = ParseRegexSubset(pattern);
        }
        catch (RegexSyntaxException ex)
        {
            throw new A2UiEvaluationException($"Invalid regex syntax (UNSUPPORTED_SYNTAX): {ex.Message}", ex);
        }

        string? rejection = CheckRegexSafety(pattern, tree);
        if (rejection is not null)
        {
            throw new A2UiEvaluationException($"Unsafe regex pattern ({rejection}).");
        }
    }

    /// <summary>
    /// Translates a validated A2UI portable regex pattern to an equivalent .NET ECMAScript Regex pattern.
    /// </summary>
    public static string TranslatePattern(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        var sb = new StringBuilder();
        bool inClass = false;

        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '\\')
            {
                if (i + 1 >= pattern.Length)
                {
                    sb.Append('\\');
                    break;
                }

                char next = pattern[i + 1];
                if (next == 'u' && i + 2 < pattern.Length && pattern[i + 2] == '{')
                {
                    int closeIndex = pattern.IndexOf('}', i + 3);
                    if (closeIndex != -1)
                    {
                        string hex = pattern.Substring(i + 3, closeIndex - (i + 3));
                        int cp = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        if (cp > 0xFFFF)
                        {
                            string surrogate = char.ConvertFromUtf32(cp);
                            string escaped = $"\\u{(int)surrogate[0]:X4}\\u{(int)surrogate[1]:X4}";
                            if (inClass)
                            {
                                sb.Append(escaped);
                            }
                            else
                            {
                                sb.Append("(?:" + escaped + ")");
                            }
                        }
                        else
                        {
                            sb.Append($"\\u{cp:X4}");
                        }

                        i = closeIndex;
                        continue;
                    }
                }

                sb.Append('\\');
                sb.Append(next);
                i++;
                continue;
            }

            if (c == '[' && !inClass)
            {
                inClass = true;
                sb.Append(c);
                continue;
            }

            if (c == ']' && inClass)
            {
                inClass = false;
                sb.Append(c);
                continue;
            }

            if (!inClass)
            {
                if (c == '$')
                {
                    sb.Append(@"\z");
                    continue;
                }

                if (c == '.')
                {
                    sb.Append(@"(?:[^\r\n\uD800-\uDFFF]|[\uD800-\uDBFF][\uDC00-\uDFFF])");
                    continue;
                }
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static Regex GetOrCreateRegex(string pattern)
    {
        if (s_regexCache.TryGetValue(pattern, out var cached))
        {
            return cached;
        }

        string translated = TranslatePattern(pattern);
        var regex = new Regex(
            translated,
            RegexOptions.ECMAScript | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1)
        );

        if (s_regexCache.Count >= MaxCacheEntries)
        {
            s_regexCache.Clear();
        }

        s_regexCache[pattern] = regex;
        return regex;
    }

    /// <summary>
    /// Validates the pattern and checks whether the value matches the pattern under A2UI regex semantics.
    /// If value exceeds <see cref="MaxRegexValueCodePoints"/>, returns false without throwing.
    /// </summary>
    /// <param name="pattern">The regex pattern to test.</param>
    /// <param name="value">The input string to match against.</param>
    /// <returns>True if the pattern matches the value; otherwise false.</returns>
    /// <exception cref="A2UiEvaluationException">Thrown when pattern is invalid or unsafe.</exception>
    public static bool IsMatch(string pattern, string value)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(value);

        ValidatePattern(pattern);

        if (CountCodePoints(value) > MaxRegexValueCodePoints)
        {
            return false;
        }

        Regex regex = GetOrCreateRegex(pattern);
        return regex.IsMatch(value);
    }
}
