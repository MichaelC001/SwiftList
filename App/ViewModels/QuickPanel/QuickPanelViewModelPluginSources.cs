using SwiftList.Core;

namespace SwiftList.App.ViewModels.QuickPanel;

// Loading a plugin-provided source. Split out of QuickPanelViewModelLoading.cs purely to keep that file
// under the repo's per-file line limit; it is the one branch of the load that does not go near a folder.
public partial class QuickPanelViewModel
{
    /// <summary>One plugin-provided source, asked for its entries and dressed as a group.</summary>
    /// <remarks>
    /// No path, so no path beside the heading and no drops: the drop check already requires a real
    /// directory, so a plugin group refuses one without knowing anything about plugins.
    ///
    /// Ordered newest-first by default, the same as any source whose kind is not "by name" -- a plugin
    /// has no kind dropdown to answer that with, and a list of recent things is what these tend to be.
    /// The group's own sort toggle still overrides it for the session, as everywhere else.
    /// </remarks>
    private async Task<QuickPanelGroupViewModel?> BuildPluginGroupAsync(
        QuickPanelTab workspace, string componentId, CancellationToken token)
    {
        var provider = QuickPanelPluginSources.Find(componentId);
        if (provider == null) return null;

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
            // A provider that throws costs its own group and nothing else, exactly as an unreachable
            // folder does.
            Logger.Log($"[QuickPanel] Plugin source '{componentId}' failed: {ex.Message}", LogLevel.Error);
            return null;
        }

        if (entries.Count == 0) return null;

        // Mapped rather than cast: a provider lives in a plugin assembly that cannot see AppSearchResult
        // at all, so what arrives is always some ISearchResult of its own making. The same mapping the
        // startup panel's plugin tabs go through.
        //
        // A provider that fills in Metadata gets sorted by it; one that does not keeps the order it
        // returned, since a descending sort over values that are all "unknown" is stable and leaves them
        // as they came. So "newest first" costs a provider nothing it has not already worked out.
        var items = entries
            .Select((entry, index) => (
                Item: Helpers.PluginResultMapper.ToUiResult(entry, index),
                Modified: entry.Metadata.Modified is var modified && modified != DateTime.MinValue ? modified : (DateTime?)null))
            .ToList();

        workspace.GroupPreferences.TryGetValue(componentId, out var preference);

        return new QuickPanelGroupViewModel(
            componentId,
            string.IsNullOrWhiteSpace(preference?.DisplayName) ? provider.Name : preference!.DisplayName.Trim(),
            string.Empty,
            items,
            QuickPanelSortMode.ModifiedDescending,
            preference?.ThumbnailView ?? true,
            preference?.Expanded ?? true);
    }
}
