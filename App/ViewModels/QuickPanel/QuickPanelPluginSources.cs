using SwiftList.App.Services.Plugin;
using SwiftList.PluginSdk.Abstractions.Plugins;

namespace SwiftList.App.ViewModels.QuickPanel;

/// <summary>
/// The plugin-provided quick panel sources, and the id each is known by.
/// </summary>
/// <remarks>
/// One place, because three surfaces have to agree on that id: the settings page listing what can be
/// added, the workspace remembering what was added, and the panel looking a provider back up when it
/// loads. The same shape PluginTabSource.ComponentId gives the startup panel's tabs, and the same
/// spelling, so a component reads the same in the settings file whichever panel it belongs to.
/// </remarks>
internal static class QuickPanelPluginSources
{
    /// <summary>Every provider the plugin layer currently offers, disabled components already dropped.</summary>
    public static IEnumerable<IQuickPanelSourceProvider> Available => PluginManager.Instance.QuickPanelSourceProviders;

    /// <summary>The id a provider is stored under: its dll, its kind, and its type name.</summary>
    public static string ComponentId(IQuickPanelSourceProvider provider)
        => $"{System.IO.Path.GetFileNameWithoutExtension(provider.GetType().Assembly.Location)}::QuickPanelSourceProvider::{provider.GetType().Name}";

    /// <summary>
    /// The provider behind an id, or null when there is not one any more.
    /// </summary>
    /// <remarks>
    /// A workspace can name a source whose plugin has since been uninstalled or switched off. The id
    /// stays in the settings rather than being pruned -- same rule the group order follows, so a plugin
    /// turned off for a week comes back where the user put it rather than at the end.
    /// </remarks>
    public static IQuickPanelSourceProvider? Find(string componentId)
        => Available.FirstOrDefault(p => ComponentId(p).Equals(componentId, StringComparison.OrdinalIgnoreCase));
}
