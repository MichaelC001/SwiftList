using System.IO;
using System.Collections.ObjectModel;
using SwiftList.App;
using SwiftList.App.ViewModels.Search.StartupPanel;
using SwiftList.Core.Services.Search;

namespace SwiftList.App.ViewModels.QuickPanel;

// Backs the quick panel, a prototype window that shows the startup panel's tabs docked to whatever
// window is in front.
//
// It builds its own StartupPanelController rather than borrowing the quick window's. That controller
// takes the two things it needs as constructor arguments (a SearchService and somewhere to put the
// items it loads), so a second instance is a supported thing to have, and this way the panel's tab
// selection is its own: selecting a tab here does not move the quick window's selection underneath.
public class QuickPanelViewModel : ViewModelBase
{
    private readonly StartupPanelController _controller;

    public QuickPanelViewModel()
    {
        _controller = new StartupPanelController(new SearchService(), ApplyItems);
    }

    public ObservableCollection<StartupPanelTabViewModel> Tabs => _controller.Tabs;

    public ObservableCollection<AppSearchResult> Items { get; } = new();

    private bool _isEmpty = true;
    public bool IsEmpty
    {
        get => _isEmpty;
        private set => SetProperty(ref _isEmpty, value);
    }

    private void ApplyItems(IEnumerable<AppSearchResult> items)
    {
        Items.Clear();
        foreach (var item in items)
        {
            StampModifiedTime(item);
            Items.Add(item);
        }

        IsEmpty = Items.Count == 0;
    }

    /// <summary>Puts the file's modified time on the row's second line, the way Recent Files does.</summary>
    /// <remarks>
    /// Recent Files gets this by prefixing ParentDir, which is what the shared row template draws
    /// underneath the name, and it reads well enough that every tab here should have it rather than the
    /// one. Rebuilt from the path each time rather than appended to whatever ParentDir already holds, so
    /// running over an item Recent Files had already stamped replaces its prefix instead of stacking a
    /// second one on top.
    /// </remarks>
    private static void StampModifiedTime(AppSearchResult item)
    {
        if (string.IsNullOrEmpty(item.FullPath)) return;

        try
        {
            if (!File.Exists(item.FullPath) && !Directory.Exists(item.FullPath)) return;

            var modified = File.GetLastWriteTime(item.FullPath);
            var relative = RecentFilesTabSource.FormatRelativeTime(modified);

            // The directory is not on the row any more, it is the row's tooltip: at this width a path
            // was truncated to uselessness anyway, and the time is what the panel is sorted and read by.
            item.ParentDir = string.IsNullOrEmpty(relative) ? string.Empty : relative;
        }
        catch
        {
            // A path that cannot be stat'd keeps whatever second line it arrived with. Not worth
            // failing a panel over, and the row is still perfectly usable without the timestamp.
        }
    }

    /// <summary>Loads the tabs and their first tab's contents. Called each time the panel is shown.</summary>
    /// <remarks>
    /// Re-run on every show rather than once at construction: the tabs are built from what is recent and
    /// where the user last was, so a panel that loaded them once would keep showing whatever was true the
    /// first time it opened.
    /// </remarks>
    public Task<bool> ActivateAsync() => _controller.TryActivateAsync();

    public void Deactivate() => _controller.Deactivate();
}
