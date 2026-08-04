using System.Windows;
using SwiftList.App.Helpers.Visuals;

namespace SwiftList.App.Tests.Helpers.Visuals;

[TestClass]
public sealed class MaximizeBoundsHelperTests
{
    [StaTestMethod]
    public void Attach_NullWindow_ThrowsArgumentNullException() => Assert.ThrowsExactly<ArgumentNullException>(() => MaximizeBoundsHelper.Attach(null!));

    [StaTestMethod]
    public void Attach_UnloadedWindow_AttachesOnLoaded()
    {
        var win = new Window();
        // Should attach event listener safely without throwing null reference exceptions
        MaximizeBoundsHelper.Attach(win);
    }
}
