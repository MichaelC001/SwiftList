using System.Collections.ObjectModel;
using SwiftList.App.Services;
using SwiftList.App.ViewModels.Search;
using SwiftList.Core;
using SwiftList.Core.Services.QuickPanel;

namespace SwiftList.App.ViewModels.QuickPanel;

// Backs the quick panel: one workspace at a time, its sources shown as groups, over whatever window is
// in front.
//
// Everything it shows comes from QuickPanelSettings -- which workspaces exist, which of their sources
// are visible and in what order, and what each group is called. Loading a source's entries is
// QuickPanelSourceLoader's job, and both it and the settings arrive through the constructor so the
// assembly logic here can be tested without an index behind it.
public partial class QuickPanelViewModel : ViewModelBase
{
    private readonly Func<QuickPanelSettings> _readSettings;
    private readonly Func<QuickPanelFolderSource, CancellationToken, Task<List<SearchResult>>> _load;

    /// <summary>The enabled workspaces, as of the last refresh.</summary>
    private List<QuickPanelTab> _workspaces = new();

    private string _activeTabId = string.Empty;

    private readonly Action _saveSettings;

    public QuickPanelViewModel(
        Func<QuickPanelSettings>? readSettings = null,
        Func<QuickPanelFolderSource, CancellationToken, Task<List<SearchResult>>>? load = null,
        Action? saveSettings = null)
    {
        _readSettings = readSettings ?? (() => UserSettings.Load().QuickPanel);
        _load = load ?? QuickPanelSourceLoader.LoadAsync;
        _saveSettings = saveSettings ?? (() => UserSettings.Load().Save());

        // Dragging a tab reorders this collection in place (a remove and an insert, which is what
        // DragReorder does to any IList), so the strip is where a new order first exists and this is the
        // only place that can notice it.
        Tabs.CollectionChanged += (_, _) => PersistTabOrder();
    }

    /// <summary>The workspace strip. Empty until the first refresh, and rebuilt by every one after.</summary>
    public ObservableCollection<QuickPanelTabViewModel> Tabs { get; } = new();

    /// <summary>One entry per visible source, each holding its own items in its own order.</summary>
    public ObservableCollection<QuickPanelGroupViewModel> Groups { get; } = new();

    /// <summary>The strip only earns its space once there is more than one workspace to reach.</summary>
    public bool HasTabStrip => Tabs.Count > 1;

    /// <summary>Reloads before the panel is shown, and picks the workspace the app in front claims.</summary>
    /// <remarks>
    /// Every open, not once: the panel shows what is recent and where you currently are, and it is
    /// reused rather than rebuilt, so a version that loaded at construction would keep showing whatever
    /// was true the first time it was ever opened. Awaited before the panel opens, not after, because
    /// whether it opens at all depends on what this turns up.
    /// </remarks>
    public async Task RefreshAsync(string? processName = null, CancellationToken token = default)
    {
        var settings = _readSettings();
        // Disabled workspaces are dropped here rather than filtered at every use: a workspace that has
        // no tab must also not be reachable by a process rule or by the number keys.
        _workspaces = settings.Tabs.Where(tab => tab.Enabled).ToList();
        _activeTabId = ResolveActiveTabId(settings, processName);

        RebuildTabs();
        await LoadActiveWorkspaceAsync(token).ConfigureAwait(true);
    }

    /// <summary>
    /// Which workspace to open on: the one the app in front claims, else wherever the user left the
    /// panel, else the one the settings recorded, else the first there is.
    /// </summary>
    /// <remarks>
    /// The app in front outranks the remembered tab deliberately -- a workspace that names an app is a
    /// statement that this app means these folders, and honouring where the panel was last left instead
    /// would make that rule fire only when it happened to agree.
    /// </remarks>
    private string ResolveActiveTabId(QuickPanelSettings settings, string? processName)
    {
        var claimed = QuickPanelTabSelection.SelectTabId(processName, _workspaces);
        if (claimed != null)
            return claimed;
        if (Contains(_activeTabId))
            return _activeTabId;
        if (Contains(settings.ActiveTabId))
            return settings.ActiveTabId;
        return _workspaces.Count > 0 ? _workspaces[0].Id : string.Empty;

        bool Contains(string id) => !string.IsNullOrEmpty(id)
            && _workspaces.Any(tab => tab.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    private bool _rebuildingTabs;

    private void RebuildTabs()
    {
        // Rebuilding empties and refills the strip one item at a time, and every one of those steps is a
        // collection change like any other. Without this the first Clear would persist an empty order.
        _rebuildingTabs = true;
        try
        {
            Tabs.Clear();
            foreach (var workspace in _workspaces)
            {
                // Captured by value: the workspace list is replaced wholesale on every refresh, so a
                // command that closed over the object would keep the old one alive and act on a stale
                // copy.
                var id = workspace.Id;
                Tabs.Add(new QuickPanelTabViewModel(
                    id, NameOf(workspace), () => _ = SelectTabAsync(id), () => _ = CloseTabAsync(id))
                {
                    IsSelected = id == _activeTabId,
                });
            }
        }
        finally
        {
            _rebuildingTabs = false;
        }
        OnPropertyChanged(nameof(HasTabStrip));
    }

    private static string NameOf(QuickPanelTab workspace) => string.IsNullOrWhiteSpace(workspace.Name)
        ? TranslationManager.Instance["QuickPanel_DefaultTabName"]
        : workspace.Name.Trim();

    /// <summary>Drops everything on screen, for when the panel is hidden.</summary>
    /// <remarks>
    /// The window is hidden and reused rather than closed, so without this it keeps the last workspace's
    /// groups behind it until the next refresh replaces them -- and anything that gets a frame in before
    /// that lands shows another workspace's files rather than nothing.
    ///
    /// Which workspace was active is deliberately kept: that is where the panel reopens, and it is the
    /// one piece of this that is not on screen.
    /// </remarks>
    public void Clear()
    {
        Groups.Clear();
        // Under the same flag a rebuild uses: emptying the strip on the way out is not the user putting
        // the workspaces in a new order, and it is the one order that must never be written.
        _rebuildingTabs = true;
        try
        {
            Tabs.Clear();
        }
        finally
        {
            _rebuildingTabs = false;
        }
        IsEmpty = true;
        OnPropertyChanged(nameof(HasTabStrip));
        OnPropertyChanged(nameof(HasContent));
    }

    /// <summary>Switches the panel to another workspace, reloading it. Nothing else about the panel moves.</summary>
    public async Task SelectTabAsync(string tabId, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(tabId) || tabId == _activeTabId)
            return;
        if (!_workspaces.Any(tab => tab.Id == tabId))
            return;

        _activeTabId = tabId;
        foreach (var tab in Tabs)
            tab.IsSelected = tab.Id == tabId;

        await LoadActiveWorkspaceAsync(token).ConfigureAwait(true);
    }

    /// <summary>Switches to the nth workspace in the strip, for the number-key shortcut. 1-based.</summary>
    public Task SelectTabAtAsync(int oneBasedIndex, CancellationToken token = default)
        => oneBasedIndex >= 1 && oneBasedIndex <= Tabs.Count
            ? SelectTabAsync(Tabs[oneBasedIndex - 1].Id, token)
            : Task.CompletedTask;

    private async Task LoadActiveWorkspaceAsync(CancellationToken token)
    {
        var workspace = _workspaces.FirstOrDefault(tab => tab.Id == _activeTabId);
        var loaded = new List<QuickPanelGroupViewModel>();

        if (workspace != null)
        {
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
                    loaded.Add(group);
            }
        }

        // Swapped in at the end rather than cleared first: switching workspaces happens with the panel
        // open, and clearing up front would empty it for the length of the load.
        Groups.Clear();
        foreach (var group in loaded)
            Groups.Add(group);

        IsEmpty = Groups.Count == 0;
        OnPropertyChanged(nameof(HasContent));
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
            preference?.Expanded ?? true);
    }

    private static string TitleOf(QuickPanelFolderSource source, QuickPanelGroupPreference? preference)
        => string.IsNullOrWhiteSpace(preference?.DisplayName)
            ? QuickPanelFolderSource.DefaultName(source.Path)
            : preference!.DisplayName.Trim();

    /// <summary>Whether there is anything worth opening the panel for.</summary>
    /// <remarks>
    /// Distinct from IsEmpty below, which they are easy to mistake for each other. This one gates
    /// whether the panel opens at all; that one is about the tab you are looking at once it has. A panel
    /// with three tabs where the selected one happens to be empty should still open, and say so -- the
    /// other tabs are one click away and only the panel can show them.
    /// </remarks>
    public bool HasContent => Groups.Count > 0 || Tabs.Count > 1;

    private bool _isEmpty = true;

    /// <summary>Whether the tab on screen has nothing in it, which the panel says rather than implies.</summary>
    public bool IsEmpty
    {
        get => _isEmpty;
        private set => SetProperty(ref _isEmpty, value);
    }

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
