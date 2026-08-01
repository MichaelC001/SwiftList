using System.Collections.ObjectModel;
using SwiftList.Core;
using SwiftList.Core.SearchIndex;

namespace SwiftList.App.ViewModels.QuickPanel;

/// <summary>One source's worth of the quick panel, with its own order.</summary>
/// <remarks>
/// Keyed by the source it was built from rather than by the folder it happens to point at: order,
/// visibility and per-group preferences are all stored against a source id, and plugin-provided sources
/// will share that id space without having a path to be keyed by at all.
///
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

    public QuickPanelGroupViewModel(
        string sourceId,
        string title,
        string folderPath,
        List<(AppSearchResult Item, DateTime? Modified)> loaded,
        QuickPanelSortMode sortMode = QuickPanelSortMode.ModifiedDescending,
        bool thumbnailView = true,
        bool expanded = true)
    {
        SourceId = sourceId;
        Title = title;
        FolderPath = folderPath;
        _loaded = loaded;
        // The fields, not the properties: each setter rebuilds, and the group has nothing to rebuild
        // from until the call below.
        _sortMode = sortMode;
        _isThumbnailView = thumbnailView;
        _isExpanded = expanded;
        Rebuild();
    }

    /// <summary>The source this group came from, which is what its stored preferences are filed under.</summary>
    public string SourceId { get; }

    /// <summary>The heading: the user's own name for the source, or the source's default one.</summary>
    public string Title { get; }

    /// <summary>The folder itself, shown in full beside the heading.</summary>
    public string FolderPath { get; }

    /// <summary>How many entries this group is showing, which under a filter is how many matched.</summary>
    public int Count => Items.Count;

    private string _filter = string.Empty;

    /// <summary>Narrows the group to the entries whose name the query matches.</summary>
    /// <remarks>
    /// The same matching every other box in this app does: <see cref="FuzzyMatcher.IsMatch"/>, which is
    /// the index scan's own fzf rule reached through the seam built for callers that need identical
    /// semantics without running a scan. So "rdm" finds readme.md here exactly as it does in the search
    /// window, and a pinyin alias works because that matcher already consults the alias providers.
    ///
    /// What it does not do is reorder: each group keeps the order its source kind gives it, because that
    /// order is a setting the user chose per source rather than something a query should override.
    /// </remarks>
    public void ApplyFilter(string? query)
    {
        var normalized = query?.Trim() ?? string.Empty;
        if (string.Equals(_filter, normalized, StringComparison.Ordinal)) return;

        _filter = normalized;
        Rebuild();
    }

    /// <summary>Whether anything survived the filter -- a group with nothing left is hidden entirely.</summary>
    public bool HasMatches => Items.Count > 0;

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
        var matching = _filter.Length == 0
            ? _loaded
            : _loaded.Where(pair => FuzzyMatcher.IsMatch(_filter, pair.Item.Name)).ToList();

        // Ordered on the DateTime, not on the string the row shows: that string is formatted and
        // localised, so ordering by it would rank "3 days ago" against "10 minutes ago" alphabetically
        // and answer differently in every language. Items with no known time sort last either way.
        var ordered = SortMode == QuickPanelSortMode.NameAscending
            ? matching.OrderBy(pair => pair.Item.Name, StringComparer.CurrentCultureIgnoreCase)
            : matching.OrderByDescending(pair => pair.Modified ?? DateTime.MinValue);

        Items.Clear();
        foreach (var (item, _) in ordered)
            Items.Add(item);

        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(HasMatches));
    }
}
