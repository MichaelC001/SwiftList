using System.IO;
using SwiftList.PluginSdk.Abstractions;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.CoreExtensions.Providers.StartupPanel;

// Startup-panel tab backed by the host's own search history (HistoryService, already wired to
// SwiftList.Core.SearchHistoryStore, most-recent-first). Capped at 20 -- unlike Recent Files, there's
// no dedicated settings page for this tab to make that configurable.
public class HistoryTabProvider : IStartupPanelTabProvider
{
    private const int MaxItems = 20;

    public string Name => TranslationService.Get("StartupPanel_TabHistory");

    // Streaming earns its keep here: every entry is checked against the disk, and a stale history full
    // of paths on a disconnected network drive turns each of those checks into a timeout. Yielded one
    // at a time, the tab shows what has been confirmed so far rather than nothing until the slowest
    // entry gives up.
    public async IAsyncEnumerable<ISearchResult> GetItemsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var yielded = 0;

        foreach (var entry in HistoryService.GetHistoryEntries())
        {
            if (cancellationToken.IsCancellationRequested || yielded >= MaxItems) yield break;

            var isApplication = entry.Kind == HistoryEntryKind.Application;
            if (!isApplication && !File.Exists(entry.Path) && !Directory.Exists(entry.Path)) continue;

            yielded++;
            yield return new StartupPanelResultItem(entry.Path, isApplication: isApplication);
        }

        await Task.CompletedTask;
    }
}
