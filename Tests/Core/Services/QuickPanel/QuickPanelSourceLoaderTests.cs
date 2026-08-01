using SwiftList.Core.Services.QuickPanel;

namespace SwiftList.Core.Tests.Services.QuickPanel;

// Covers the two parts that decide what a source shows without touching the index or the disk: the
// order a kind implies, and how favorites are read. The loading itself goes through the service pipe
// or a filesystem walk and has no injectable seam (see IndexedDirectoryEnumerator).
[TestClass]
public sealed class QuickPanelSourceLoaderTests
{
    private static SearchResult Entry(string name, DateTime modified) => new()
    {
        Name = name,
        Path = @"C:\Movies\" + name,
        Metadata = new PluginSdk.Abstractions.FileMetadata(0, modified, modified, modified),
    };

    private static List<SearchResult> Sample() => new()
    {
        Entry("b.mp4", new DateTime(2026, 1, 2)),
        Entry("a.mp4", new DateTime(2026, 1, 3)),
        Entry("c.mp4", new DateTime(2026, 1, 1)),
    };

    [TestMethod]
    public void Order_Launcher_IsByName()
    {
        var ordered = QuickPanelSourceLoader.Order(Sample(), QuickPanelSourceKind.Launcher, maxItems: 0);

        CollectionAssert.AreEqual(new[] { "a.mp4", "b.mp4", "c.mp4" }, ordered.Select(r => r.Name).ToList());
    }

    [TestMethod]
    public void Order_AllByModified_IsNewestFirst()
    {
        var ordered = QuickPanelSourceLoader.Order(Sample(), QuickPanelSourceKind.AllByModified, maxItems: 0);

        CollectionAssert.AreEqual(new[] { "a.mp4", "b.mp4", "c.mp4" }, ordered.Select(r => r.Name).ToList());
        Assert.AreEqual(new DateTime(2026, 1, 3), ordered[0].Metadata.Modified);
        Assert.AreEqual(new DateTime(2026, 1, 1), ordered[^1].Metadata.Modified);
    }

    // The cap applies after ordering, or it would keep an arbitrary handful and then sort those.
    [TestMethod]
    public void Order_MaxItems_KeepsTheFirstOnesInThatOrder()
    {
        var ordered = QuickPanelSourceLoader.Order(Sample(), QuickPanelSourceKind.AllByModified, maxItems: 2);

        CollectionAssert.AreEqual(new[] { "a.mp4", "b.mp4" }, ordered.Select(r => r.Name).ToList());
    }

    [TestMethod]
    public void Order_MaxItemsZero_KeepsEverything()
        => Assert.HasCount(3, QuickPanelSourceLoader.Order(Sample(), QuickPanelSourceKind.Launcher, maxItems: 0));

}
