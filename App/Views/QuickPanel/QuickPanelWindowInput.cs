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
