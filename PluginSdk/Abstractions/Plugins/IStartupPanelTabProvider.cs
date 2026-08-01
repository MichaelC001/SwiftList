namespace SwiftList.PluginSdk.Abstractions.Plugins;

/// <summary>
/// Contributes a tab to the quick window's Startup Panel, shown above the result list
/// when the search box is empty. The host renders whatever items are returned through its own result
/// list (icons, open, actions menu all come for free) -- a provider only needs to say what to show
/// right now. A tab whose items are empty is hidden automatically; there's nothing else to configure.
/// </summary>
public interface IStartupPanelTabProvider : IPluginComponent
{

    /// <summary>
    /// Yields the items to show right now. Called each time the panel is (re)activated.
    /// </summary>
    /// <remarks>
    /// Streaming rather than returning a finished set, because the panel does not wait for it: the tab
    /// appears when the first item arrives and fills in as the rest do. A provider that has to go and
    /// look therefore costs only its own tab's completeness, never the panel's appearance, and a
    /// provider that yields nothing at all never produces a tab.
    ///
    /// A provider with everything already in memory can yield straight from a list and pays nothing for
    /// the shape. The token is cancelled when the panel closes or is reactivated, so a long enumeration
    /// should honour it rather than run to completion for a panel nobody is looking at.
    /// </remarks>
    IAsyncEnumerable<ISearchResult> GetItemsAsync(CancellationToken cancellationToken = default);
}
