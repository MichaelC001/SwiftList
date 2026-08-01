using SwiftList.Core.Indexer.Usn;

namespace SwiftList.Core.Tests.Indexer.Usn;

// What a subscriber is allowed to conclude from this list. Getting it wrong in one direction wakes
// every plugin on the volume for a temp file; getting it wrong in the other loses a change a plugin
// was waiting for, which is the failure nobody would ever see reported as one.
[TestClass]
public sealed class DriveChangedDirectoriesTests
{
    [TestMethod]
    public void AFreshListCoversEverything()
    {
        // Nothing has fallen off yet, so a reader at the very beginning can trust it.
        Assert.IsTrue(new DriveChangedDirectories().Covers(0));
    }

    [TestMethod]
    public void OnlyTheDirectoriesAfterTheReadersOwnRevisionComeBack()
    {
        var changed = new DriveChangedDirectories();
        changed.Record(1, new[] { @"C:\One" });
        changed.Record(2, new[] { @"C:\Two" });
        changed.Record(3, new[] { @"C:\Three" });

        CollectionAssert.AreEqual(new[] { @"C:\Two", @"C:\Three" }, changed.DirectoriesAfter(1).ToList());
    }

    // The same list is broadcast to every subscriber, each at its own revision, so reading it must not
    // consume it.
    [TestMethod]
    public void ReadingTheListLeavesItForTheNextReader()
    {
        var changed = new DriveChangedDirectories();
        changed.Record(1, new[] { @"C:\One" });

        _ = changed.DirectoriesAfter(0).ToList();

        CollectionAssert.AreEqual(new[] { @"C:\One" }, changed.DirectoriesAfter(0).ToList());
    }

    [TestMethod]
    public void ABatchThatCouldNotBePinnedDownTakesEverythingOlderWithIt()
    {
        // Keeping the older entries would be worse than keeping none: they would read as the complete
        // set of places that changed, and the unknown batch's own directory would not be among them.
        var changed = new DriveChangedDirectories();
        changed.Record(1, new[] { @"C:\One" });

        changed.RecordUnknown(2);

        Assert.IsEmpty(changed.DirectoriesAfter(0).ToList());
        Assert.IsFalse(changed.Covers(1));
        Assert.IsTrue(changed.Covers(2));
    }

    [TestMethod]
    public void OverflowingDropsTheOldestAndSaysSo()
    {
        var changed = new DriveChangedDirectories();
        for (var revision = 1; revision <= DriveChangedDirectories.Capacity + 10; revision++)
            changed.Record(revision, new[] { $@"C:\Dir{revision}" });

        // A reader still back at the start can no longer be told where things changed...
        Assert.IsFalse(changed.Covers(0));
        // ...but one that kept up is unaffected.
        Assert.IsTrue(changed.Covers(DriveChangedDirectories.Capacity + 9));
    }

    // A revision with only some of its directories left would read as "these are the only places it
    // touched" and hide the rest, so a revision is dropped whole or not at all.
    [TestMethod]
    public void APartiallyDroppedRevisionIsDroppedOutright()
    {
        var changed = new DriveChangedDirectories();
        changed.Record(1, Enumerable.Range(0, 10).Select(i => $@"C:\First\{i}").ToList());
        changed.Record(2, Enumerable.Range(0, DriveChangedDirectories.Capacity).Select(i => $@"C:\Second\{i}").ToList());

        Assert.IsFalse(changed.Covers(0), "revision 1 was cut in half, so a reader before it has a gap");
        Assert.IsTrue(changed.Covers(1));
        Assert.IsEmpty(changed.DirectoriesAfter(1).Where(d => d.StartsWith(@"C:\First")).ToList());
    }

    // One batch wider than the whole budget: nothing of it can be kept, and nothing older survives to
    // be mistaken for the full picture either.
    [TestMethod]
    public void ASingleOversizedBatchLeavesNothingBehind()
    {
        var changed = new DriveChangedDirectories();
        changed.Record(1, new[] { @"C:\One" });
        changed.Record(2, Enumerable.Range(0, DriveChangedDirectories.Capacity + 1).Select(i => $@"C:\Big\{i}").ToList());

        Assert.IsFalse(changed.Covers(1));
    }

    [TestMethod]
    public void EmptyDirectoryNamesAreNotRecorded()
    {
        var changed = new DriveChangedDirectories();
        changed.Record(1, new[] { string.Empty, @"C:\Real" });

        CollectionAssert.AreEqual(new[] { @"C:\Real" }, changed.DirectoriesAfter(0).ToList());
    }

    [TestMethod]
    public void CloneDoesNotShareStateWithTheLiveList()
    {
        var live = new DriveChangedDirectories();
        live.Record(1, new[] { @"C:\One" });

        var clone = live.Clone();
        live.Record(2, new[] { @"C:\Two" });

        CollectionAssert.AreEqual(new[] { @"C:\One" }, clone.DirectoriesAfter(0).ToList());
    }
}
