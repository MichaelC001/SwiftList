using SwiftList.App.ViewModels.Settings.QuickPanel;
using SwiftList.Core;

namespace SwiftList.App.Tests.ViewModels.Settings.QuickPanel;

// The half of the source page that plugin-provided sources brought with them. Split from
// QuickPanelSettingsViewModelTests only to keep that file under the repo's per-file line limit; the
// folder-only cases stayed there.
[TestClass]
public sealed class QuickPanelPluginSourceSettingsTests
{
    private static UserSettings BuildSettings(string folderPath, params string[] pluginSourceIds)
    {
        var tab = new QuickPanelTab { Id = "tab1" };
        tab.Folders.Add(QuickPanelFolderSource.For(folderPath));
        foreach (var id in pluginSourceIds)
            tab.PluginSourceIds.Add(id);

        var settings = new UserSettings();
        settings.QuickPanel.Tabs = new List<QuickPanelTab> { tab };
        settings.QuickPanel.ActiveTabId = tab.Id;
        return settings;
    }

    // Folders and plugin sources share one list and one order on screen, and are split back into their
    // two settings lists on save, so a plugin source dragged above a folder has to survive the trip.
    [TestMethod]
    public void Save_PluginSourceAndFolder_KeepOneSharedOrder()
    {
        var settings = BuildSettings(@"C:\a", "Demo::QuickPanelSourceProvider::Recent");

        var vm = new QuickPanelSettingsViewModel(settings);
        var tab = vm.Tabs.Single();
        Assert.HasCount(2, tab.Sources);

        var plugin = tab.Sources.Single(s => !s.IsFolderSource);
        tab.MoveUpCommand.Execute(plugin);
        vm.Save();

        var saved = settings.QuickPanel.Tabs.Single();
        Assert.AreEqual("Demo::QuickPanelSourceProvider::Recent", saved.GroupOrder[0], "the shared order leads with it");
        CollectionAssert.AreEqual(new[] { "Demo::QuickPanelSourceProvider::Recent" }, saved.PluginSourceIds);
        Assert.HasCount(1, saved.Folders, "and the folder is still a folder");
    }

    // A stored id whose plugin is gone keeps its place rather than being pruned: same rule the group
    // order follows, so a plugin switched off for a week comes back where the user put it.
    [TestMethod]
    public void Save_PluginSourceWhoseProviderIsMissing_IsKept()
    {
        var settings = BuildSettings(@"C:\a", "Gone::QuickPanelSourceProvider::Nothing");

        var vm = new QuickPanelSettingsViewModel(settings);
        vm.Save();

        CollectionAssert.AreEqual(
            new[] { "Gone::QuickPanelSourceProvider::Nothing" },
            settings.QuickPanel.Tabs.Single().PluginSourceIds);
    }
}
