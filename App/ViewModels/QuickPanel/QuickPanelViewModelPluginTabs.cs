using SwiftList.Core;
using SwiftList.PluginSdk.Abstractions.Plugins;

namespace SwiftList.App.ViewModels.QuickPanel;

// Loading a plugin's tab. Split out of QuickPanelViewModelLoading.cs purely to keep that file under the
// repo's per-file line limit; it is the one part of the load that never goes near a folder.
public partial class QuickPanelViewModel
{
    /// <summary>Asks a provider for its list and files it as this tab's one and only group.</summary>
    /// <remarks>
    /// One group, and it wears no heading: the tab is already named after it, and a heading repeating
    /// that name would be the panel saying the same word twice. The rest of the header stays, since the
    /// sort and view toggles apply here exactly as they do to a folder.
    ///
    /// No path, so no drops -- the drop check already requires a real directory, which means a plugin tab
    /// refuses one without anybody having to know it is a plugin tab.
    ///
    /// Ordered newest-first by default, the same as any source whose kind is not "by name": a provider
    /// has no kind dropdown to answer that with, and a list of recent things is what these tend to be.
    /// Entries with no known time keep the order the provider returned them in.
    /// </remarks>
    internal async Task LoadPluginTabAsync(
        IQuickPanelTabProvider provider,
        string id,
        string name,
        Action<QuickPanelGroupViewModel, int> place,
        CancellationToken token)
    {
        IReadOnlyList<PluginSdk.Abstractions.ISearchResult> entries;
        try
        {
            entries = await provider.GetEntriesAsync(token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A provider that throws costs its own tab and nothing else, exactly as an unreachable folder
            // costs its own group.
            Logger.Log($"[QuickPanel] Plugin tab '{id}' failed: {ex.Message}", LogLevel.Error);
            return;
        }

        if (entries.Count == 0) return;

        // Mapped rather than cast: a provider lives in a plugin assembly that cannot see AppSearchResult
        // at all, so what arrives is always some ISearchResult of its own making.
        var items = entries
            .Select((entry, index) => (
                Item: Helpers.PluginResultMapper.ToUiResult(entry, index),
                Modified: entry.Metadata.Modified is var modified && modified != DateTime.MinValue ? modified : (DateTime?)null))
            .ToList();

        place(new QuickPanelGroupViewModel(id, name, string.Empty, items, showsHeading: false), 0);
    }
}
