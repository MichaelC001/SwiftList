using SwiftList.App.ViewModels.Settings.QuickPanel;
using SwiftList.Core;

namespace SwiftList.App.Tests.ViewModels.Settings.QuickPanel;

// What a folder looks like the moment it is added. Split from QuickPanelSettingsViewModelTests only to
// keep that file under the repo's per-file line limit.
[TestClass]
public sealed class QuickPanelAddSourceTests
{
    private static UserSettings BuildSettings(string existingFolder)
    {
        var tab = new QuickPanelTab { Id = "tab1" };
        tab.Folders.Add(QuickPanelFolderSource.For(existingFolder));

        var settings = new UserSettings();
        settings.QuickPanel.Tabs = new List<QuickPanelTab> { tab };
        settings.QuickPanel.ActiveTabId = tab.Id;
        return settings;
    }

    // A folder picked by hand is nearly always a place things are kept rather than a place they arrive,
    // so it starts showing everything by name. Recently-changed-files is one dropdown away, and is a bad
    // thing to assume: it can leave a folder full of files showing nothing at all when none of them has
    // been touched lately.
    [TestMethod]
    public void AddedFolder_StartsAsEverythingByName()
    {
        var settings = BuildSettings(@"C:\a");
        var vm = new QuickPanelSettingsViewModel(settings);
        var tab = vm.Tabs.Single();

        tab.AddFolders(new[] { @"C:\projects" });

        Assert.AreEqual(QuickPanelSourceKind.Launcher, tab.Sources.Single(s => s.Path == @"C:\projects").Kind);

        vm.Save();
        Assert.AreEqual(
            QuickPanelSourceKind.Launcher,
            settings.QuickPanel.Tabs.Single().Folders.Single(f => f.Path == @"C:\projects").Kind);
    }

    // The fresh-install workspace is deliberately not this: Desktop, Downloads and Documents are
    // recent-files there, being places things arrive rather than places things are kept.
    [TestMethod]
    public void TheDefaultWorkspaceStillOpensOnRecentFiles()
        => Assert.IsTrue(QuickPanelTab.CreateDefault().Folders.All(f => f.Kind == QuickPanelSourceKind.RecentFiles));

    [TestMethod]
    public void AddFolders_SkipsOneTheWorkspaceAlreadyHas()
    {
        var settings = BuildSettings(@"C:\a");
        var vm = new QuickPanelSettingsViewModel(settings);
        var tab = vm.Tabs.Single();

        tab.AddFolders(new[] { @"C:\A", @"C:\projects", @"C:\projects" });

        Assert.HasCount(2, tab.Sources);
    }
}
