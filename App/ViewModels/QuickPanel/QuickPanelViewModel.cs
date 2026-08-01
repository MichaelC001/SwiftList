using System.Collections.ObjectModel;
using SwiftList.App.Services;
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
//
// See QuickPanelViewModelLoading for what a refresh actually loads, QuickPanelViewModelFilter for the
// box at the top, and QuickPanelViewModelWriteback for the two things the strip writes back.
public partial class QuickPanelViewModel : ViewModelBase
{
    private readonly Func<QuickPanelSettings> _readSettings;
    private readonly Func<QuickPanelFolderSource, CancellationToken, Task<List<SearchResult>>> _load;
    private readonly Action _saveSettings;

    /// <summary>The workspaces with a tab: enabled, and with something behind them.</summary>
    private List<QuickPanelTab> _workspaces = new();

    /// <summary>What each of those loaded, so switching between them is a swap rather than a reload.</summary>
    private Dictionary<string, List<QuickPanelGroupViewModel>> _content = new(StringComparer.OrdinalIgnoreCase);

    private string _activeTabId = string.Empty;

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

    /// <summary>One entry per visible source of the workspace on screen, each in its own order.</summary>
    public ObservableCollection<QuickPanelGroupViewModel> Groups { get; } = new();

    /// <summary>The strip only earns its space once there is more than one workspace to reach.</summary>
    public bool HasTabStrip => Tabs.Count > 1;

    /// <summary>Whether the workspace on screen has any group that takes files dropped onto it.</summary>
    /// <remarks>
    /// The panel dismisses itself when it loses the foreground, and dragging a file in from Explorer
    /// begins by clicking Explorer -- so a droppable panel would vanish before the drag it exists for
    /// ever started. While this is true the manager leaves it up, and the hotkey or Escape closes it.
    ///
    /// Not narrowed to "while a drag is running", which would be the better rule: the deactivation
    /// happens when the other window is clicked, which is before any drag exists to detect.
    /// </remarks>
    public bool AcceptsDrops => Groups.Any(group => group.AcceptsDrops);

    /// <summary>Whether there is anything worth opening the panel for.</summary>
    /// <remarks>
    /// Distinct from IsEmpty below, which they are easy to mistake for each other. This one gates
    /// whether the panel opens at all; that one is about the tab you are looking at once it has.
    /// </remarks>
    public bool HasContent => Groups.Count > 0 || Tabs.Count > 1;

    private bool _isEmpty = true;

    /// <summary>Whether the tab on screen has nothing in it, which the panel says rather than implies.</summary>
    public bool IsEmpty
    {
        get => _isEmpty;
        private set => SetProperty(ref _isEmpty, value);
    }

    /// <summary>
    /// Which workspace to open on: the one the app in front claims, else wherever the user left the
    /// panel, else the one the settings recorded, else the first there is.
    /// </summary>
    /// <remarks>
    /// The app in front outranks the remembered tab deliberately -- a workspace that names an app is a
    /// statement that this app means these folders, and honouring where the panel was last left instead
    /// would make that rule fire only when it happened to agree. Every candidate is checked against the
    /// workspaces that actually have a tab, so a claim on an empty one falls through like any other miss.
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
        _content = new Dictionary<string, List<QuickPanelGroupViewModel>>(StringComparer.OrdinalIgnoreCase);
        // The box on screen is emptied with everything else, so the next open is not silently filtered
        // by something typed into a panel that has since been away.
        SearchQuery = string.Empty;
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

    /// <summary>Switches the panel to another workspace. Everything is already loaded, so this is a swap.</summary>
    public Task SelectTabAsync(string tabId, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(tabId) || tabId == _activeTabId)
            return Task.CompletedTask;
        if (!_workspaces.Any(tab => tab.Id == tabId))
            return Task.CompletedTask;

        _activeTabId = tabId;
        foreach (var tab in Tabs)
            tab.IsSelected = tab.Id == tabId;

        ShowActiveWorkspace();
        return Task.CompletedTask;
    }

    /// <summary>Switches to the nth workspace in the strip, for the number-key shortcut. 1-based.</summary>
    public Task SelectTabAtAsync(int oneBasedIndex, CancellationToken token = default)
        => oneBasedIndex >= 1 && oneBasedIndex <= Tabs.Count
            ? SelectTabAsync(Tabs[oneBasedIndex - 1].Id, token)
            : Task.CompletedTask;

    /// <summary>Puts the active workspace's groups on screen, keeping whatever is typed in the box.</summary>
    private void ShowActiveWorkspace()
    {
        Groups.Clear();
        if (_content.TryGetValue(_activeTabId, out var groups))
        {
            foreach (var group in groups)
                Groups.Add(group);
        }

        // Switching workspace with something typed keeps the filter: the box is still on screen with
        // that text in it, and a strip that quietly showed everything again would be contradicting it.
        if (SearchQuery.Length > 0)
            ApplyFilter();
        else
            IsEmpty = Groups.Count == 0;

        OnPropertyChanged(nameof(HasContent));
        // Switching workspace can change the answer: one tab may take drops where the next does not.
        OnPropertyChanged(nameof(AcceptsDrops));
    }
}
