using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SwiftList.App.Views.QuickPanel;

// The decisions the panel's input handlers make, as functions of what was pressed rather than of the
// window's own state. Split out of QuickPanelWindow.xaml.cs purely to keep that file under the repo's
// per-file line limit; every one of these is static, and they are here rather than there because they
// are also the only parts of the panel's input worth testing without a mouse.
public partial class QuickPanelWindow
{
    /// <summary>Which workspace a keystroke asks for, 1-based, or 0 for anything that is not the shortcut.</summary>
    /// <remarks>
    /// The same modifier the result lists use for "jump to result N", rather than a second setting that
    /// would only ever be set to the same thing -- one "hold this and press a number" key everywhere.
    /// </remarks>
    internal static int WorkspaceIndexFor(Key key, ModifierKeys modifiers, string? jumpModifier)
    {
        if (string.IsNullOrEmpty(jumpModifier)) return 0;

        // The numpad row counts as the same number: compared as the digit it typed rather than as the
        // key it came from.
        var digit = key switch
        {
            >= Key.D1 and <= Key.D9 => key - Key.D1 + 1,
            >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad1 + 1,
            _ => 0,
        };
        if (digit == 0) return 0;

        // "D3", not "3". A combo string is a Key.ToString() spelling -- that is what the hotkey recorder
        // writes and what TryParseHotkey reads back -- and a bare digit parses as the raw enum ordinal
        // instead, "3" being Key.Tab. It matched nothing, silently, which is exactly what a shortcut
        // nobody pressed also looks like.
        return Helpers.WpfUiHelper.MatchesHotkey($"{jumpModifier}+D{digit}", modifiers, Key.D1 + (digit - 1))
            ? digit
            : 0;
    }

    /// <summary>Clicking anything that is not a row drops the selection, as a file manager's own blank
    /// space does.</summary>
    /// <remarks>
    /// Previewed, because the click this exists for lands on a group's ListBox background and that
    /// ListBox would otherwise be first to see it -- and left unhandled, so the row click, the rubber
    /// band and the drag helper all still get their turn.
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

    /// <summary>Whether what was hit is blank space rather than something that acts on its own.</summary>
    /// <remarks>
    /// A row, obviously. Also anything in a group header: collapsing a group or switching its order is
    /// not a click on nothing, and taking the selection away with it would be a surprise.
    /// </remarks>
    internal static bool IsClickOnNothing(DependencyObject source)
        => FindAncestor<System.Windows.Controls.ListBoxItem>(source) == null
           && FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(source) == null;

    private static T? FindAncestor<T>(DependencyObject from) where T : DependencyObject
    {
        for (var node = from; node != null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is T match) return match;
        }
        return null;
    }

    /// <summary>Empties every group's list under the given root.</summary>
    /// <remarks>
    /// Every one, not just the one under the pointer: each group renders its own list, so a selection
    /// left in another would keep a row looking selected that a keystroke no longer acts on.
    /// </remarks>
    internal static void ClearSelection(DependencyObject root)
    {
        if (root is System.Windows.Controls.ListBox list)
        {
            list.UnselectAll();
            return;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            ClearSelection(VisualTreeHelper.GetChild(root, i));
    }
}
