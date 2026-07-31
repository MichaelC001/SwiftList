using System.Windows;
using System.Windows.Media;

namespace SwiftList.App.Views.Settings.Plugins;

// Code-behind for FieldRowTemplate.xaml's array master/detail event handlers -- split out of
// PluginConfigWindow.xaml.cs purely to let the recursive field-row template live in this separate
// ResourceDictionary and keep PluginConfigWindow.xaml under the file-length limit; none of these
// depend on PluginConfigWindow's own state.
public partial class PluginConfigFieldRowTemplate : ResourceDictionary
{
    // Brings the newly-added (and newly-selected) row into view when the master list has more
    // items than fit -- otherwise Add silently appends off-screen below the visible scroll area.
    private void ArrayMasterList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox { SelectedItem: not null } listBox)
            listBox.ScrollIntoView(listBox.SelectedItem);
    }

    // Keeps an array field's master list column exactly as tall as its detail panel, so only the
    // ListBox itself scrolls (not the whole page). Driven from code rather than a pure Grid Auto-row
    // + ElementName height binding, because that combination feeds back on itself: an unresolved
    // first-pass height lets the master list's own (unbounded) natural size leak into the row's Auto
    // height, which the detail panel -- if it were Stretch-aligned -- would then adopt too, permanently
    // locking in an inflated value. Doing the sync explicitly after each real layout avoids that.
    private void ArrayDetailPanel_Loaded(object sender, RoutedEventArgs e)
    {
        SyncArrayMasterListHeight(sender);
        TriggerWindowResizeToFit(sender);
    }

    private void ArrayDetailPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        SyncArrayMasterListHeight(sender);
        TriggerWindowResizeToFit(sender);
    }

    private static void TriggerWindowResizeToFit(object sender)
    {
        if (sender is FrameworkElement element && Window.GetWindow(element) is Controls.Dialogs.PluginFieldPromptWindow promptWindow)
        {
            promptWindow.ResizeToFit();
        }
    }

    private static void SyncArrayMasterListHeight(object sender)
    {
        if (sender is not FrameworkElement detailPanel) return;
        if (VisualTreeHelper.GetParent(detailPanel) is not System.Windows.Controls.Grid parentGrid) return;

        var masterGrid = parentGrid.Children.OfType<System.Windows.Controls.Grid>()
            .FirstOrDefault(g => g.Name == "ArrayMasterListGrid");
        if (masterGrid == null) return;

        // An empty array has no selected item, so the detail panel collapses to zero height. Don't
        // mirror that onto the master list too -- that would hide the Add button along with it, which
        // is exactly what's needed to add the very first item. Fall back to its own natural size instead.
        masterGrid.Height = detailPanel.ActualHeight > 0 ? detailPanel.ActualHeight : double.NaN;
    }
}
