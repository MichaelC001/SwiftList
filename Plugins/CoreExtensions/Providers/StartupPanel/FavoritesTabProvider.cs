using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.CoreExtensions.Providers.StartupPanel;

// Startup-panel tab backed by the host's own favorites (FavoritesService, already wired to
// SwiftList.Core.UserSettings.Favorites). These are user-curated and usually few, so no cap.
public class FavoritesTabProvider : IStartupPanelTabProvider
{
    public string Name => TranslationService.Get("StartupPanel_TabFavorites");

    // Everything is already in memory, so this yields straight through: the streaming shape costs a
    // provider with nothing to wait for exactly nothing.
    public async IAsyncEnumerable<ISearchResult> GetItemsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var favorite in FavoritesService.GetFavorites())
        {
            if (cancellationToken.IsCancellationRequested) yield break;
            if (string.IsNullOrWhiteSpace(favorite.Path)) continue;

            yield return new PanelResultItem(favorite.Path, favorite.Name);
        }

        await Task.CompletedTask;
    }
}
