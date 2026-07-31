using System.Windows;
using System.Windows.Input;
using SwiftList.App;
using SwiftList.App.Services;
using SwiftList.App.ViewModels.QuickPanel;
using Native = SwiftList.App.Views.InlineSearchWindow.Helpers.InlineSearchWindowNativeMethods;

namespace SwiftList.App.Views.QuickPanel;

// Prototype window: the startup panel's tabs, docked to the bottom-right of whatever window is in
// front. Lifecycle, the F2 trigger and the docking maths all live in QuickPanelManager; this file is
// only the window's own input handling.
public partial class QuickPanelWindow : Window
{
    public QuickPanelWindow(QuickPanelViewModel viewModel)
    {
        InitializeComponent();
        // Both suppressed, as on the quick window: this panel is shown and hidden rather than opened
        // and closed, and the manager keeps the one instance, so letting Alt+F4 destroy it would leave
        // that reference pointing at a dead window with nothing to bring back.
        Helpers.Visuals.SystemMenuBlocker.Attach(this);
        DataContext = viewModel;

    }






    /// <summary>Takes the foreground and puts keyboard focus on the list.</summary>
    /// <remarks>
    /// The window is ShowActivated="False", so it comes up without focus and a plain Activate() from a
    /// thread that does not own the foreground is ignored by Windows. Attaching to the foreground
    /// thread's input queue for the duration is what makes the call land, the same approach and the
    /// same P/Invokes the inline search window uses to focus its own search box.
    /// </remarks>
    public bool ActivateAndFocus()
    {

        var foreground = Native.GetForegroundWindow();
        var currentThread = Native.GetCurrentThreadId();
        var foregroundThread = foreground != IntPtr.Zero
            ? Native.GetWindowThreadProcessId(foreground, out _)
            : 0;

        var attached = false;
        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
                attached = Native.AttachThreadInput(currentThread, foregroundThread, true);

            Activate();
            ItemsList.Focus();
            Keyboard.Focus(ItemsList);

            return IsActive;
        }
        finally
        {
            if (attached)
                Native.AttachThreadInput(currentThread, foregroundThread, false);
        }
    }


    // Escape does what losing the foreground does. Previewed rather than handled on the way up, since
    // the list has focus and would consume the key first.
    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        e.Handled = true;
        Services.QuickPanel.QuickPanelManager.Instance?.Hide();
    }

    private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The guard every other custom-chrome window here carries: DragMove throws if the button
            // has already been released by the time it runs.
        }
    }

    private void ItemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsList.SelectedItem is not AppSearchResult result) return;

        FileExecutor.OpenFileOrFolder(result.FullPath);
    }
}
