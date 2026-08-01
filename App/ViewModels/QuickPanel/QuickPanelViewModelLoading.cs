using SwiftList.App.ViewModels.Search;
using SwiftList.Core;

namespace SwiftList.App.ViewModels.QuickPanel;

// What a refresh actually loads. Split out of QuickPanelViewModel.cs purely to keep that file under the
// repo's per-file line limit; it has no state of its own and only ever operates on the one view model it
// is part of.
public partial class QuickPanelViewModel
{
    /// <summary>Reloads before the panel is shown, and picks the workspace the app in front claims.</summary>
    /// <remarks>
    /// Every open, not once: the panel shows what is recent and where you currently are, and it is
    /// reused rather than rebuilt, so a version that loaded at construction would keep showing whatever
    /// was true the first time it was ever opened. Awaited before the panel opens, not after, because
    /// whether it opens at all depends on what this turns up.
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

        // Every workspace, not just the one about to be shown. A tab is only worth a place in the strip
        // if there is something behind it, and there is no way to know that without asking -- so they
        // are all asked, together rather than one after another, which makes the wait the slowest
        // workspace instead of the sum of them. Switching tabs afterwards is a swap, not a reload.
        var loaded = await Task.WhenAll(
            enabled.Select(workspace => LoadWorkspaceAsync(workspace, token))).ConfigureAwait(true);

        _content = new Dictionary<string, List<QuickPanelGroupViewModel>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < enabled.Count; i++)
        {
            if (loaded[i].Count > 0)
                _content[enabled[i].Id] = loaded[i];
        }

        _workspaces = enabled.Where(workspace => _content.ContainsKey(workspace.Id)).ToList();
        _activeTabId = ResolveActiveTabId(settings, processName);

        RebuildTabs();
        ShowActiveWorkspace();
    }

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
        var source = _workspaces
            .SelectMany(workspace => workspace.Folders)
            .FirstOrDefault(folder => folder.Id.Equals(group.SourceId, StringComparison.OrdinalIgnoreCase));
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

    /// <summary>One workspace's visible sources, in the order it stores, minus the ones that came back empty.</summary>
    private async Task<List<QuickPanelGroupViewModel>> LoadWorkspaceAsync(
        QuickPanelTab workspace, CancellationToken token)
    {
        var groups = new List<QuickPanelGroupViewModel>();

        foreach (var id in QuickPanelGroupOrdering.Resolve(
                     workspace.Folders.Select(folder => folder.Id),
                     workspace.GroupOrder,
                     workspace.DisabledGroupIds))
        {
            var source = workspace.Folders.FirstOrDefault(folder => folder.Id == id);
            if (source == null)
                continue;

            var group = await BuildGroupAsync(workspace, source, token).ConfigureAwait(true);
            if (group != null)
                groups.Add(group);
        }

        return groups;
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
