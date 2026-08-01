using System.IO;
using System.Windows;
using SwiftList.App.ViewModels.QuickPanel;
using SwiftList.PluginSdk.Shell.FileOperations;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;

namespace SwiftList.App.Views.QuickPanel;

// Dropping files onto a group copies them into that group's folder. Split out of QuickPanelWindow so
// the window file stays under the repo's per-file line limit; it has no state of its own and works
// entirely off the group each handler's own DataContext names.
//
// The copy is Windows' own (IFileOperation, via ShellPasteHelper): one native progress dialog for the
// whole batch, the real "this file already exists" prompt, and an undo entry -- all things a hand-rolled
// File.Copy loop would have to reinvent badly.
public partial class QuickPanelWindow
{
    /// <summary>Whether a drag hovering over this group is one it should take.</summary>
    /// <remarks>
    /// Three separate questions, and all three have to be yes. Whether the source was configured as a
    /// drop target. Whether the drag is carrying files at all -- a drag of text or of anything else has
    /// nothing to copy. And whether it started inside this panel: a row dragged from one group towards
    /// another is on its way OUT to some other window, and turning a half-finished drag-out into a real
    /// file copy in the folder next door is not a mistake worth being able to make.
    /// </remarks>
    internal static bool CanDrop(QuickPanelGroupViewModel? group, bool carriesFiles, bool startedInsideThePanel)
        => group is { AcceptsDrops: true }
           && carriesFiles
           && !startedInsideThePanel
           && !string.IsNullOrEmpty(group.FolderPath)
           && Directory.Exists(group.FolderPath);

    // The three answers read off the live drag. IsDragActive is set by this app's own drag-out helper for
    // the length of its DoDragDrop, which is exactly "this drag started in one of our lists".
    private static bool CanDrop(QuickPanelGroupViewModel? group, DragEventArgs e)
        => CanDrop(group,
            e.Data.GetDataPresent(DataFormats.FileDrop),
            Views.Controls.Results.ResultsDragDropHelper.IsDragActive);

    private static QuickPanelGroupViewModel? GroupOf(object sender)
        => (sender as FrameworkElement)?.DataContext as QuickPanelGroupViewModel;

    private void Group_DragOver(object sender, DragEventArgs e)
    {
        var group = GroupOf(sender);
        var allowed = CanDrop(group, e);

        // Copy, never Move, whatever the source offered or the user is holding down. The panel is
        // somewhere to put a copy of something; a drag into it that could silently take the original
        // away from wherever it came from is a different and much more expensive thing to get wrong.
        e.Effects = allowed ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;

        if (group != null)
            group.IsDropTarget = allowed;
    }

    private void Group_DragLeave(object sender, DragEventArgs e)
    {
        if (GroupOf(sender) is { } group)
            group.IsDropTarget = false;
    }

    private void Group_Drop(object sender, DragEventArgs e)
    {
        var group = GroupOf(sender);
        if (group != null)
            group.IsDropTarget = false;

        if (!CanDrop(group, e)) return;

        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;

        var existing = paths.Where(path => File.Exists(path) || Directory.Exists(path)).ToList();
        if (existing.Count == 0) return;

        // Never a move, so the flag is fixed rather than read from the drag. See Group_DragOver.
        ShellPasteHelper.PasteAsync(existing, group!.FolderPath, move: false);

        // The panel stays where it is. The copy runs behind its own dialog and the group will pick the
        // new files up on the next open, which is when the panel next asks the index what is there.
    }
}
