using System.Windows;
using System.Windows.Input;
using SwiftList.App;
using SwiftList.App.Services;
using SwiftList.App.Services.ShellMenu.ActionFlyout;
using SwiftList.App.ViewModels.QuickPanel;
using Native = SwiftList.App.Views.InlineSearchWindow.Helpers.InlineSearchWindowNativeMethods;

namespace SwiftList.App.Views.QuickPanel;

// Prototype window: the startup panel's tabs, docked to the bottom-right of whatever window is in
// front. Lifecycle, the F2 trigger and the docking maths all live in QuickPanelManager; this file is
// only the window's own input handling.
public partial class QuickPanelWindow : Window, SwiftList.PluginSdk.Abstractions.IPluginSearchWindow
{
    // The four methods ActionFlyout needs from whoever hosts it. Deliberately IPluginSearchWindow and
    // not ISearchWindow: that larger interface is built around an in-window actions pane, which is what
    // ShellMenuPresenter drives and what this panel has no equivalent of. The flyout asks only for these.
    public void LocateInExplorerExternal(string path) => FileExecutor.LocateInExplorer(path);
    public void OpenFileOrFolderExternal(string path) => FileExecutor.OpenFileOrFolder(path);
    public void OpenFileOrFolderAsAdminExternal(string path) => FileExecutor.OpenFileOrFolderAsAdmin(path);
    public void HideWindow() => Services.QuickPanel.QuickPanelManager.Instance?.Hide();

    public QuickPanelWindow(QuickPanelViewModel viewModel)
    {
        InitializeComponent();
        // Both suppressed, as on the quick window: this panel is shown and hidden rather than opened
        // and closed, and the manager keeps the one instance, so letting Alt+F4 destroy it would leave
        // that reference pointing at a dead window with nothing to bring back.
        Helpers.Visuals.SystemMenuBlocker.Attach(this);
        DataContext = viewModel;

        // Drag out to Explorer and friends, the one line ResultsControl uses for the same lists.
        Views.Controls.Results.ResultsDragDropHelper.Register(ItemsList);


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
        if (e.Key != Key.Escape)
        {
            TryRunActionHotkey(e);
            return;
        }


        // The flyout closes on its own Escape and hangs its handler on this window too. Letting this
        // one act as well would dismiss the panel out from under a menu the user was only closing.
        if (Services.ShellMenu.ActionFlyout.ActionFlyout.IsOpen) return;

        e.Handled = true;
        Services.QuickPanel.QuickPanelManager.Instance?.Hide();
    }

    /// <summary>Runs an action's configured hotkey against the selection, without opening the menu.</summary>
    /// <remarks>
    /// The full window reaches this through SearchInputHelper.TryActionHotkey, which needs an
    /// ISearchWindow and a ShellMenuPresenter for gates this panel has no equivalent of: whether the
    /// search caret is at the end, and whether an actions pane could be shown. The execution underneath
    /// is an overload taking only IPluginSearchWindow, which is what the quick navigation menu already
    /// calls for the same reason, so the panel uses that directly.
    ///
    /// Bare keys are allowed through here where the full window checks its caret first: with no search
    /// box there is nothing a bare keystroke could be typing into, so a bound one can only have been
    /// meant as the action.
    /// </remarks>
    private void TryRunActionHotkey(System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.None && !Helpers.HotkeyActionTrigger.HasBareKeyActionHotkey(e.Key))
            return;

        var selection = ItemsList.SelectedItems.OfType<AppSearchResult>().ToList();
        if (selection.Count == 0) return;

        if (Helpers.HotkeyActionTrigger.TryExecute(e, selection, this, PluginSdk.Abstractions.SearchWindowType.Main, hideOnRun: true))
            e.Handled = true;
    }

    /// <summary>True while DragMove's modal loop is running, so the dismiss-on-deactivate can stand down.</summary>
    /// <remarks>
    /// DragMove blocks in a modal move loop and the window comes out of it deactivated, which the
    /// manager treats as "the user clicked away" and hides on. So dragging the panel made it vanish the
    /// moment the button came up. The flag spans the loop, and the window asks for the foreground back
    /// afterwards, since it genuinely did lose it.
    /// </remarks>
    public bool IsDraggingWindow { get; private set; }

    private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        try
        {
            IsDraggingWindow = true;
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The guard every other custom-chrome window here carries: DragMove throws if the button
            // has already been released by the time it runs.
        }
        finally
        {
            IsDraggingWindow = false;
            Activate();
        }
    }

    // The same flyout the full window shows, anchored to the list rather than to a search box, this
    // panel having none. Right button UP rather than down, matching the full window: pressing down is
    // what moves the selection, so acting on the release is what lets a right-click on an unselected row
    // act on that row instead of on whatever was selected before it.
    private void ItemsList_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var selection = ItemsList.SelectedItems.OfType<AppSearchResult>().ToList();
        if (selection.Count == 0) return;

        e.Handled = true;
        ActionFlyout.Show(selection, this, this, ItemsList, System.Windows.Controls.Primitives.PlacementMode.MousePoint);
    }

    private void ItemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsList.SelectedItem is not AppSearchResult result) return;

        FileExecutor.OpenFileOrFolder(result.FullPath);
    }
}
