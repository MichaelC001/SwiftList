using System.Windows;
using System.Windows.Input;

namespace SwiftList.App.Helpers.Visuals;

/// <summary>
/// Helper providing drag-restore behavior for custom window chromes,
/// allowing maximized windows to be un-maximized and repositioned seamlessly while dragging.
/// ponytail: Extracted to decouple window drag restoration logic across custom chrome windows and maintain line limits.
/// </summary>
public static class WindowMaximizedDragHelper
{
    /// <summary>
    /// Drag-moves the specified window when restored, or restores and seamlessly drags it when maximized.
    /// </summary>
    public static void DragMoveOrRestore(Window window, MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(e);

        if (e.LeftButton != MouseButtonState.Pressed || e.ClickCount >= 2)
        {
            return;
        }

        if (window.WindowState == WindowState.Maximized)
        {
            var mousePos = e.GetPosition(window);
            var screenPoint = window.PointToScreen(mousePos);
            var percentX = window.ActualWidth > 0 ? mousePos.X / window.ActualWidth : 0.5;

            window.WindowState = WindowState.Normal;

            var targetWidth = window.ActualWidth > 0 ? window.ActualWidth : window.RestoreBounds.Width;
            if (targetWidth <= 0) targetWidth = 800;

            var newLeft = screenPoint.X - (targetWidth * percentX);
            var newTop = screenPoint.Y - mousePos.Y;

            window.Left = newLeft;
            window.Top = newTop;
        }

        try
        {
            window.DragMove();
        }
        catch (InvalidOperationException)
        {
            // Ignore standard DragMove state exceptions when primary mouse button is released
        }
    }
}
