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

}
