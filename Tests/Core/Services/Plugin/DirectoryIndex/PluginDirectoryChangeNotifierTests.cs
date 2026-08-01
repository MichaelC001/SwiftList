using SwiftList.Core.Indexer.Usn;
using SwiftList.Core.Services.Plugin.DirectoryIndex;

namespace SwiftList.Core.Tests.Services.Plugin.DirectoryIndex;

// Only the matching is covered: the rest of the notifier is a debounce timer plus two live index
// subscriptions (a named pipe and a process-wide event), none of which has an injectable seam. Getting
// the matching wrong is the part that would be invisible -- a plugin silently never refreshing, or
// every plugin re-listing everything whenever any drive ticks.
[TestClass]
public sealed class PluginDirectoryChangeNotifierTests
{
    [TestMethod]
    public void SourceTouchesPath_LocalDriveLetter_MatchesOnlyThatDrive()
    {
        Assert.IsTrue(PluginDirectoryChangeNotifier.SourceTouchesPath("C", @"C:\Movies"));
        Assert.IsTrue(PluginDirectoryChangeNotifier.SourceTouchesPath("c", @"C:\"));
        Assert.IsFalse(PluginDirectoryChangeNotifier.SourceTouchesPath("C", @"D:\Movies"));
    }

    [TestMethod]
    public void SourceTouchesPath_WslAndFolderIndexRoots_MatchDirectoriesInsideThem()
    {
        Assert.IsTrue(PluginDirectoryChangeNotifier.SourceTouchesPath(@"\\wsl$\Ubuntu", @"\\wsl$\Ubuntu\home\me\bin"));
        Assert.IsTrue(PluginDirectoryChangeNotifier.SourceTouchesPath(@"D:\Projects\ProjectA", @"D:\Projects\ProjectA\src"));
        Assert.IsFalse(PluginDirectoryChangeNotifier.SourceTouchesPath(@"\\wsl$\Ubuntu", @"\\wsl$\Debian\home"));
    }

    // A plugin watching a parent of the changed source has to hear about it too: its listing includes
    // everything under that source.
    [TestMethod]
    public void SourceTouchesPath_RegisteredDirectoryContainingTheSource_AlsoMatches()
    {
        Assert.IsTrue(PluginDirectoryChangeNotifier.SourceTouchesPath(@"D:\Projects\ProjectA", @"D:\Projects"));
        Assert.IsTrue(PluginDirectoryChangeNotifier.SourceTouchesPath("D", @"D:\"));
    }

    // Prefix comparison on whole segments only, or a change in one folder index would refresh plugins
    // pointed at an unrelated sibling that happens to share its opening characters.
    [TestMethod]
    public void SourceTouchesPath_SiblingWithTheSamePrefix_DoesNotMatch()
    {
        Assert.IsFalse(PluginDirectoryChangeNotifier.SourceTouchesPath(@"D:\Projects\ProjectA", @"D:\Projects\ProjectAB"));
        Assert.IsFalse(PluginDirectoryChangeNotifier.SourceTouchesPath(@"D:\Projects\ProjectAB", @"D:\Projects\ProjectA"));
    }

    [TestMethod]
    public void SourceTouchesPath_MissingSourceOrPath_DoesNotMatch()
    {
        Assert.IsFalse(PluginDirectoryChangeNotifier.SourceTouchesPath("", @"C:\Movies"));
        Assert.IsFalse(PluginDirectoryChangeNotifier.SourceTouchesPath("C", ""));
    }

    [TestMethod]
    public void PluginsUnderSource_ReturnsEachAffectedPluginOnce()
    {
        var registrations = new List<(string PluginId, string Path)>
        {
            ("FileFilters", @"C:\Movies"),
            ("FileFilters", @"C:\Downloads"),
            ("FileFilters", @"D:\Archive"),
            ("Other", @"D:\Archive"),
        };

        var affected = PluginDirectoryChangeNotifier.PluginsUnderSource("C", registrations).ToList();

        // Twice under C:, but one refresh is one refresh.
        CollectionAssert.AreEqual(new[] { "FileFilters" }, affected);
    }

    [TestMethod]
    public void PluginsUnderSource_SourceNobodyRegisteredUnder_ReturnsNothing()
    {
        var registrations = new List<(string PluginId, string Path)> { ("FileFilters", @"C:\Movies") };

        Assert.IsEmpty(PluginDirectoryChangeNotifier.PluginsUnderSource("Z", registrations).ToList());
    }

    private static readonly List<(string PluginId, string Path)> Registrations = new()
    {
        ("CoreExtensions.StartMenu", @"C:\ProgramData\Microsoft\Windows\Start Menu"),
        ("FileFilters", @"C:\Movies"),
    };

    private static UsnIndexer.DriveIndexStatus Drive(long revision, DriveChangedDirectories changed)
        => new() { Drive = "C", Revision = revision, ChangedDirectories = changed };

    // The bug this whole mechanism exists for: a drive revision alone says "C: moved", which is true of
    // every temp file write, and the Start Menu sits on C: like everything else does.
    [TestMethod]
    public void PluginsForLocalChange_ChangeElsewhereOnTheDrive_WakesNobody()
    {
        var changed = new DriveChangedDirectories();
        changed.Record(1, new[] { @"C:\Users\me\AppData\Local\Temp" });

        Assert.IsEmpty(PluginDirectoryChangeNotifier.PluginsForLocalChange(Drive(1, changed), 0, Registrations).ToList());
    }

    [TestMethod]
    public void PluginsForLocalChange_ChangeInsideARegisteredDirectory_WakesThatPluginOnly()
    {
        var changed = new DriveChangedDirectories();
        changed.Record(1, new[] { @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs" });

        CollectionAssert.AreEqual(
            new[] { "CoreExtensions.StartMenu" },
            PluginDirectoryChangeNotifier.PluginsForLocalChange(Drive(1, changed), 0, Registrations).ToList());
    }

    // Several directories under the same registration in one span is still one refresh.
    [TestMethod]
    public void PluginsForLocalChange_ReportsEachPluginOnce()
    {
        var changed = new DriveChangedDirectories();
        changed.Record(1, new[] { @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs" });
        changed.Record(2, new[] { @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Tools" });

        CollectionAssert.AreEqual(
            new[] { "CoreExtensions.StartMenu" },
            PluginDirectoryChangeNotifier.PluginsForLocalChange(Drive(2, changed), 0, Registrations).ToList());
    }

    // Only what happened since this subscriber's own revision: an older entry it already acted on must
    // not make it act again.
    [TestMethod]
    public void PluginsForLocalChange_IgnoresDirectoriesItHasAlreadySeen()
    {
        var changed = new DriveChangedDirectories();
        changed.Record(1, new[] { @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs" });
        changed.Record(2, new[] { @"C:\Users\me\AppData\Local\Temp" });

        Assert.IsEmpty(PluginDirectoryChangeNotifier.PluginsForLocalChange(Drive(2, changed), 1, Registrations).ToList());
    }

    // A gap means "something happened and I cannot tell you where". Refreshing everything under the
    // drive is a wasted pass; concluding nothing happened would lose the change.
    [TestMethod]
    public void PluginsForLocalChange_WhenTheChangeListHasAGap_FallsBackToTheWholeDrive()
    {
        var changed = new DriveChangedDirectories();
        changed.RecordUnknown(5);

        CollectionAssert.AreEquivalent(
            new[] { "CoreExtensions.StartMenu", "FileFilters" },
            PluginDirectoryChangeNotifier.PluginsForLocalChange(Drive(5, changed), 0, Registrations).ToList());
    }

    // A plugin registered ABOVE the directory that changed is affected too -- its listing includes it.
    [TestMethod]
    public void PluginsForLocalChange_ChangeBelowARegisteredDirectory_StillWakesIt()
    {
        var changed = new DriveChangedDirectories();
        changed.Record(1, new[] { @"C:\Movies\2024\Summer" });

        CollectionAssert.AreEqual(
            new[] { "FileFilters" },
            PluginDirectoryChangeNotifier.PluginsForLocalChange(Drive(1, changed), 0, Registrations).ToList());
    }
}
