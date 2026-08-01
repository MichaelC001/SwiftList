using SwiftList.App.ViewModels.Search.StartupPanel;

namespace SwiftList.App.Tests.ViewModels.Search.StartupPanel;

// What dragging a tab in the startup panel's strip stores as StartupPanel.TabOrder.
[TestClass]
public sealed class StartupPanelTabReorderTests
{
    [TestMethod]
    public void Apply_StoresTheStripsOwnOrder()
        => CollectionAssert.AreEqual(
            new[] { "c", "a", "b" },
            StartupPanelTabReorder.Apply(new[] { "c", "a", "b" }, new[] { "a", "b", "c" }));

    // A source that yielded nothing has no tab, so a drag could not reach it. Its id survives rather
    // than being dropped -- it just lands after everything that was on screen to be arranged.
    [TestMethod]
    public void Apply_KeepsIdsWithNoTabThisTimeRound()
        => CollectionAssert.AreEqual(
            new[] { "b", "a", "history", "favorites" },
            StartupPanelTabReorder.Apply(new[] { "b", "a" }, new[] { "history", "a", "favorites", "b" }));

    [TestMethod]
    public void Apply_NoStoredOrderYet_IsJustTheStrip()
        => CollectionAssert.AreEqual(
            new[] { "b", "a" },
            StartupPanelTabReorder.Apply(new[] { "b", "a" }, null));

    // Built-in sources carry synthetic ids and a plugin tab carries its component id, but nothing
    // guarantees a source has one at all -- an unnamed tab must not become an empty entry that then
    // matches the next unnamed one.
    [TestMethod]
    public void Apply_IgnoresEmptyIds()
        => CollectionAssert.AreEqual(
            new[] { "a" },
            StartupPanelTabReorder.Apply(new[] { "", "a", "" }, new[] { "" }));

    [TestMethod]
    public void Apply_NeverRepeatsAnId()
        => CollectionAssert.AreEqual(
            new[] { "a", "b" },
            StartupPanelTabReorder.Apply(new[] { "a", "a" }, new[] { "b", "b", "a" }));
}
