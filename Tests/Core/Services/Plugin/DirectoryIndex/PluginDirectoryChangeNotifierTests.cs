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

    private static List<string> Affected(DriveChangedDirectories changed, long previousRevision, string source = "C")
        => PluginDirectoryChangeNotifier.PluginsForChange(source, changed, previousRevision, Registrations).ToList();

    // The bug this whole mechanism exists for: a revision alone says "C: moved", which is true of every
    // temp file write, and the Start Menu sits on C: like everything else does.
    [TestMethod]
    public void PluginsForChange_ChangeElsewhereOnTheSource_WakesNobody()
    {
        var changed = new DriveChangedDirectories();
        changed.Record(1, new[] { @"C:\Users\me\AppData\Local\Temp" });

        Assert.IsEmpty(Affected(changed, 0));
    }

    [TestMethod]
    public void PluginsForChange_ChangeInsideARegisteredDirectory_WakesThatPluginOnly()
    {
        var changed = new DriveChangedDirectories();
        changed.Record(1, new[] { @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs" });

        CollectionAssert.AreEqual(new[] { "CoreExtensions.StartMenu" }, Affected(changed, 0));
    }

    // Several directories under the same registration in one span is still one refresh.
    [TestMethod]
    public void PluginsForChange_ReportsEachPluginOnce()
    {
        var changed = new DriveChangedDirectories();
        changed.Record(1, new[] { @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs" });
        changed.Record(2, new[] { @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Tools" });

        CollectionAssert.AreEqual(new[] { "CoreExtensions.StartMenu" }, Affected(changed, 0));
    }

    // Only what happened since this subscriber's own revision: an older entry it already acted on must
    // not make it act again.
    [TestMethod]
    public void PluginsForChange_IgnoresDirectoriesItHasAlreadySeen()
    {
        var changed = new DriveChangedDirectories();
        changed.Record(1, new[] { @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs" });
        changed.Record(2, new[] { @"C:\Users\me\AppData\Local\Temp" });

        Assert.IsEmpty(Affected(changed, 1));
    }

    // A gap means "something happened and I cannot tell you where" -- a batch too wide to enumerate, or
    // a rescan that replaced a whole tree. Refreshing everything under the source is a wasted pass;
    // concluding nothing happened would lose the change.
    [TestMethod]
    public void PluginsForChange_WhenTheChangeListHasAGap_FallsBackToTheWholeSource()
    {
        var changed = new DriveChangedDirectories();
        changed.RecordUnknown(5);

        CollectionAssert.AreEquivalent(
            new[] { "CoreExtensions.StartMenu", "FileFilters" },
            Affected(changed, 0));
    }

    // A plugin registered ABOVE the directory that changed is affected too -- its listing includes it.
    [TestMethod]
    public void PluginsForChange_ChangeBelowARegisteredDirectory_StillWakesIt()
    {
        var changed = new DriveChangedDirectories();
        changed.Record(1, new[] { @"C:\Movies\2024\Summer" });

        CollectionAssert.AreEqual(new[] { "FileFilters" }, Affected(changed, 0));
    }

    // Same rule whatever index is behind it: a share, a WSL distro and a folder index all report through
    // the one path now, so a directory under a UNC root behaves exactly as one under a drive letter.
    [TestMethod]
    public void PluginsForChange_WorksTheSameForANetworkSource()
    {
        var registrations = new List<(string PluginId, string Path)>
        {
            ("Plugin.Share", @"\\nas\media\music"),
            ("Plugin.Elsewhere", @"\\nas\media\video"),
        };
        var changed = new DriveChangedDirectories();
        changed.Record(1, new[] { @"\\nas\media\music\albums" });

        CollectionAssert.AreEqual(
            new[] { "Plugin.Share" },
            PluginDirectoryChangeNotifier.PluginsForChange(@"\\nas\media", changed, 0, registrations).ToList());
    }
}
