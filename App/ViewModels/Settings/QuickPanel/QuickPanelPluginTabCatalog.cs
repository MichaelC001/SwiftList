using SwiftList.App.ViewModels.QuickPanel;
using SwiftList.Core;

namespace SwiftList.App.ViewModels.Settings.QuickPanel;

/// <summary>The plugin tabs the settings page lists, and whether each is currently in the strip.</summary>
/// <remarks>
/// A thin read over QuickPanelPluginTabs so the settings side never talks to PluginManager directly.
/// Read fresh each time the page is built rather than held: plugins can be enabled or disabled on their
/// own page, and a stale list would offer a tab that is no longer there or hide one that just appeared.
/// </remarks>
internal static class QuickPanelPluginTabCatalog
{
    /// <summary>Every plugin tab that exists right now, each saying whether the strip shows it.</summary>
    public static List<QuickPanelPluginTabOption> Available(QuickPanelSettings settings)
        => QuickPanelPluginTabs.Available
            .Select(provider =>
            {
                var id = QuickPanelPluginTabs.ComponentId(provider);
                var open = !settings.ClosedPluginTabIds.Contains(id, StringComparer.OrdinalIgnoreCase);
                return new QuickPanelPluginTabOption(id, provider.Name, open);
            })
            .ToList();
}
