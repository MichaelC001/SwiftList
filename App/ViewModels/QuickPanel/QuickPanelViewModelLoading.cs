using SwiftList.App.ViewModels.Search;
using SwiftList.Core;

namespace SwiftList.App.ViewModels.QuickPanel;

// What a refresh actually loads. Split out of QuickPanelViewModel.cs purely to keep that file under the
// repo's per-file line limit; it has no state of its own and only ever operates on the one view model it
// is part of.
public partial class QuickPanelViewModel
{
    /// <summary>
    /// Starts loading every workspace and returns as soon as there is something worth opening for --
    /// or when everything has finished and there is not.
    /// </summary>
    /// <remarks>
    /// Nothing waits on anything else. Each source is its own task, each group appears the moment its own
    /// source is ready, and the panel opens on the first one to arrive rather than the last. A source on
    /// a disconnected share used to hold the whole summon open behind it; now it costs only its own
    /// group, which turns up late or not at all.
    ///
    /// The task returned is deliberately not "everything is loaded": the caller's question is only
    /// whether to open a window, and that is answered by the first entry. The rest lands afterwards,
    /// into a panel that is already on screen.
    ///
    /// Streaming stops at the source, not inside it. A source's entries are ordered and capped as a set
    /// (see QuickPanelSourceLoader.Order), so emitting them one at a time would mean re-sorting the group
    /// under the pointer and re-deciding which ones the cap keeps, on every arrival. A slow source
    /// honestly shows up as its group arriving late; it should not show up as a list that will not sit
    /// still.
    /// </remarks>
    public async Task RefreshAsync(string? processName = null, CancellationToken token = default)
    {
        // Each open starts unfiltered. The box is part of the window and every open builds a new one, so
        // it is empty on screen -- a query left on this view model would narrow the list by something
        // the user cannot see and did not type. Assigned to the field rather than the property: there is
        // nothing to re-filter, the groups it would run over are about to be replaced.
        _searchQuery = string.Empty;
        OnPropertyChanged(nameof(SearchQuery));

        var settings = _readSettings();
        // Disabled workspaces are dropped here rather than filtered at every use: a workspace that has
        // no tab must also not be reachable by a process rule or by the number keys.
        var enabled = settings.Tabs.Where(tab => tab.Enabled).ToList();

        _content = new Dictionary<string, List<QuickPanelGroupViewModel>>(StringComparer.OrdinalIgnoreCase);
        _workspaces = new List<QuickPanelTab>();
        _pendingWorkspaces = enabled;
        // What the panel wants to open on, decided before anything has loaded. _activeTabId is what it
        // is actually showing, which may be a stand-in until the wanted one turns up -- or forever, if
        // that workspace has nothing in it.
        _wantedTabId = ResolveActiveTabId(settings, processName, enabled);
        _activeTabId = string.Empty;

        RebuildTabs();
        ShowActiveWorkspace();

        // Completed by the first group to land. Nothing else awaits it, so a summon that turns up empty
        // still finishes -- through the WhenAll below rather than through this.
        var firstArrival = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _firstArrival = firstArrival;

        var everything = Task.WhenAll(enabled.Select(workspace => LoadWorkspaceAsync(workspace, token)));

        // Whichever comes first: something to show, or nothing left to wait for.
        await Task.WhenAny(firstArrival.Task, everything).ConfigureAwait(true);
        _firstArrival = null;
    }

    /// <summary>Enabled workspaces still loading, in configured order -- what a group's rank is read from.</summary>
    private List<QuickPanelTab> _pendingWorkspaces = new();

    private TaskCompletionSource? _firstArrival;

    /// <summary>Loads one workspace's visible sources, each on its own, and files each result as it lands.</summary>
    private async Task LoadWorkspaceAsync(QuickPanelTab workspace, CancellationToken token)
    {
        var visible = QuickPanelGroupOrdering.Resolve(
            workspace.Folders.Select(folder => folder.Id),
            workspace.GroupOrder,
            workspace.DisabledGroupIds).ToList();

        await Task.WhenAll(visible.Select(async (id, rank) =>
        {
            var source = workspace.Folders.FirstOrDefault(folder => folder.Id == id);
            if (source == null) return;

            var group = await BuildGroupAsync(workspace, source, token).ConfigureAwait(true);
            if (group != null) Place(workspace, group, rank);
        })).ConfigureAwait(true);
    }

    /// <summary>Files a finished group under its workspace, in the position the settings give it.</summary>
    /// <remarks>
    /// At its configured rank, never appended. Groups now arrive in whatever order their sources happen
    /// to finish, so appending would let a fast source outrank a slow one and quietly replace the user's
    /// own order with a race -- the same trap the startup panel's tabs hit and solved the same way.
    ///
    /// The tab appears with the workspace's first group, for the same reason it disappears when a
    /// workspace has none: a tab is only worth a place in the strip if there is something behind it.
    /// </remarks>
    private void Place(QuickPanelTab workspace, QuickPanelGroupViewModel group, int rank)
    {
        // Sources finish on their own tasks, so two can land at the same moment. In the running app the
        // UI SynchronizationContext serialises the continuations and this is safe by accident of where
        // they resume; the lock is what makes it true without depending on that, and it is the only
        // thing holding these collections together anywhere there is no dispatcher.
        lock (_placing)
        {
            if (!_content.TryGetValue(workspace.Id, out var groups))
            {
                _content[workspace.Id] = groups = new List<QuickPanelGroupViewModel>();
                AddWorkspaceTab(workspace);
            }

            var at = groups.FindIndex(existing => RankOf(existing.SourceId) > rank);
            if (at < 0) at = groups.Count;
            groups.Insert(at, group);
            _ranks[group.SourceId] = rank;

            if (workspace.Id.Equals(_activeTabId, StringComparison.OrdinalIgnoreCase))
                ShowActiveWorkspace();
        }

        _firstArrival?.TrySetResult();
    }

    private readonly object _placing = new();

    // Where each source sits in its workspace's configured order, remembered as it lands so the next
    // arrival can be slotted against it.
    private readonly Dictionary<string, int> _ranks = new(StringComparer.OrdinalIgnoreCase);

    private int RankOf(string sourceId) => _ranks.TryGetValue(sourceId, out var rank) ? rank : int.MaxValue;

    /// <summary>What the panel wants to be showing, which it may not be able to yet.</summary>
    private string _wantedTabId = string.Empty;

    /// <summary>Gives a workspace its tab, at the position the settings order it in.</summary>
    /// <remarks>
    /// Tabs arrive in whatever order their workspaces first produce something, so the position comes
    /// from the configured order rather than from arrival -- appending would let a fast workspace
    /// outrank a slow one and quietly replace the user's own order with a race.
    ///
    /// The wanted workspace may be slower than another, or may have nothing at all. Until it arrives the
    /// panel shows the first tab it has, and switches the moment the wanted one does turn up. The wanted
    /// id is never overwritten by that stand-in, which is what lets it still be honoured later.
    /// </remarks>
    private void AddWorkspaceTab(QuickPanelTab workspace)
    {
        var at = _pendingWorkspaces.IndexOf(workspace);
        var before = _workspaces.FindIndex(existing => _pendingWorkspaces.IndexOf(existing) > at);
        if (before < 0) before = _workspaces.Count;
        _workspaces.Insert(before, workspace);

        var isWanted = workspace.Id.Equals(_wantedTabId, StringComparison.OrdinalIgnoreCase);
        var showingNothing = string.IsNullOrEmpty(_activeTabId)
            || !_workspaces.Any(tab => tab.Id.Equals(_activeTabId, StringComparison.OrdinalIgnoreCase));

        if (isWanted || showingNothing)
            _activeTabId = isWanted ? workspace.Id : _workspaces[0].Id;

        RebuildTabs();
        if (isWanted || showingNothing) ShowActiveWorkspace();
    }

    /// <summary>The folder source behind a group, looked up across every workspace this refresh knows.</summary>
    private QuickPanelFolderSource? SourceOf(string sourceId) => _pendingWorkspaces
        .SelectMany(workspace => workspace.Folders)
        .FirstOrDefault(folder => folder.Id.Equals(sourceId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Loads one group's source again and puts the result back into that same group.</summary>
    /// <remarks>
    /// For after a drop: the files were copied by the shell behind its own dialog, and nothing tells the
    /// panel they arrived. Only the one group is reloaded, and into the object that is already on screen,
    /// so its sort, its view and whether it is collapsed all survive -- which a full refresh would not
    /// leave alone.
    ///
    /// A source that has since gone (settings edited while the panel was up) simply leaves the group as
    /// it was: there is nothing to load it from, and emptying it would be a worse answer than stale.
    /// </remarks>
    public async Task ReloadGroupAsync(QuickPanelGroupViewModel group, CancellationToken token = default)
    {
        var source = SourceOf(group.SourceId);
        if (source == null) return;

        List<SearchResult> results;
        try
        {
            results = await _load(source, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Log($"[QuickPanel] Source '{source.Path}' failed to reload: {ex.Message}", LogLevel.Error);
            return;
        }

        group.Replace(results
            .Select((result, index) => (
                Item: SearchResultHelper.CreateUiResult(result, string.Empty, index, isApplication: false, scope: null),
                Modified: ReadModified(result)))
            .ToList());

        // A group that the reload emptied is hidden by its own HasMatches, so the panel's own "nothing
        // here" line has to be recomputed against what is left.
        IsEmpty = !Groups.Any(shown => shown.HasMatches);
    }

    /// <summary>One configured source, loaded and dressed as a group -- or null when it has nothing.</summary>
    /// <remarks>
    /// A source that came back empty is left out rather than shown as an empty heading. The panel is a
    /// quarter of the window it docks to and every heading costs a row of it; which sources exist is
    /// what the settings page is for, and a heading reading "(0)" that cannot be opened into anything
    /// would spend that row saying so.
    /// </remarks>
    private async Task<QuickPanelGroupViewModel?> BuildGroupAsync(
        QuickPanelTab workspace, QuickPanelFolderSource source, CancellationToken token)
    {
        List<SearchResult> results;
        try
        {
            results = await _load(source, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // One unreachable folder (a disconnected drive, a permission change) costs its own group and
            // nothing else: the rest of the workspace is still worth showing.
            Logger.Log($"[QuickPanel] Source '{source.Path}' failed to load: {ex.Message}", LogLevel.Error);
            return null;
        }

        if (results.Count == 0)
            return null;

        workspace.GroupPreferences.TryGetValue(source.Id, out var preference);

        var items = results
            .Select((result, index) => (
                Item: SearchResultHelper.CreateUiResult(result, string.Empty, index, isApplication: false, scope: null),
                Modified: ReadModified(result)))
            .ToList();

        return new QuickPanelGroupViewModel(
            source.Id,
            TitleOf(source, preference),
            source.Path,
            items,
            // Not preference.Sort: the kind already IS the order choice, and nothing writes the stored
            // sort yet. Once the panel persists what the user does to a group, the stored value becomes
            // the override and this becomes its fallback.
            QuickPanelGroupPreference.DefaultSortFor(source.Kind),
            // The settings page owns this one: the header's own toggle overrides it for the session it
            // is pressed in, and this is what the group opens as.
            preference?.ThumbnailView ?? true,
            preference?.Expanded ?? true,
            source.AcceptsDrops);
    }

    private static string TitleOf(QuickPanelFolderSource source, QuickPanelGroupPreference? preference)
        => string.IsNullOrWhiteSpace(preference?.DisplayName)
            ? QuickPanelFolderSource.DefaultName(source.Path)
            : preference!.DisplayName.Trim();

    /// <summary>The result's modified time, without going to the filesystem for it.</summary>
    /// <remarks>
    /// The index answers for almost every result, so reading the file here instead would be a filesystem
    /// round trip per row, on the UI thread, to re-learn something already recorded.
    ///
    /// MinValue is the index's "not known": those sort last and get no timestamp line, and
    /// AppSearchResult's own throttled background read fills the value in for next time.
    /// </remarks>
    private static DateTime? ReadModified(SearchResult item)
        => item.Metadata.Modified is var modified && modified != DateTime.MinValue ? modified : null;
}
