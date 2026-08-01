namespace SwiftList.PluginSdk.Abstractions.Plugins;

/// <summary>
/// Contributes a source to the Quick Panel -- the floating panel docked over whatever window is in
/// front. A source becomes one group in the panel, with its own heading, and the host renders the
/// entries through its own result rows (icons, open, the actions menu all come for free).
/// </summary>
/// <remarks>
/// Deliberately not the same shape as <see cref="IStartupPanelTabProvider"/>, which streams. That panel
/// shows a tab the moment its first item arrives and fills it in afterwards; this one orders and caps a
/// source's entries as a set (newest first, or by name, and at most so many), so it cannot show half of
/// one without re-sorting the group and re-deciding what the cap keeps on every arrival. A finished set
/// is therefore what it asks for, and what it can honestly use.
///
/// That costs nothing in latency: every source of every workspace loads on its own task and the panel
/// opens on the first one to arrive, so a provider that has to go and look delays only its own group.
/// It should still honour the token -- it is cancelled when the panel closes, and a provider that runs
/// on regardless is working for a window nobody is looking at.
///
/// A source that returns nothing produces no group, and a workspace whose sources all return nothing
/// gets no tab. There is nothing to configure for that.
///
/// Where a source appears is the user's: they add it to whichever workspaces they want, and each of
/// those remembers its own position, whether it is hidden, what it is called and how it is displayed --
/// all keyed by this provider's component id, alongside the user's own folders.
/// </remarks>
public interface IQuickPanelSourceProvider : IPluginComponent
{
    /// <summary>The entries to show right now. Called each time the panel is summoned.</summary>
    Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default);
}
