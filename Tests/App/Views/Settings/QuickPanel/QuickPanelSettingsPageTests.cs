using SwiftList.App.ViewModels.Settings.QuickPanel;
using SwiftList.Core;

namespace SwiftList.App.Tests.Views.Settings.QuickPanel;

// Constructing the page is the test. A StaticResource that does not exist compiles perfectly happily
// and only throws when the page is first built, which for a settings page nobody visits during a smoke
// run means shipping it -- this one shipped with a converter key that no dictionary in the app defines
// (BoolToVisibilityConverter, where the app's own key is BoolToVis) and the build said nothing.
[TestClass]
public sealed class QuickPanelSettingsPageTests
{
    [StaTestMethod]
    public void Page_BuildsWithEveryResourceItReferences()
    {
        var page = new SwiftList.App.Views.Settings.QuickPanel.QuickPanelSettingsPage
        {
            DataContext = new QuickPanelSettingsViewModel(new UserSettings())
        };

        Assert.IsNotNull(page.Content);
    }
}
