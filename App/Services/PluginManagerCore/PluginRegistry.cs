using SwiftList.PluginSdk.Abstractions.Plugins;

using SwiftList.PluginSdk.Abstractions.Plugins.Preview;
using SwiftList.PluginSdk.Abstractions.Plugins.WindowAdapters;
namespace SwiftList.App.Services.PluginManagerCore;

/// <summary>
/// Callback interface used by <see cref="PluginLoader"/> to register discovered
/// plugin components back into the owning <see cref="PluginManager"/>.
/// </summary>
internal interface PluginRegistry
{
    void RegisterPlugin(IPlugin plugin);
    void AddInstantResultProvider(IInstantResultProvider provider);
    void AddSearchableItemProvider(ISearchableItemProvider provider);
    void AddSidebarFilterProvider(ISidebarFilterProvider provider);
    void AddResultColumnProvider(IResultColumnProvider provider);
    void AddTranslationProvider(ITranslationProvider provider);
    void AddThemeProvider(IThemeProvider provider);
    void AddActivePathCollector(IActivePathCollector provider);
    void AddFilePreviewProvider(IFilePreviewProvider provider);
    void AddQuickNavigationProvider(IQuickNavigationProvider provider);
    void AddThumbnailProvider(IThumbnailProvider provider);
    void AddQueryTokenProvider(IQueryTokenProvider provider);
    void AddStartupPanelTabProvider(IStartupPanelTabProvider provider);
    void AddQuickPanelTabProvider(IQuickPanelTabProvider provider);
}
