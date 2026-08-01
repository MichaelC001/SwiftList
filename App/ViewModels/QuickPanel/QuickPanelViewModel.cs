using System.IO;
using System.Collections.ObjectModel;
using SwiftList.App.ViewModels.Search.StartupPanel;

namespace SwiftList.App.ViewModels.QuickPanel;

// Backs the quick panel: results grouped by the folder they sit in, shown over whatever window is in
// front.
//
// It has NO data source. The startup panel's tabs were wired in while this was being built, so there
// was something real to group, sort and scroll against; that was scaffolding and it is gone. What
// remains is the shell and everything it does with what it is given: grouping, per-group order and
// view, the two row templates, drag, the action menu. Until a source is attached, SetItems is never
// called, Groups stays empty, and the panel does not open at all.
public class QuickPanelViewModel : ViewModelBase
{


    public ObservableCollection<StartupPanelTabViewModel> Tabs { get; } = new();

    /// <summary>One entry per folder, each holding its own items in its own order.</summary>
    public ObservableCollection<QuickPanelGroupViewModel> Groups { get; } = new();


    public QuickPanelViewModel()
    {

    }

    /// <summary>Reloads before the panel is shown. Nothing to reload yet.</summary>
    /// <remarks>
    /// Deliberately still here with no source behind it. The panel shows what is recent and where you
    /// currently are, so it has to reload on every open rather than once: a version that loaded at
    /// construction would keep showing whatever was true the first time it was ever opened, and the
    /// panel is reused rather than rebuilt.
    ///
    /// That decision was made and tested while the startup panel was standing in as a source, and
    /// removing the source would otherwise have taken it with it, leaving whoever attaches the real one
    /// to rediscover it. Awaited before the panel opens, not after, because whether it opens at all
    /// depends on what this turns up.
    /// </remarks>
    public Task RefreshAsync() => Task.CompletedTask;

    /// <summary>Whether there is anything worth opening the panel for.</summary>
    /// <remarks>
    /// Distinct from IsEmpty below, which they are easy to mistake for each other. This one gates
    /// whether the panel opens at all; that one is about the tab you are looking at once it has. A panel
    /// with three tabs where the selected one happens to be empty should still open, and say so.
    /// </remarks>
    public bool HasContent => Groups.Count > 0;


    private bool _isEmpty = true;

    /// <summary>Whether the tab on screen has nothing in it, which the panel says rather than implies.</summary>
    public bool IsEmpty
    {
        get => _isEmpty;
        private set => SetProperty(ref _isEmpty, value);
    }

    /// <summary>Takes a freshly loaded set, stamps it, and hands it to Rebuild for ordering.</summary>
    private void SetItems(IEnumerable<AppSearchResult> items)
    {
        // The timestamp is read once per load and kept alongside the item purely to sort by. What the
        // change because the order did, and re-reading it would put the row's own second line through a
        // to learn and fills it in later, and a written value would keep the placeholder forever.
        var loaded = items
            .Select(item => (Item: item, Modified: ReadModified(item)))
            .ToList();


        // Folders in alphabetical order. Ordering them by anything derived from their contents, newest
        // file for instance, would reshuffle the whole panel every time a file changed underneath it.
        Groups.Clear();
        foreach (var group in loaded
                     .GroupBy(pair => DirectoryOf(pair.Item.FullPath))
                     .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            Groups.Add(new QuickPanelGroupViewModel(group.Key, group.ToList()));
        }

        IsEmpty = Groups.Count == 0;
    }

    private static string DirectoryOf(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;

        try
        {
            // A drive root has no parent, so it groups under itself rather than collapsing into the
            // empty-string group alongside every other unparented path.
            return Path.GetDirectoryName(path) ?? path;
        }
        catch
        {
            return path;
        }
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


}
