using SwiftList.App.ViewModels.Settings.QuickPanel;
using SwiftList.Core;

namespace SwiftList.App.Tests.ViewModels.Settings.QuickPanel;

// Covers the staging both view models do: what the source list on screen turns into in the settings
// file. AddFolder is the one command left out -- it opens a folder picker and has nothing else in it.
[TestClass]
public sealed class QuickPanelSettingsViewModelTests
{
    // QuickPanelSettings ships with a default tab; these tests replace it with one holding exactly the
    // folders each case needs.
    private static UserSettings BuildSettings(params string[] folderPaths)
    {
        var tab = new QuickPanelTab { Id = "tab1" };
        foreach (var path in folderPaths)
            tab.Folders.Add(QuickPanelFolderSource.For(path));

        var settings = new UserSettings();
        settings.QuickPanel.Tabs = new List<QuickPanelTab> { tab };
        settings.QuickPanel.ActiveTabId = tab.Id;
        return settings;
    }

    [TestMethod]
    public void Save_WritesTheListOrderAndTheHiddenRows()
    {
        var settings = BuildSettings(@"C:\a", @"C:\b");
        var vm = new QuickPanelSettingsViewModel(settings);
        var tab = vm.Tabs.Single();
        var first = tab.Sources[0];
        tab.Sources[1].IsVisible = false;
        tab.MoveDownCommand.Execute(first);

        vm.Save();

        var saved = settings.QuickPanel.Tabs.Single();
        // The row that moved down is no longer first, and the order stored is the order on screen.
        Assert.AreNotEqual(first.Id, saved.GroupOrder[0]);
        CollectionAssert.AreEqual(tab.Sources.Select(s => s.Id).ToList(), saved.GroupOrder);
        CollectionAssert.AreEqual(new[] { tab.Sources.First(s => !s.IsVisible).Id }, saved.DisabledGroupIds);
    }

    [TestMethod]
    public void Save_CustomName_IsStoredOnlyWhenThereIsOne()
    {
        var settings = BuildSettings(@"C:\a", @"C:\b");
        var vm = new QuickPanelSettingsViewModel(settings);
        var tab = vm.Tabs.Single();
        var named = tab.Sources[0];
        named.DisplayName = "  素材  ";

        vm.Save();

        var saved = settings.QuickPanel.Tabs.Single();
        Assert.AreEqual("素材", saved.GroupPreferences[named.Id].DisplayName);
        Assert.IsFalse(saved.GroupPreferences.ContainsKey(tab.Sources[1].Id), "an untouched row needs no entry");
    }

    [TestMethod]
    public void Save_RemovedFolder_LeavesNothingBehind()
    {
        var settings = BuildSettings(@"C:\a", @"C:\b");
        var vm = new QuickPanelSettingsViewModel(settings);
        var tab = vm.Tabs.Single();
        var doomed = tab.Sources[0];
        doomed.DisplayName = "gone";
        tab.RemoveSourceCommand.Execute(doomed);

        vm.Save();

        var saved = settings.QuickPanel.Tabs.Single();
        Assert.IsFalse(saved.Folders.Any(f => f.Id == doomed.Id));
        CollectionAssert.DoesNotContain(saved.GroupOrder, doomed.Id);
        Assert.IsFalse(saved.GroupPreferences.ContainsKey(doomed.Id), "a deleted source must not keep accumulating");
    }

    // Built-in sources can be hidden but not deleted: there is no way to add one back.
    [TestMethod]
    public void RemoveSource_BuiltInRow_IsIgnored()
    {
        var settings = BuildSettings(@"C:\a");
        var vm = new QuickPanelSettingsViewModel(settings);
        var tab = vm.Tabs.Single();
        var builtIn = tab.Sources.First(s => !s.IsFolderSource);

        tab.RemoveSourceCommand.Execute(builtIn);

        CollectionAssert.Contains(tab.Sources.ToList(), builtIn);
    }

    [TestMethod]
    public void Save_FolderFields_RoundTrip()
    {
        var settings = BuildSettings(@"C:\a");
        var vm = new QuickPanelSettingsViewModel(settings);
        var row = vm.Tabs.Single().Sources.First(s => s.IsFolderSource);
        row.Kind = QuickPanelSourceKind.Launcher;
        row.Recursive = true;
        row.FilterPattern = " *.exe;*.lnk ";
        row.MaxItems = 50;

        vm.Save();

        var folder = settings.QuickPanel.Tabs.Single().Folders.Single();
        Assert.AreEqual(QuickPanelSourceKind.Launcher, folder.Kind);
        Assert.IsTrue(folder.Recursive);
        Assert.AreEqual("*.exe;*.lnk", folder.FilterPattern);
        Assert.AreEqual(50, folder.MaxItems);
    }

    // The clone gets its own folder ids, so everything that addresses a source by id has to be rewritten
    // with it -- otherwise the copy's order and hidden list would point at the original's sources.
    [TestMethod]
    public void DuplicateTab_RemapsEverythingThatAddressesASourceById()
    {
        var settings = BuildSettings(@"C:\a", @"C:\b");
        var vm = new QuickPanelSettingsViewModel(settings);
        var original = vm.Tabs.Single();
        original.Sources[0].DisplayName = "素材";
        original.Sources[1].IsVisible = false;

        vm.DuplicateTabCommand.Execute(null);
        vm.Save();

        var copy = settings.QuickPanel.Tabs.Last();
        var originalIds = settings.QuickPanel.Tabs.First().Folders.Select(f => f.Id).ToHashSet();
        Assert.HasCount(2, copy.Folders);
        Assert.IsFalse(copy.Folders.Any(f => originalIds.Contains(f.Id)), "a clone must not reuse ids");
        Assert.IsFalse(copy.GroupOrder.Any(originalIds.Contains), "order still points at the original's sources");
        Assert.IsFalse(copy.DisabledGroupIds.Any(originalIds.Contains), "hidden list still points at the original's sources");
        Assert.IsTrue(copy.GroupPreferences.Values.Any(p => p.DisplayName == "素材"));
        Assert.IsFalse(copy.GroupPreferences.Keys.Any(originalIds.Contains));
    }

    [TestMethod]
    public void RemoveTab_LastRemainingTab_IsKept()
    {
        var settings = BuildSettings(@"C:\a");
        var vm = new QuickPanelSettingsViewModel(settings);

        vm.RemoveTabCommand.Execute(null);

        Assert.HasCount(1, vm.Tabs);
    }
}
