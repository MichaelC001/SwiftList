using SwiftList.Core.SearchIndex.Query;

namespace SwiftList.Core.Tests.SearchIndex.Query;

[TestClass]
public sealed class SearchQuerySortParserTests
{
    [TestMethod]
    public void Strip_NoSuffix_ReturnsQueryUnchangedWithEmptyTokens()
    {
        var result = SearchQuerySortParser.Strip("readme", out var tokens);

        Assert.AreEqual("readme", result);
        Assert.IsEmpty(tokens);
    }

    [TestMethod]
    public void Strip_ValidSuffix_ExtractsTokensAndTrimsQuery()
    {
        var result = SearchQuerySortParser.Strip("readme :a,b,c", out var tokens);

        Assert.AreEqual("readme", result);
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, tokens.ToArray());
    }

    [TestMethod]
    public void Strip_SuffixOnly_ReturnsEmptyQuery()
    {
        var result = SearchQuerySortParser.Strip(":a,b", out var tokens);

        Assert.AreEqual(string.Empty, result);
        CollectionAssert.AreEqual(new[] { "a", "b" }, tokens.ToArray());
    }

    [TestMethod]
    public void Strip_SuffixInMiddleOfQuery_IsNotTreatedAsSuffix()
    {
        // ":a,b" must be the LAST whitespace-separated token to count as the suffix.
        var result = SearchQuerySortParser.Strip(":a,b readme", out var tokens);

        Assert.AreEqual(":a,b readme", result);
        Assert.IsEmpty(tokens);
    }

    [TestMethod]
    public void Strip_EmptyTokenInSuffix_IsRejectedAsNotASuffix()
    {
        // "readme :a,,c" has an empty token between the commas -- Strip.Any(p => p.Length == 0) rejects it.
        var result = SearchQuerySortParser.Strip("readme :a,,c", out var tokens);

        Assert.AreEqual("readme :a,,c", result);
        Assert.IsEmpty(tokens);
    }

    [TestMethod]
    public void Strip_BareColon_IsRejectedAsTooShort()
    {
        var result = SearchQuerySortParser.Strip("readme :", out var tokens);

        Assert.AreEqual("readme :", result);
        Assert.IsEmpty(tokens);
    }

    [TestMethod]
    public void Strip_CustomPrefixChar_ExtractsTokensAndTrimsQuery()
    {
        var result = SearchQuerySortParser.Strip("readme #a,b,c", out var tokens, '#');

        Assert.AreEqual("readme", result);
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, tokens.ToArray());
    }

    [TestMethod]
    public void StripExclusionBypass_LeadingAsterisk_IsStrippedAndFlagged()
    {
        var result = SearchQuerySortParser.StripExclusionBypass("*readme", out var bypass);

        Assert.AreEqual("readme", result);
        Assert.IsTrue(bypass);
    }

    [TestMethod]
    public void StripExclusionBypass_NoAsterisk_IsUnchanged()
    {
        var result = SearchQuerySortParser.StripExclusionBypass("readme", out var bypass);

        Assert.AreEqual("readme", result);
        Assert.IsFalse(bypass);
    }

    [TestMethod]
    public void StripExclusionBypass_EmptyQuery_DoesNotThrow()
    {
        var result = SearchQuerySortParser.StripExclusionBypass("", out var bypass);

        Assert.AreEqual("", result);
        Assert.IsFalse(bypass);
    }
}
