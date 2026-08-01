using SwiftList.Core.Services.Plugin.DirectoryIndex;

namespace SwiftList.Core.Tests.Services.Plugin.DirectoryIndex;

[TestClass]
public sealed class FilterPatternHelperTests
{
    [TestMethod]
    public void Split_EmptyOrWhitespace_DefaultsToMatchAll()
    {
        CollectionAssert.AreEqual(new[] { "*" }, FilterPatternHelper.Split(""));
        CollectionAssert.AreEqual(new[] { "*" }, FilterPatternHelper.Split("   "));
    }

    [TestMethod]
    public void Split_SinglePattern_ReturnsThatPattern() => CollectionAssert.AreEqual(new[] { "*.lnk" }, FilterPatternHelper.Split("*.lnk"));

    [TestMethod]
    public void Split_MixedSeparatorsAndWhitespace_TrimsEachEntry() => CollectionAssert.AreEqual(new[] { "*.exe", "*.lnk", "*.bat" }, FilterPatternHelper.Split(" *.exe; *.lnk , *.bat "));

    // null is the "don't filter at all" signal a subtree walk checks once instead of running a
    // wildcard match per entry that could only ever return true.
    [TestMethod]
    public void SplitOrNullIfMatchAll_MatchAllPatterns_ReturnNull()
    {
        Assert.IsNull(FilterPatternHelper.SplitOrNullIfMatchAll("*"));
        Assert.IsNull(FilterPatternHelper.SplitOrNullIfMatchAll("*.*"));
        Assert.IsNull(FilterPatternHelper.SplitOrNullIfMatchAll(""));
        Assert.IsNull(FilterPatternHelper.SplitOrNullIfMatchAll(null));
        // One match-all entry makes the whole list match-all, whatever else it lists.
        Assert.IsNull(FilterPatternHelper.SplitOrNullIfMatchAll("*.exe;*"));
    }

    [TestMethod]
    public void SplitOrNullIfMatchAll_RealPatterns_ReturnThem() => CollectionAssert.AreEqual(new[] { "*.exe", "*.lnk" }, FilterPatternHelper.SplitOrNullIfMatchAll("*.exe;*.lnk"));

    [TestMethod]
    public void Matches_ExtensionPattern_IsCaseInsensitiveAndAnchoredToTheExtension()
    {
        var patterns = new[] { "*.exe" };

        Assert.IsTrue(FilterPatternHelper.Matches("Setup.EXE", patterns));
        Assert.IsFalse(FilterPatternHelper.Matches("setup.exe.txt", patterns));
        Assert.IsFalse(FilterPatternHelper.Matches("exe", patterns));
    }

    [TestMethod]
    public void Matches_AnyPatternInTheList_IsEnough()
    {
        var patterns = new[] { "*.exe", "note?.txt" };

        Assert.IsTrue(FilterPatternHelper.Matches("app.exe", patterns));
        Assert.IsTrue(FilterPatternHelper.Matches("note1.txt", patterns));
        Assert.IsFalse(FilterPatternHelper.Matches("notes12.txt", patterns));
    }

    // "*.*" is the DOS spelling of "everything", which is how Directory.EnumerateFiles reads it --
    // taken literally as a Win32 expression it would drop every extension-less name instead.
    [TestMethod]
    public void Matches_DosMatchAllPattern_MatchesNamesWithoutADot() => Assert.IsTrue(FilterPatternHelper.Matches("Makefile", new[] { "*.*" }));
}
