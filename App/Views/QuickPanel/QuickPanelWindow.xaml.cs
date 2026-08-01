using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SwiftList.App;
using SwiftList.App.Services;
using SwiftList.App.Services.ShellMenu.ActionFlyout;
using SwiftList.App.ViewModels.QuickPanel;
using Native = SwiftList.App.Views.InlineSearchWindow.Helpers.InlineSearchWindowNativeMethods;

namespace SwiftList.App.Views.QuickPanel;

// The panel itself: one workspace's sources, docked to the bottom-right of whatever window is in
// front. Lifecycle, the hotkey and the docking maths all live in QuickPanelManager, and what is shown
// comes from QuickPanelViewModel; this file is only the window's own input handling.
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

        // Each group owns a list of its own, so drag registration and the "which list is this" question
        // are both answered per list rather than once for a named one. GroupList_Loaded below does the
        // registering as each appears.

        // Every one of those lists is torn down when the panel closes, so the one _activeList points at
        // is detached the moment it does. Left dangling it is worse than null: GroupList_Loaded's ??=
        // would never replace it, and every hotkey on the next open would read the selection of a list
        // that is no longer on screen -- which is always empty, silently.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is false) _activeList = null;
        };
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
            _activeList?.Focus();
            if (_activeList != null) Keyboard.Focus(_activeList);

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
            if (TrySwitchWorkspace(e)) return;
            TryRunActionHotkey(e);
            return;
        }


        // The flyout closes on its own Escape and hangs its handler on this window too. Letting this
        // one act as well would dismiss the panel out from under a menu the user was only closing.
        if (Services.ShellMenu.ActionFlyout.ActionFlyout.IsOpen) return;

        e.Handled = true;
        Services.QuickPanel.QuickPanelManager.Instance?.Hide();
    }

    /// <summary>Hold the jump-to-Nth-result modifier and press 1-9 to switch workspace.</summary>
    /// <remarks>
    /// The same modifier the result lists use for "jump to result N", rather than a second setting that
    /// would only ever be set to the same thing -- one "hold this and press a number" key everywhere.
    /// There is no numbered row to collide with here: this panel's groups are opened by clicking them.
    ///
    /// Checked ahead of the action hotkeys because a bare digit can legitimately be bound to an action,
    /// and the modifier is what tells the two apart.
    /// </remarks>
    private bool TrySwitchWorkspace(System.Windows.Input.KeyEventArgs e)
    {
        var index = WorkspaceIndexFor(e.Key, Keyboard.Modifiers, Core.UserSettings.Load().Hotkeys.SelectJumpModifier);
        if (index == 0 || DataContext is not QuickPanelViewModel viewModel) return false;

        e.Handled = true;
        _ = viewModel.SelectTabAtAsync(index);
        return true;
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

        var selection = _activeList?.SelectedItems.OfType<AppSearchResult>().ToList() ?? new List<AppSearchResult>();
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
        if (sender is not System.Windows.Controls.ListBox list) return;

        _activeList = list;
        var selection = list.SelectedItems.OfType<AppSearchResult>().ToList();
        if (selection.Count == 0) return;

        e.Handled = true;
        ActionFlyout.Show(selection, this, this, list, System.Windows.Controls.Primitives.PlacementMode.MousePoint);
    }

    /// <summary>The list the user last touched, which is what a keystroke acts on.</summary>
    /// <remarks>
    /// There is no single list any more: each folder group renders its own. A hotkey has to act on the
    /// one being used, and "last interacted with" is what that means in a panel where any of them can
    /// hold a selection.
    /// </remarks>
    private System.Windows.Controls.ListBox? _activeList;

    /// <summary>Clicking anything that is not a row drops the selection, as a file manager's own blank
    /// space does.</summary>
    /// <remarks>
    /// Previewed, because the click this exists for lands on a group's ListBox background and that
    /// ListBox would otherwise be first to see it -- and left unhandled, so the row click, the rubber
    /// band and the drag helper all still get their turn.
    ///
    /// Every group is cleared, not just the one under the pointer: each renders its own list, so a
    /// selection left in another group would keep a row looking selected that a keystroke no longer
    /// acts on.
    /// </remarks>
    private void Content_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Whatever else this press turns out to be -- a row click, the start of a rubber band, a click on
        // nothing -- the list it landed in is the one the user is now working in. Only right-click and
        // double-click used to say so, so a plain left-click (or a band) left the hotkeys acting on
        // whichever list had been double-clicked last.
        if (e.OriginalSource is DependencyObject clicked
            && FindAncestor<System.Windows.Controls.ListBox>(clicked) is { } list)
            _activeList = list;

        // Not while a selection is being extended. Ctrl or Shift on blank space is the start of adding
        // to what is already selected -- a rubber band drawn with Ctrl held, most obviously -- and this
        // runs first, so without the check it would empty the very selection that gesture is extending.
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0) return;

        if (e.OriginalSource is DependencyObject source && IsClickOnNothing(source))
            ClearSelection((DependencyObject)sender);
    }

    private void GroupList_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox list) return;

        Views.Controls.Results.ResultsDragDropHelper.Register(list);
        _activeList ??= list;
    }

    /// <summary>Hands the wheel to the scroller around the groups.</summary>
    /// <remarks>
    /// A ListBox swallows the wheel even with its own scrolling disabled, and there is one of these per
    /// folder, so the pointer resting on any group left the panel unable to scroll at all. Nothing
    /// bubbles out on its own to fix that: the event has to be re-raised at the parent by hand. The
    /// plugin config page carries the same handler for the same reason.
    /// </remarks>
    private void GroupList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox list) return;

        e.Handled = true;
        var bubbled = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender,
        };
        (list.Parent as UIElement)?.RaiseEvent(bubbled);
    }

    // A group's own sort, taken from the button's own DataContext rather than the panel's: every header
    // has one of these and each acts on the folder it heads.
    private void SortToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement { DataContext: QuickPanelGroupViewModel group })
            group.ToggleSort();
    }

    // Also a group's own, alongside its sort: which view suits a folder is a property of the folder.
    private void ViewToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement { DataContext: QuickPanelGroupViewModel group })
            group.ToggleView();
    }

    private void ItemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox list) return;

        _activeList = list;
        if (list.SelectedItem is not AppSearchResult result) return;

        FileExecutor.OpenFileOrFolder(result.FullPath);
    }
}
