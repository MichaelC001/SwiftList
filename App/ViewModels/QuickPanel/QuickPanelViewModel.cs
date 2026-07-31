using System.ComponentModel;
using System.Windows.Data;
using System.Globalization;
using System.Collections.ObjectModel;
using SwiftList.App;
using SwiftList.App.ViewModels.Search.StartupPanel;

namespace SwiftList.App.ViewModels.QuickPanel;

// Backs the quick panel: a list of results grouped by the folder they sit in, shown over whatever
// window is in front.
//
// It has NO data source. The startup panel's tabs were wired in while the window itself was being
// built, purely so there was something real to lay out and scroll; that was scaffolding and it is
// gone. What remains is the shell and its behaviour, waiting for the source this panel is actually
// for. Until one is attached, Items stays empty and the panel does not open at all.
public class QuickPanelViewModel : ViewModelBase
{
    public ObservableCollection<StartupPanelTabViewModel> Tabs { get; } = new();

    public ObservableCollection<AppSearchResult> Items { get; } = new();

    /// <summary>Items grouped by the folder they sit in, which is what the list binds to.</summary>
    /// <remarks>
    /// Grouped on FullPath through a converter rather than on a property of the item: the folder is not
    /// something a result carries, and the field that would have held it carries the modified time.
    ///
    /// The view is built once over the same ObservableCollection, so regrouping follows from the
    /// collection changing and nothing has to rebuild it.
    /// </remarks>
    public ICollectionView ItemsView { get; }

    public QuickPanelViewModel()
    {
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.GroupDescriptions.Add(new PropertyGroupDescription(
            nameof(AppSearchResult.FullPath), new Views.QuickPanel.ResultDirectoryConverter()));
    }

    /// <summary>Whether there is anything worth opening the panel for.</summary>
    /// <remarks>
    /// Distinct from IsEmpty below, which they are easy to mistake for each other. This one gates
    /// whether the panel opens at all; that one is about the tab you are looking at once it has. A panel
    /// with three tabs where the selected one happens to be empty should still open, and say so.
    /// </remarks>
    public bool HasContent => Items.Count > 0;

    private bool _isEmpty = true;

    /// <summary>Whether the tab on screen has nothing in it, which the panel says rather than implies.</summary>
    public bool IsEmpty
    {
        get => _isEmpty;
        private set => SetProperty(ref _isEmpty, value);
    }

    /// <summary>Replaces the panel's contents, newest first within each folder.</summary>
    /// <remarks>
    /// Sorted here rather than through a SortDescription, which would need a modified-time property on
    /// AppSearchResult: that is a shared model and has none. Grouping alone leaves insertion order
    /// intact within each group, so inserting in order is enough.
    ///
    /// The key is the DateTime, not the string that goes onto the row. That string is formatted and
    /// localised, so ordering by it would rank "3 days ago" against "10 minutes ago" alphabetically and
    /// answer differently in every language.
    /// </remarks>
    public void SetItems(IEnumerable<AppSearchResult> items)
    {
        Items.Clear();

        var stamped = items
            .Select(item => (Item: item, Modified: ReadModified(item)))
            .OrderByDescending(pair => pair.Modified ?? DateTime.MinValue)
            .ToList();

        foreach (var (item, modified) in stamped)
        {
            StampModifiedTime(item, modified);
            Items.Add(item);
        }

        IsEmpty = Items.Count == 0;
    }

    /// <summary>The result's modified time, without going to the filesystem for it.</summary>
    /// <remarks>
    /// AppSearchResult.DateModified answers from the index for almost every result, and falls back to a
    /// throttled background read only for the few it does not know. Reading the file here instead would
    /// be three filesystem round trips per row, on the UI thread, to re-learn something already
    /// recorded.
    ///
    /// MinValue is that property's "not known yet": those sort last and get no timestamp line, and the
    /// background load fills the value in for next time.
    /// </remarks>
    private static DateTime? ReadModified(AppSearchResult item)
        => item.DateModified is var modified && modified != DateTime.MinValue ? modified : null;

    /// <summary>Puts the modified time on the row's second line, the way Recent Files renders it.</summary>
    /// <remarks>
    /// Written onto ParentDir, which is what the shared row template draws under the name. The
    /// directory that field would otherwise hold is not lost: it is what the rows are grouped by, so it
    /// appears once per group in the header rather than repeated on every row.
    /// </remarks>
    private static void StampModifiedTime(AppSearchResult item, DateTime? modifiedOrNull)
    {
        if (modifiedOrNull is not { } modified) return;

        // Absolute first, interval in brackets after it. The interval alone answers "how stale is this"
        // at a glance but never "which day was that"; the absolute alone is the reverse. The absolute
        // half is formatted by the current culture rather than a fixed pattern, so a machine set to a
        // language that writes the date the other way round gets its own order.
        var relative = RecentFilesTabSource.FormatRelativeTime(modified);
        var absolute = modified.ToString("g", CultureInfo.CurrentCulture);
        item.ParentDir = string.IsNullOrEmpty(relative) ? absolute : $"{absolute} ({relative})";
    }
}
