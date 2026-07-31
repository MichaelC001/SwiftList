using System.Collections.ObjectModel;
using System.IO;
using SwiftList.App;

namespace SwiftList.App.ViewModels.QuickPanel;

/// <summary>One folder's worth of the quick panel, with its own order.</summary>
/// <remarks>
/// The panel used to group through a CollectionView, which was simpler but cannot do this: sorting on
/// a view is a property of the whole view, so every group necessarily shared one order. Ordering per
/// group means the groups have to be real objects that each hold their own items, which is what this
/// is.
///
/// The trade that comes with it: each group renders its own list, so a selection belongs to one group
/// rather than spanning them. Acting on a set of files from two different folders at once is no longer
/// possible, and rubber-banding across a group boundary does nothing.
/// </remarks>
public class QuickPanelGroupViewModel : ViewModelBase
{
    private readonly List<(AppSearchResult Item, DateTime? Modified)> _loaded;

    public QuickPanelGroupViewModel(string folderPath, List<(AppSearchResult Item, DateTime? Modified)> loaded)
    {
        FolderPath = folderPath;
        _loaded = loaded;
        Rebuild();
    }

    /// <summary>The folder itself, shown in full beside the heading.</summary>
    public string FolderPath { get; }

    /// <summary>Its last segment, which is what the heading leads with.</summary>
    /// <remarks>
    /// A drive root has no last segment, so it stands as its own name: "D:\" reads better as a heading
    /// than the empty string trimming it would otherwise give.
    /// </remarks>
    public string LeafName
    {
        get
        {
            var trimmed = FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var leaf = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(leaf) ? FolderPath : leaf;
        }
    }

    public int Count => _loaded.Count;

    public ObservableCollection<AppSearchResult> Items { get; } = new();

    private QuickPanelSortMode _sortMode = QuickPanelSortMode.ModifiedDescending;

    /// <summary>This group's own order. Newest first until someone says otherwise.</summary>
    public QuickPanelSortMode SortMode
    {
        get => _sortMode;
        set
        {
            if (!SetProperty(ref _sortMode, value)) return;
            Rebuild();
        }
    }

    public void ToggleSort() => SortMode = SortMode == QuickPanelSortMode.ModifiedDescending
        ? QuickPanelSortMode.NameAscending
        : QuickPanelSortMode.ModifiedDescending;

    private bool _isThumbnailView = true;

    /// <summary>Thumbnail tiles when true, the detail list when false. This folder's own choice.</summary>
    /// <remarks>
    /// Per group for the same reason the order is: a folder of images wants tiles and a folder of
    /// documents wants names and dates, and which is which is a property of the folder, not of the
    /// panel. It lived on the panel while every group shared one list, and stayed there one step longer
    /// than it had to.
    /// </remarks>
    public bool IsThumbnailView
    {
        get => _isThumbnailView;
        set => SetProperty(ref _isThumbnailView, value);
    }

    public void ToggleView() => IsThumbnailView = !IsThumbnailView;

    private bool _isExpanded = true;

    /// <summary>Held here rather than by the Expander so it survives the group being rebuilt.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    private void Rebuild()
    {
        // Ordered on the DateTime, not on the string the row shows: that string is formatted and
        // localised, so ordering by it would rank "3 days ago" against "10 minutes ago" alphabetically
        // and answer differently in every language. Items with no known time sort last either way.
        var ordered = SortMode == QuickPanelSortMode.NameAscending
            ? _loaded.OrderBy(pair => pair.Item.Name, StringComparer.CurrentCultureIgnoreCase)
            : _loaded.OrderByDescending(pair => pair.Modified ?? DateTime.MinValue);

        Items.Clear();
        foreach (var (item, _) in ordered)
            Items.Add(item);
    }
}
