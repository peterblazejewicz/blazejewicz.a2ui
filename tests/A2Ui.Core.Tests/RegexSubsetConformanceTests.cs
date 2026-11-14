using Blazejewicz.A2Ui.Core.Functions;

namespace Blazejewicz.A2Ui.Core.Tests;

public sealed class RegexSubsetConformanceTests
{
    // --- Basic Regex Function Cases ---

    [Fact]
    public void Test_EvaluateFunction_RegexMatch_MatchesValidNumber()
    {
        bool result = RegexSubset.IsMatch("^[0-9]+$", "12345");
        Assert.True(result);
    }

    [Fact]
    public void Test_EvaluateFunction_RegexMismatch_RejectsLetters()
    {
        bool result = RegexSubset.IsMatch("^[0-9]+$", "abc");
        Assert.False(result);
    }

    [Fact]
    public void Test_EvaluateFunction_RegexInvalidPattern_ThrowsExpressionError()
    {
        var ex = Assert.Throws<A2UiEvaluationException>(() => RegexSubset.IsMatch("[invalid(", "abc"));
        Assert.Contains("UNSUPPORTED_SYNTAX", ex.Message);
    }

    // --- 31 A2UI Regex Dialect & Static Safety Guard Conformance Cases ---

    [Fact]
    public void Test_EvaluateFunction_Regex_V10_Dollar_DoesNotMatchBeforeTrailingNewline()
    {
        bool result = RegexSubset.IsMatch("^abc$", "abc\n");
        Assert.False(result);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V10_DigitClass_AsciiOnly()
    {
        bool result = RegexSubset.IsMatch(@"^\d+$", "١٢٣");
        Assert.False(result);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V10_WordClass_AsciiOnly()
    {
        bool result = RegexSubset.IsMatch(@"^\w+$", "café");
        Assert.False(result);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V10_Dot_ExcludesNewline()
    {
        bool result = RegexSubset.IsMatch("^a.b$", "a\nb");
        Assert.False(result);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V10_CaseSensitive()
    {
        bool result = RegexSubset.IsMatch("^abc$", "ABC");
        Assert.False(result);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V10_WordBoundary_AsciiOnly()
    {
        bool result = RegexSubset.IsMatch(@"\b", "é");
        Assert.False(result);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V10_SearchSemantics()
    {
        bool result = RegexSubset.IsMatch("abc", "xxabcxx");
        Assert.True(result);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V10_AstralDot_MatchesSingleCodePoint()
    {
        bool result = RegexSubset.IsMatch("^.$", "😀");
        Assert.True(result);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V10_BracedUnicodeEscape()
    {
        bool result = RegexSubset.IsMatch(@"^\u{1F600}+$", "😀😀");
        Assert.True(result);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V10_RejectsBackreference()
    {
        var ex = Assert.Throws<A2UiEvaluationException>(() => RegexSubset.IsMatch(@"(a)\1", "aa"));
        Assert.Contains("UNSUPPORTED_SYNTAX", ex.Message);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V10_RejectsLookbehind()
    {
        var ex = Assert.Throws<A2UiEvaluationException>(() => RegexSubset.IsMatch("(?<=a)b", "ab"));
        Assert.Contains("UNSUPPORTED_SYNTAX", ex.Message);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V10_RejectsNamedGroup()
    {
        var ex = Assert.Throws<A2UiEvaluationException>(() => RegexSubset.IsMatch("(?<n>a)", "a"));
        Assert.Contains("UNSUPPORTED_SYNTAX", ex.Message);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V10_RejectsInlineFlag()
    {
        var ex = Assert.Throws<A2UiEvaluationException>(() => RegexSubset.IsMatch("(?i)a", "a"));
        Assert.Contains("UNSUPPORTED_SYNTAX", ex.Message);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V10_RejectsUnicodeProperty()
    {
        var ex = Assert.Throws<A2UiEvaluationException>(() => RegexSubset.IsMatch(@"\p{L}", "a"));
        Assert.Contains("UNSUPPORTED_SYNTAX", ex.Message);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V10_RejectsNestedQuantifierPlus()
    {
        var ex = Assert.Throws<A2UiEvaluationException>(() => RegexSubset.IsMatch("(a+)+$", "aaa"));
        Assert.Contains("NESTED_QUANTIFIER", ex.Message);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V10_RejectsNestedQuantifierBoundedOuter()
    {
        var ex = Assert.Throws<A2UiEvaluationException>(() => RegexSubset.IsMatch("(a*){2,5}", "aaa"));
        Assert.Contains("NESTED_QUANTIFIER", ex.Message);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V10_RejectsOverlappingAlternationPrefix()
    {
        var ex = Assert.Throws<A2UiEvaluationException>(() => RegexSubset.IsMatch("(a|ab)*c", "abc"));
        Assert.Contains("OVERLAPPING_ALTERNATION", ex.Message);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V10_RejectsOverlappingAlternationClass()
    {
        var ex = Assert.Throws<A2UiEvaluationException>(() => RegexSubset.IsMatch(@"(\d|[0-5])+", "123"));
        Assert.Contains("OVERLAPPING_ALTERNATION", ex.Message);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V10_RejectsAdjacentOverlapDirect()
    {
        var ex = Assert.Throws<A2UiEvaluationException>(() => RegexSubset.IsMatch(@"\d+\d+", "12"));
        Assert.Contains("ADJACENT_OVERLAP", ex.Message);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V10_RejectsAdjacentOverlapAcrossNullable()
    {
        var ex = Assert.Throws<A2UiEvaluationException>(() => RegexSubset.IsMatch(@"\d+\s*\d+", "12"));
        Assert.Contains("ADJACENT_OVERLAP", ex.Message);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V10_AcceptsOptionalOuterGroup()
    {
        bool result = RegexSubset.IsMatch("(?:ab+)?c", "abbc");
        Assert.True(result);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V10_AcceptsDisjointAlternation()
    {
        bool result = RegexSubset.IsMatch("(a|b)*c", "abac");
        Assert.True(result);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V10_AcceptsNonNullableSeparator()
    {
        bool result = RegexSubset.IsMatch(@"\w+\s+\w+", "hello world");
        Assert.True(result);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V10_AcceptsAnchoredEmailLike()
    {
        bool result = RegexSubset.IsMatch(@"^[a-z]+@[a-z]+\.com$", "ada@example.com");
        Assert.True(result);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V10_AcceptsLookaheadPasswordLike()
    {
        bool result = RegexSubset.IsMatch(@"^(?=.*\d).{8,}$", "secret42");
        Assert.True(result);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V10_Accepts256CodePointPattern()
    {
        string p256 = new('a', 256);
        bool result = RegexSubset.IsMatch(p256, p256);
        Assert.True(result);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V10_Rejects257CodePointPattern()
    {
        string p257 = new('a', 257);
        var ex = Assert.Throws<A2UiEvaluationException>(() => RegexSubset.IsMatch(p257, "a"));
        Assert.Contains("PATTERN_TOO_LONG", ex.Message);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V10_Accepts4096CodePointValue()
    {
        string v4096 = new('a', 4096);
        bool result = RegexSubset.IsMatch("^a+$", v4096);
        Assert.True(result);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V10_ValueExceeding4096CodePoints_ReturnsInvalid()
    {
        string v4097 = new('a', 4097);
        bool result = RegexSubset.IsMatch("^a+$", v4097);
        Assert.False(result);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V09_RejectsNestedQuantifier()
    {
        var ex = Assert.Throws<A2UiEvaluationException>(() => RegexSubset.IsMatch("(a+)+$", "aaa"));
        Assert.Contains("NESTED_QUANTIFIER", ex.Message);
    }

    [Fact]
    public void Test_EvaluateFunction_Regex_V09_ValueExceeding4096CodePoints_ReturnsFalse()
    {
        string v4097 = new('a', 4097);
        bool result = RegexSubset.IsMatch("^a+$", v4097);
        Assert.False(result);
    }
}
