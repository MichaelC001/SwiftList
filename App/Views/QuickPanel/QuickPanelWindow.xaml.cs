using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SwiftList.App;
using SwiftList.App.Services;
using SwiftList.App.Services.ShellMenu.ActionFlyout;
using SwiftList.App.ViewModels.QuickPanel;

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
        // The system menu and its Alt+F4 stay blocked, though closing is now the normal way out: this
        // window has no title bar to right-click and no close button, so the menu has nothing to offer,
        // and the manager's own Escape and hotkey are the ways out that leave it in a known state.
        Helpers.Visuals.SystemMenuBlocker.Attach(this);
        DataContext = viewModel;

        // Each group owns a list of its own, so drag registration and the "which list is this" question
        // are both answered per list rather than once for a named one. GroupList_Loaded below does the
        // registering as each appears.

    }








    /// <summary>Takes the foreground and puts keyboard focus in the filter box.</summary>
    /// <remarks>
    /// The quick window's own summon, step for step, rather than a second way of doing this: the
    /// overlay dismissal has already run in QuickPanelManager.Toggle (it has to, before anything is
    /// shown), and what is left is ForceForeground through the hook, then Activate, then the box.
    ///
    /// ForceForeground rather than a bare Activate: this window is ShowActivated="False", so it comes up
    /// without focus, and Windows ignores a foreground grab from a thread that does not already own it.
    /// The hook process does own real recent input, which is what makes the handover land -- the same
    /// route and the same helper the quick window goes through.
    ///
    /// Queued at Input priority so it runs after the layout and render that Show just scheduled; asking
    /// for focus before the box exists on screen is asking a control that is not there yet.
    ///
    /// The box rather than a list: the panel is summoned to reach something, and typing towards it is
    /// the fastest way in when the workspace holds more than a screenful. What it costs is that a bare
    /// key is now typing rather than running an action -- TryRunActionHotkey stands down while the box
    /// has focus, by design, and a click on a row is what hands the keyboard back to the list.
    /// </remarks>
    public void ActivateAndFocus() => Dispatcher.BeginInvoke(new Action(() =>
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            Views.QuickSearchWindow.Helpers.QuickSearchWindowController.ForceForeground(hwnd);

        Activate();
        Focus();
        FilterBox.FocusInput();
    }), System.Windows.Threading.DispatcherPriority.Input);


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

        // A filter in the box is undone first, and only a second Escape closes the panel. Anything else
        // means the one key that gets you out of a narrowed list also throws away the panel you were
        // narrowing it in.
        if (DataContext is QuickPanelViewModel { SearchQuery.Length: > 0 } filtered)
        {
            e.Handled = true;
            filtered.SearchQuery = string.Empty;
            return;
        }

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
    /// Bare keys are allowed through here where the full window checks its caret first -- but only while
    /// nothing is being typed into. That used to be free: there was no box in this panel at all, so a
    /// bound key could only have been meant as the action. The filter box changed that premise, and
    /// every combination stands down while it has focus, not just the bare ones: Ctrl+A and Ctrl+C in a
    /// text box are the box's, whatever else they may also be bound to.
    /// </remarks>
    private void TryRunActionHotkey(System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;

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
