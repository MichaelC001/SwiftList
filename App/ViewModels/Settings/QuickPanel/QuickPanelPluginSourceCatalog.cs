using SwiftList.App.ViewModels.QuickPanel;

namespace SwiftList.App.ViewModels.Settings.QuickPanel;

/// <summary>What the settings page can offer to add, and what to call an id it already holds.</summary>
/// <remarks>
/// A thin read over QuickPanelPluginSources so the settings side never talks to PluginManager directly:
/// the page has to answer one more question than the panel does -- what a workspace's stored id is
/// called when the plugin behind it is gone -- and that answer belongs next to the list, not scattered
/// through the view models that need it.
/// </remarks>
internal static class QuickPanelPluginSourceCatalog
{
    /// <summary>Every plugin source that can be added right now, as (id, name).</summary>
    public static List<(string Id, string Name)> Available()
        => QuickPanelPluginSources.Available
            .Select(provider => (QuickPanelPluginSources.ComponentId(provider), provider.Name))
            .ToList();

    /// <summary>
    /// What to call a stored id. The provider's own name while it is there, and the id itself once it
    /// is not -- a workspace keeps a source whose plugin is switched off or uninstalled, so the row has
    /// to say something rather than go blank, and the id is the only truthful thing left to say.
    /// </summary>
    public static string NameOf(string componentId)
        => QuickPanelPluginSources.Find(componentId)?.Name ?? componentId;
}
