using SwiftList.Core;

namespace SwiftList.App.ViewModels.QuickPanel;

// The two things the panel does TO the settings rather than with them: closing a tab and dragging one.
// Split out purely to keep QuickPanelViewModel.cs under the repo's per-file line limit; it has no state
// of its own and only ever operates on the one view model it is part of.
//
// Everything else the panel offers is per-session -- a group's sort, its view, whether it is collapsed --
// and is deliberately not written anywhere. These two are, because they change what the strip IS rather
// than how one open of it looks: a closed tab that came back on the next summon, or an order that
// forgot itself, would not read as a preference at all.
//
// The caveat that comes with writing from here: the Quick Panel settings page edits a staged copy and
// puts it back on Save, so a page left open across one of these ends up overwriting it. Same rule as
// anywhere else two surfaces write one setting -- whoever saves last wins.
public partial class QuickPanelViewModel
{
    /// <summary>Writes the strip's order back over the workspaces it came from.</summary>
    /// <remarks>
    /// Disabled workspaces have no tab to drag, so they are not reordered by one either: the enabled
    /// workspaces are dealt back into the positions enabled workspaces already held, in the strip's new
    /// order, and everything else stays where it was. Sorting the whole list by the strip would have
    /// swept every disabled workspace to the end -- a change to a part of the settings that is not even
    /// visible from here.
    /// </remarks>
    private void PersistTabOrder()
    {
        // A drag is a remove followed by an insert, so the strip is briefly one tab short. That halfway
        // state is not an order worth writing, and it is exactly the one where the counts disagree.
        if (_rebuildingTabs || Tabs.Count != _workspaces.Count) return;

        var settings = _readSettings();
        var byId = settings.Tabs.ToDictionary(tab => tab.Id, StringComparer.OrdinalIgnoreCase);
        var reordered = settings.Tabs.ToList();
        var slots = settings.Tabs
            .Select((tab, index) => (tab, index))
            .Where(pair => pair.tab.Enabled)
            .Select(pair => pair.index)
            .ToList();
        if (slots.Count != Tabs.Count) return;

        for (var n = 0; n < slots.Count; n++)
        {
            if (!byId.TryGetValue(Tabs[n].Id, out var workspace)) return;
            reordered[slots[n]] = workspace;
        }

        settings.Tabs = reordered;
        _workspaces = reordered.Where(tab => tab.Enabled).ToList();
        _saveSettings();
    }

    /// <summary>Disables the workspace behind a tab and takes it out of the strip.</summary>
    /// <remarks>
    /// Disabled, not deleted. The sources behind a workspace took assembling, and closing a tab is a
    /// statement about the strip rather than about them -- the same thing the startup panel's own x
    /// means. The settings page is where one comes back.
    /// </remarks>
    public async Task CloseTabAsync(string tabId, CancellationToken token = default)
    {
        var settings = _readSettings();
        var workspace = settings.Tabs.FirstOrDefault(tab => tab.Id == tabId);
        if (workspace is not { Enabled: true }) return;

        workspace.Enabled = false;
        _saveSettings();

        _workspaces = settings.Tabs.Where(tab => tab.Enabled).ToList();
        // Closing the tab being looked at leaves the panel on the first one still there, rather than on
        // a workspace that no longer has a tab to reach it by.
        if (!_workspaces.Any(tab => tab.Id == _activeTabId))
            _activeTabId = _workspaces.Count > 0 ? _workspaces[0].Id : string.Empty;

        RebuildTabs();
        await LoadActiveWorkspaceAsync(token).ConfigureAwait(true);
    }
}
