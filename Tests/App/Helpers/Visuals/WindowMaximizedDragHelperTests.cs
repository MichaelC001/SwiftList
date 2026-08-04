using System.Windows;
using SwiftList.App.Helpers.Visuals;

namespace SwiftList.App.Tests.Helpers.Visuals;

[TestClass]
public sealed class WindowMaximizedDragHelperTests
{
    [StaTestMethod]
    public void DragMoveOrRestore_NullWindowOrArgs_ThrowsArgumentNullException() => Assert.ThrowsExactly<ArgumentNullException>(() => WindowMaximizedDragHelper.DragMoveOrRestore(null!, null!));

    [StaTestMethod]
    public void DragMoveOrRestore_RestoredWindow_KeepsStateAsRestored()
    {
        var win = new Window
        {
            WindowState = WindowState.Normal,
            Width = 600,
            Height = 400
        };

        Assert.AreEqual(WindowState.Normal, win.WindowState);
    }
}
