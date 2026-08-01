using SwiftList.App.ViewModels.QuickPanel;
using SwiftList.Core;

namespace SwiftList.App.Tests.ViewModels.QuickPanel;

// Covers the assembly the panel does between the settings and what is on screen: which workspace it
// opens on, which of that workspace's sources become groups, in what order, and what each one is
// called. The loading itself goes through the index and is handed in, so none of this touches one.
[TestClass]
public sealed class QuickPanelViewModelTests
{
    private static SearchResult Entry(string folder, string name, DateTime modified) => new()
    {
        Name = name,
        Path = System.IO.Path.Combine(folder, name),
        Metadata = new PluginSdk.Abstractions.FileMetadata(0, modified, modified, modified),
    };

    private static QuickPanelFolderSource Folder(string path, string id, QuickPanelSourceKind kind = QuickPanelSourceKind.RecentFiles)
        => new() { Id = id, Path = path, Kind = kind };

    // Every source answers with one entry named after its own folder, unless a case says otherwise --
    // enough to tell the groups apart without any of them being empty and dropped.
    private static Task<List<SearchResult>> OneEach(QuickPanelFolderSource source, CancellationToken token)
        => Task.FromResult(new List<SearchResult> { Entry(source.Path, "file.txt", new DateTime(2026, 1, 1)) });

    private static QuickPanelViewModel Build(
        QuickPanelSettings settings,
        Func<QuickPanelFolderSource, CancellationToken, Task<List<SearchResult>>>? load = null)
        => new(() => settings, load ?? OneEach, saveSettings: () => { });

    private static QuickPanelSettings OneWorkspace(params QuickPanelFolderSource[] folders)
    {
        var tab = new QuickPanelTab { Id = "w1", Name = "Work" };
        tab.Folders.AddRange(folders);
        return new QuickPanelSettings { Tabs = new List<QuickPanelTab> { tab }, ActiveTabId = tab.Id };
    }

    [TestMethod]
    public async Task Refresh_BuildsOneGroupPerVisibleSource()
    {
        var settings = OneWorkspace(Folder(@"C:\a", "s1"), Folder(@"C:\b", "s2"));
        var vm = Build(settings);

        await vm.RefreshAsync();

        CollectionAssert.AreEqual(new[] { "s1", "s2" }, vm.Groups.Select(g => g.SourceId).ToList());
        Assert.IsTrue(vm.HasContent);
        Assert.IsFalse(vm.IsEmpty);
    }

    [TestMethod]
    public async Task Refresh_FollowsTheStoredOrderAndSkipsHiddenSources()
    {
        var settings = OneWorkspace(Folder(@"C:\a", "s1"), Folder(@"C:\b", "s2"), Folder(@"C:\c", "s3"));
        var workspace = settings.Tabs[0];
        workspace.GroupOrder = new List<string> { "s3", "s1" };
        workspace.DisabledGroupIds = new List<string> { "s1" };

        var vm = Build(settings);
        await vm.RefreshAsync();

        // s3 leads because the order says so; s1 is hidden; s2 is unlisted and keeps its position after
        // everything that is listed.
        CollectionAssert.AreEqual(new[] { "s3", "s2" }, vm.Groups.Select(g => g.SourceId).ToList());
    }

    // A configured folder that currently has nothing costs a heading and a row of a panel that is a
    // quarter of the window it docks to, and says only what the settings page already does.
    [TestMethod]
    public async Task Refresh_SourceWithNothingInIt_GetsNoGroup()
    {
        var settings = OneWorkspace(Folder(@"C:\a", "s1"), Folder(@"C:\b", "s2"));
        var vm = Build(settings, (source, _) => Task.FromResult(source.Id == "s1"
            ? new List<SearchResult>()
            : new List<SearchResult> { Entry(source.Path, "file.txt", new DateTime(2026, 1, 1)) }));

        await vm.RefreshAsync();

        CollectionAssert.AreEqual(new[] { "s2" }, vm.Groups.Select(g => g.SourceId).ToList());
    }

    // One unreachable folder -- a disconnected drive, a permission change -- must not take the rest of
    // the workspace with it.
    [TestMethod]
    public async Task Refresh_SourceThatThrows_LosesOnlyItsOwnGroup()
    {
        var settings = OneWorkspace(Folder(@"C:\a", "s1"), Folder(@"C:\b", "s2"));
        var vm = Build(settings, (source, _) => source.Id == "s1"
            ? throw new UnauthorizedAccessException("no")
            : Task.FromResult(new List<SearchResult> { Entry(source.Path, "file.txt", new DateTime(2026, 1, 1)) }));

        await vm.RefreshAsync();

        CollectionAssert.AreEqual(new[] { "s2" }, vm.Groups.Select(g => g.SourceId).ToList());
    }

    [TestMethod]
    public async Task Refresh_EmptyWorkspace_SaysSoAndIsNotWorthOpening()
    {
        var settings = OneWorkspace(Folder(@"C:\a", "s1"));
        var vm = Build(settings, (_, _) => Task.FromResult(new List<SearchResult>()));

        await vm.RefreshAsync();

        Assert.IsTrue(vm.IsEmpty);
        Assert.IsFalse(vm.HasContent, "a single empty workspace has nothing the panel could show");
    }

    // The other tabs are one click away and only the panel can show them, so an empty selected tab is
    // still worth opening when there is somewhere else to go.
    [TestMethod]
    public async Task Refresh_EmptySelectedTab_StillOpensWhenThereAreOthers()
    {
        var settings = OneWorkspace(Folder(@"C:\a", "s1"));
        settings.Tabs.Add(new QuickPanelTab { Id = "w2", Name = "Other" });
        var vm = Build(settings, (_, _) => Task.FromResult(new List<SearchResult>()));

        await vm.RefreshAsync();

        Assert.IsTrue(vm.IsEmpty);
        Assert.IsTrue(vm.HasContent);
    }

    [TestMethod]
    public async Task Refresh_GroupTitle_IsTheFolderNameUntilItIsRenamed()
    {
        var settings = OneWorkspace(Folder(@"C:\Users\me\Downloads", "s1"), Folder(@"C:\b", "s2"));
        settings.Tabs[0].GroupPreferences["s2"] = new QuickPanelGroupPreference { DisplayName = "  素材  " };

        var vm = Build(settings);
        await vm.RefreshAsync();

        Assert.AreEqual("Downloads", vm.Groups[0].Title);
        Assert.AreEqual("素材", vm.Groups[1].Title);
    }

    // The kind IS the order choice, so a launcher folder must not come up newest-first.
    [TestMethod]
    public async Task Refresh_GroupOrder_ComesFromTheSourceKind()
    {
        var settings = OneWorkspace(
            Folder(@"C:\a", "s1", QuickPanelSourceKind.Launcher),
            Folder(@"C:\b", "s2", QuickPanelSourceKind.AllByModified));

        var vm = Build(settings, (source, _) => Task.FromResult(new List<SearchResult>
        {
            Entry(source.Path, "b.txt", new DateTime(2026, 1, 2)),
            Entry(source.Path, "a.txt", new DateTime(2026, 1, 3)),
        }));
        await vm.RefreshAsync();

        Assert.AreEqual(QuickPanelSortMode.NameAscending, vm.Groups[0].SortMode);
        Assert.AreEqual("a.txt", vm.Groups[0].Items[0].Name);
        Assert.AreEqual(QuickPanelSortMode.ModifiedDescending, vm.Groups[1].SortMode);
        Assert.AreEqual("a.txt", vm.Groups[1].Items[0].Name, "newest first, which this file also is");
    }

    [TestMethod]
    public async Task Refresh_StoredViewAndExpandedState_AreHonoured()
    {
        var settings = OneWorkspace(Folder(@"C:\a", "s1"));
        settings.Tabs[0].GroupPreferences["s1"] = new QuickPanelGroupPreference
        {
            ThumbnailView = false,
            Expanded = false,
        };

        var vm = Build(settings);
        await vm.RefreshAsync();

        Assert.IsFalse(vm.Groups[0].IsThumbnailView);
        Assert.IsFalse(vm.Groups[0].IsExpanded);
    }

    [TestMethod]
    public async Task Refresh_DisabledWorkspace_GetsNoTab()
    {
        var settings = OneWorkspace(Folder(@"C:\a", "s1"));
        settings.Tabs.Add(new QuickPanelTab { Id = "w2", Name = "Off", Enabled = false });

        var vm = Build(settings);
        await vm.RefreshAsync();

        CollectionAssert.AreEqual(new[] { "w1" }, vm.Tabs.Select(t => t.Id).ToList());
        Assert.IsFalse(vm.HasTabStrip, "a strip of one names the only thing there is");
    }

    [TestMethod]
    public async Task Refresh_TheAppInFront_PicksTheWorkspaceThatClaimsIt()
    {
        var settings = OneWorkspace(Folder(@"C:\a", "s1"));
        var claiming = new QuickPanelTab { Id = "w2", Name = "Editing", Processes = { "premiere" } };
        claiming.Folders.Add(Folder(@"C:\video", "s9"));
        settings.Tabs.Add(claiming);

        var vm = Build(settings);
        await vm.RefreshAsync("premiere.exe");

        Assert.AreEqual("w2", vm.Tabs.Single(t => t.IsSelected).Id);
        CollectionAssert.AreEqual(new[] { "s9" }, vm.Groups.Select(g => g.SourceId).ToList());
    }

    // A workspace with no tab must not be reachable by a process rule either.
    [TestMethod]
    public async Task Refresh_DisabledWorkspace_CannotClaimTheAppInFront()
    {
        var settings = OneWorkspace(Folder(@"C:\a", "s1"));
        settings.Tabs.Add(new QuickPanelTab { Id = "w2", Enabled = false, Processes = { "premiere" } });

        var vm = Build(settings);
        await vm.RefreshAsync("premiere.exe");

        Assert.AreEqual("w1", vm.Tabs.Single(t => t.IsSelected).Id);
    }

    [TestMethod]
    public async Task Refresh_NoAppClaimsIt_OpensOnTheRecordedTab()
    {
        var settings = OneWorkspace(Folder(@"C:\a", "s1"));
        settings.Tabs.Add(new QuickPanelTab { Id = "w2", Name = "Other" });
        settings.ActiveTabId = "w2";

        var vm = Build(settings);
        await vm.RefreshAsync("notepad.exe");

        Assert.AreEqual("w2", vm.Tabs.Single(t => t.IsSelected).Id);
    }

    // Where the user left the panel outranks the recorded tab, which is only the starting point.
    [TestMethod]
    public async Task Refresh_ReopensWhereTheUserLeftIt()
    {
        var settings = OneWorkspace(Folder(@"C:\a", "s1"));
        var second = new QuickPanelTab { Id = "w2", Name = "Other" };
        second.Folders.Add(Folder(@"C:\b", "s2"));
        settings.Tabs.Add(second);

        var vm = Build(settings);
        await vm.RefreshAsync();
        await vm.SelectTabAsync("w2");
        await vm.RefreshAsync();

        Assert.AreEqual("w2", vm.Tabs.Single(t => t.IsSelected).Id);
    }

    // ...but the app in front still outranks both: a workspace that names an app is a statement that
    // this app means these folders.
    [TestMethod]
    public async Task Refresh_TheAppInFront_OutranksWhereTheUserLeftIt()
    {
        var settings = OneWorkspace(Folder(@"C:\a", "s1"));
        settings.Tabs[0].Processes.Add("notepad");
        var second = new QuickPanelTab { Id = "w2", Name = "Other" };
        second.Folders.Add(Folder(@"C:\b", "s2"));
        settings.Tabs.Add(second);

        var vm = Build(settings);
        await vm.RefreshAsync();
        await vm.SelectTabAsync("w2");
        await vm.RefreshAsync("notepad.exe");

        Assert.AreEqual("w1", vm.Tabs.Single(t => t.IsSelected).Id);
    }

    [TestMethod]
    public async Task SelectTab_SwapsTheGroupsForTheOtherWorkspace()
    {
        var settings = OneWorkspace(Folder(@"C:\a", "s1"));
        var second = new QuickPanelTab { Id = "w2", Name = "Other" };
        second.Folders.Add(Folder(@"C:\b", "s2"));
        settings.Tabs.Add(second);

        var vm = Build(settings);
        await vm.RefreshAsync();
        await vm.SelectTabAsync("w2");

        CollectionAssert.AreEqual(new[] { "s2" }, vm.Groups.Select(g => g.SourceId).ToList());
        Assert.IsTrue(vm.Tabs.Single(t => t.Id == "w2").IsSelected);
        Assert.IsFalse(vm.Tabs.Single(t => t.Id == "w1").IsSelected);
        Assert.IsTrue(vm.HasTabStrip);
    }

    [TestMethod]
    public async Task SelectTab_AWorkspaceThatIsNotThere_ChangesNothing()
    {
        var settings = OneWorkspace(Folder(@"C:\a", "s1"));
        var vm = Build(settings);
        await vm.RefreshAsync();

        await vm.SelectTabAsync("nope");

        Assert.AreEqual("w1", vm.Tabs.Single(t => t.IsSelected).Id);
        CollectionAssert.AreEqual(new[] { "s1" }, vm.Groups.Select(g => g.SourceId).ToList());
    }

    // What the number keys reach: the strip's own positions, 1-based, and nothing outside it.
    [TestMethod]
    public async Task SelectTabAt_CountsFromOneAndIgnoresAnythingPastTheEnd()
    {
        var settings = OneWorkspace(Folder(@"C:\a", "s1"));
        settings.Tabs.Add(new QuickPanelTab { Id = "w2", Name = "Other" });

        var vm = Build(settings);
        await vm.RefreshAsync();

        await vm.SelectTabAtAsync(2);
        Assert.AreEqual("w2", vm.Tabs.Single(t => t.IsSelected).Id);

        await vm.SelectTabAtAsync(3);
        Assert.AreEqual("w2", vm.Tabs.Single(t => t.IsSelected).Id);

        await vm.SelectTabAtAsync(1);
        Assert.AreEqual("w1", vm.Tabs.Single(t => t.IsSelected).Id);
    }

    // What the panel leaves behind when it is hidden: nothing on screen, so a frame that slips in
    // before the next load lands cannot show the last workspace's files.
    [TestMethod]
    public async Task Clear_EmptiesEverythingThatIsOnScreen()
    {
        var settings = OneWorkspace(Folder(@"C:\a", "s1"));
        settings.Tabs.Add(new QuickPanelTab { Id = "w2", Name = "Other" });
        var vm = Build(settings);
        await vm.RefreshAsync();

        vm.Clear();

        Assert.IsEmpty(vm.Groups);
        Assert.IsEmpty(vm.Tabs);
        Assert.IsTrue(vm.IsEmpty);
        Assert.IsFalse(vm.HasContent);
        Assert.IsFalse(vm.HasTabStrip);
    }

    // The active workspace is the one piece of the panel's state that is not on screen, so clearing
    // must not take it: that is where the panel reopens.
    [TestMethod]
    public async Task Clear_KeepsWhereTheUserLeftThePanel()
    {
        var settings = OneWorkspace(Folder(@"C:\a", "s1"));
        var second = new QuickPanelTab { Id = "w2", Name = "Other" };
        second.Folders.Add(Folder(@"C:\b", "s2"));
        settings.Tabs.Add(second);

        var vm = Build(settings);
        await vm.RefreshAsync();
        await vm.SelectTabAsync("w2");
        vm.Clear();
        await vm.RefreshAsync();

        Assert.AreEqual("w2", vm.Tabs.Single(t => t.IsSelected).Id);
    }

    // The whole way round, because the two halves agree only if every id survives the trip: the settings
    // page edits a clone, files the new name under the source's id, and Save puts the clone back -- and
    // the panel then has to find that entry again by the same id off the live settings object.
    [TestMethod]
    public async Task RenamingASourceInSettings_ShowsUpAsTheGroupHeading()
    {
        var userSettings = new UserSettings();
        var workspace = new QuickPanelTab { Id = "w1" };
        workspace.Folders.Add(QuickPanelFolderSource.For(@"C:\Users\me\Desktop"));
        userSettings.QuickPanel.Tabs = new List<QuickPanelTab> { workspace };
        userSettings.QuickPanel.ActiveTabId = "w1";

        var page = new SwiftList.App.ViewModels.Settings.QuickPanel.QuickPanelSettingsViewModel(userSettings);
        page.Tabs.Single().Sources.Single().DisplayName = "11212";
        page.Save();

        var vm = new QuickPanelViewModel(() => userSettings.QuickPanel, OneEach, saveSettings: () => { });
        await vm.RefreshAsync();

        Assert.AreEqual("11212", vm.Groups.Single().Title);
    }

    [TestMethod]
    public async Task Refresh_NoWorkspacesAtAll_IsNotWorthOpening()
    {
        var vm = Build(new QuickPanelSettings { Tabs = new List<QuickPanelTab>() });

        await vm.RefreshAsync();

        Assert.IsEmpty(vm.Tabs);
        Assert.IsEmpty(vm.Groups);
        Assert.IsFalse(vm.HasContent);
    }
}
