using System.Collections.ObjectModel;
using System.Windows;
using SwiftList.Core;

using SwiftList.Core.Services.Search;

using SwiftList.App.Services.Plugin;
namespace SwiftList.App.ViewModels.Search.StartupPanel;

/// <summary>
/// Owns the Startup Panel shown above the quick window's results when the search box is
/// empty: the tab strip (built-in "Recent Files" plus whatever IStartupPanelTabProvider plugins are
/// enabled) and the fetch that populates results through the same Results/ResultsControl pipeline a
/// normal search uses -- <paramref name="applyResults"/> is <see cref="SearchExecutionViewModel"/>'s
/// own ReplaceResults. A tab whose source returns zero items is left out of the strip entirely.
/// </summary>
public class StartupPanelController : ViewModelBase
{
    private sealed class ActiveTab
    {
        public required StartupPanelTabViewModel ViewModel { get; init; }
        public required ITabSource Source { get; init; }
        public required List<AppSearchResult> Items { get; init; }

        // Its position in the configured tab order, kept so a tab arriving late still lands where the
        // user put it rather than where it finished.
        public required int Rank { get; init; }
    }

    private readonly SearchService _searchService;
    private readonly Action<IEnumerable<AppSearchResult>> _applyResults;
    private readonly List<ActiveTab> _activeTabs = new();

    // Bumped every time the panel is (re)activated or deactivated so a fetch that's still in flight
    // when the user starts typing (or the panel is hidden/disabled) knows to discard its result.
    private int _requestId;

    // Remembers which tab the user last picked (by label, since ITabSource instances are rebuilt fresh
    // on every activation -- see BuildCandidateSources) so re-showing the window after hiding it on,
    // say, the "History" tab reopens onto History again instead of always resetting to the first tab.
    private string? _lastSelectedLabel;

    public StartupPanelController(SearchService searchService, Action<IEnumerable<AppSearchResult>> applyResults)
    {
        _searchService = searchService;
        _applyResults = applyResults;

        // Dragging a tab reorders this collection in place and touches nothing else, so the strip is
        // where a new order first exists. Everything this controller does to Tabs it does to _activeTabs
        // first, which is what the no-op check below recognises: only a drag can leave the two holding
        // the same tabs in a different order.
        Tabs.CollectionChanged += (_, _) => SyncStripOrder();
    }

    /// <summary>Follows a dragged strip: reorders the controller's own list and stores the new order.</summary>
    private void SyncStripOrder()
    {
        // Mid-drag the strip is one tab short (a remove and an insert), and at activation it is empty.
        // Neither is an order.
        if (Tabs.Count == 0 || Tabs.Count != _activeTabs.Count) return;

        var byViewModel = _activeTabs.ToDictionary(tab => tab.ViewModel);
        if (!Tabs.All(byViewModel.ContainsKey)) return;

        var reordered = Tabs.Select(vm => byViewModel[vm]).ToList();
        // Every other path here mutates _activeTabs first, so it already agrees and there is nothing to
        // store. Without this, streaming a tab in would write the order once per tab.
        if (reordered.SequenceEqual(_activeTabs)) return;

        _activeTabs.Clear();
        _activeTabs.AddRange(reordered);

        var settings = UserSettings.Load();
        settings.StartupPanel.TabOrder = StartupPanelTabReorder.Apply(
            Tabs.Select(tab => tab.Id), settings.StartupPanel.TabOrder);
        settings.Save();
    }

    public ObservableCollection<StartupPanelTabViewModel> Tabs { get; } = new();

    private Visibility _visibility = Visibility.Collapsed;
    public Visibility Visibility
    {
        get => _visibility;
        set => SetProperty(ref _visibility, value);
    }

    /// <summary>Streams every enabled tab source at once, each tab appearing with its first item and
    /// filling in after. Returns whether the panel ended up with anything at all.</summary>
    public async Task<bool> TryActivateAsync()
    {
        if (!UserSettings.Load().StartupPanel.Enabled)
        {
            _requestId++; // invalidate any fetch still in flight from a prior activation
            Visibility = Visibility.Collapsed;
            return false;
        }

        var requestId = ++_requestId;
        var sources = BuildCandidateSources();

        _activeTabs.Clear();
        Tabs.Clear();

        // Every source is consumed at once and each tab appears when its own first item arrives, rather
        // than the panel waiting for the slowest of them. A source that yields nothing never creates a
        // tab, which is how empty tabs stay hidden now that nothing counts them up front.
        //
        // The whole set is still awaited before returning, because the caller's answer is "did the panel
        // end up with anything", but the panel is already visible and filling in by then.
        await Task.WhenAll(sources.Select((source, rank) => StreamTabAsync(source, rank, requestId)));

        if (requestId != _requestId)
            return false; // superseded by a newer activation/deactivation while fetching

        if (_activeTabs.Count == 0)
        {
            Visibility = Visibility.Collapsed;
            return false;
        }

        return true;
    }

    /// <summary>Consumes one source, creating its tab on the first item and appending the rest.</summary>
    /// <remarks>
    /// Each append re-applies the selected tab's whole list rather than pushing single items: the apply
    /// callback is the same one a real search uses and it replaces results wholesale. For a startup
    /// panel's handful of rows that costs nothing, and it keeps this from needing a second, incremental
    /// path through the result list that only this feature would use.
    /// </remarks>
    private async Task StreamTabAsync(ITabSource source, int rank, int requestId)
    {
        ActiveTab? tab = null;

        await foreach (var item in source.LoadItemsAsync().ConfigureAwait(true))
        {
            if (requestId != _requestId) return;

            if (tab == null)
            {
                var tabVm = new StartupPanelTabViewModel(source.Label, () => CloseTab(source), () => SelectTab(source), source.Id);
                tab = new ActiveTab { ViewModel = tabVm, Source = source, Rank = rank, Items = new List<AppSearchResult>() };

                // Inserted at its configured position, not appended. Tabs now appear in the order their
                // first items happen to arrive, so appending would let a fast source outrank a slow one
                // and quietly replace the user's own TabOrder with a race.
                var at = _activeTabs.FindIndex(t => t.Rank > rank);
                if (at < 0) at = _activeTabs.Count;
                _activeTabs.Insert(at, tab);
                Tabs.Insert(at, tabVm);

                Visibility = Visibility.Visible;

                // The remembered tab wins if it turns up, but until it does the first one to arrive is
                // selected so the panel is never visible with nothing chosen. Selecting again when the
                // remembered one appears is what makes the preference survive being slower than another.
                if (_activeTabs.Count == 1 || source.Label == _lastSelectedLabel)
                    SelectTab(source);
            }

            tab.Items.Add(item);

            if (tab.ViewModel.IsSelected)
                _applyResults(tab.Items);
        }
    }

    /// <summary>Hides the panel (an explorer-jump suggestion is taking its slot, a real query started,
    /// or the window is closing) and discards any in-flight fetch.</summary>
    public void Deactivate()
    {
        _requestId++;
        Visibility = Visibility.Collapsed;
    }

    private List<ITabSource> BuildCandidateSources()
    {
        var sources = new List<ITabSource>();
        if (UserSettings.Load().StartupPanel.RecentFilesEnabled)
            sources.Add(new RecentFilesTabSource(_searchService));
        if (UserSettings.Load().StartupPanel.LastDirectoryEnabled)
            sources.Add(new LastDirectoryTabSource());

        // StartupPanelTabProviders already excludes plugin components disabled via Plugin Management;
        // ClosedTabIds is the separate, panel-local "user hid this one" list -- see PluginTabSource.Close.
        var closedIds = UserSettings.Load().StartupPanel.ClosedTabIds;
        foreach (var provider in PluginManager.Instance.StartupPanelTabProviders)
        {
            if (!closedIds.Contains(PluginTabSource.ComponentId(provider), StringComparer.OrdinalIgnoreCase))
                sources.Add(new PluginTabSource(provider));
        }

        // Reordered per StartupPanel.TabOrder (position = priority, most-preferred first), covering
        // both built-ins and plugin tabs -- a source whose id isn't listed there yet falls back to
        // int.MaxValue, which (List<T>.Sort/OrderBy are both stable) lands it after every listed source
        // while preserving its built-in-then-plugin-discovery-order position relative to any OTHER
        // unlisted source, rather than an arbitrary reshuffle. Same pattern as
        // PluginManager.QuickNavigationProviders' own ordering.
        var order = UserSettings.Load().StartupPanel.TabOrder;
        return sources
            .OrderBy(s =>
            {
                var rank = order.IndexOf(s.Id);
                return rank >= 0 ? rank : int.MaxValue;
            })
            .ToList();
    }


    /// <summary>Moves the selection to the next tab, wrapping from the last back to the first. A no-op
    /// with 0 or 1 active tabs (nothing to cycle to).</summary>
    public void SelectNextTab() => ShiftSelectedTab(1);

    /// <summary>Moves the selection to the previous tab, wrapping from the first back to the last.</summary>
    public void SelectPreviousTab() => ShiftSelectedTab(-1);

    private void ShiftSelectedTab(int direction)
    {
        if (_activeTabs.Count < 2)
            return;

        var currentIndex = _activeTabs.FindIndex(t => t.ViewModel.IsSelected);
        if (currentIndex < 0)
            currentIndex = 0;

        var nextIndex = (currentIndex + direction + _activeTabs.Count) % _activeTabs.Count;
        SelectTab(_activeTabs[nextIndex].Source);
    }

    private void SelectTab(ITabSource source)
    {
        var match = _activeTabs.FirstOrDefault(t => ReferenceEquals(t.Source, source));
        if (match == null)
            return;

        foreach (var tab in _activeTabs)
            tab.ViewModel.IsSelected = ReferenceEquals(tab, match);

        _lastSelectedLabel = match.Source.Label;
        _applyResults(match.Items);
    }

    private void CloseTab(ITabSource source)
    {
        source.Close();

        var match = _activeTabs.FirstOrDefault(t => ReferenceEquals(t.Source, source));
        if (match == null)
            return;

        var wasSelected = match.ViewModel.IsSelected;
        _activeTabs.Remove(match);
        Tabs.Remove(match.ViewModel);

        if (_activeTabs.Count == 0)
        {
            Visibility = Visibility.Collapsed;
            _applyResults(Array.Empty<AppSearchResult>());
            return;
        }

        if (wasSelected)
            SelectTab(_activeTabs[0].Source);
    }
}
