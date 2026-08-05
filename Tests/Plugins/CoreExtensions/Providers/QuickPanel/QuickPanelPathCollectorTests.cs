using SwiftList.Plugins.CoreExtensions.Providers.QuickPanel;

namespace SwiftList.Plugins.CoreExtensions.Tests.Providers.QuickPanel;

[TestClass]
public sealed class QuickPanelPathCollectorTests
{
    [TestMethod]
    public void IsQuickPanelWindow_SwiftListQuickPanel_ReturnsTrue() =>
        Assert.IsTrue(QuickPanelPathCollector.IsQuickPanelWindow("HwndWrapper[SwiftList.App;;abc]", "SwiftList Quick Panel"));

    [TestMethod]
    public void IsQuickPanelWindow_OtherSwiftListWindow_ReturnsFalse() =>
        Assert.IsFalse(QuickPanelPathCollector.IsQuickPanelWindow("HwndWrapper[SwiftList.App;;abc]", "SwiftList Settings"));

    [TestMethod]
    public void IsQuickPanelWindow_NonWpfWindow_ReturnsFalse() =>
        Assert.IsFalse(QuickPanelPathCollector.IsQuickPanelWindow("CabinetWClass", "SwiftList Quick Panel"));

    [TestMethod]
    public void CanHandle_ClassAndTitle_RecognizesQuickPanel()
    {
        var collector = new QuickPanelPathCollector();

        Assert.IsTrue(collector.CanHandle("HwndWrapper[SwiftList.App;;abc]", "SwiftList Quick Panel"));
    }
}
