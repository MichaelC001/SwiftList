using System.IO;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.App.Services.PluginManagerCore;
using SwiftList.App.ViewModels.Settings.Plugins;
using SwiftList.Core;

using SwiftList.Core.Services.Search;

namespace SwiftList.App.ViewModels.Search.StartupPanel;

// One entry per candidate tab, built fresh on every StartupPanelController activation. A source that
// yields zero items is simply left out of the tab strip -- see StartupPanelController.TryActivateAsync.
internal interface ITabSource
{
    string Label { get; }

    // Stable identity for StartupPanel.TabOrder (persisted reordering) -- a synthetic
    // "__builtin::..." string for the two built-ins, PluginTabSource.ComponentId for a plugin tab.
    string Id { get; }

    // Hides this tab from the panel going forward (the x button). Deliberately distinct from a plugin
    // component being disabled -- see PluginTabSource.Close for why the two must not share storage.
    void Close();
    // Streaming, so a tab appears on its first item rather than when its slowest one arrives. A source
    // that yields nothing produces no tab at all, which is how empty tabs stay hidden without anyone
    // having to count first.
    IAsyncEnumerable<AppSearchResult> LoadItemsAsync(CancellationToken cancellationToken = default);
}

// The built-in "Recent Files" tab -- distinct from the plugin-provided sources below because it needs
// its own dedicated Settings sub-page (target directories, count, max age) and an IPC round-trip to
// the indexing service, neither of which fits the plugin model's "just hand back items" contract.
internal sealed class RecentFilesTabSource : ITabSource
{
    public const string SourceId = "__builtin::RecentFiles";

    private readonly SearchService _searchService;

    public RecentFilesTabSource(SearchService searchService) => _searchService = searchService;

    public string Label => TranslationManager.Instance["StartupPanel_TabRecentFiles"];
    public string Id => SourceId;

    public void Close()
    {
        var settings = UserSettings.Load();
        settings.StartupPanel.RecentFilesEnabled = false;
        settings.Save();
    }

    // The round trip to the indexing service is one call that either answers or does not, so the
    // streaming here begins after it: the wait is real and cannot be broken up, but the mapping that
    // follows can be, and the tab appears on the first mapped row rather than the last.
    public async IAsyncEnumerable<AppSearchResult> LoadItemsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var panelSettings = UserSettings.Load().StartupPanel;
        if (panelSettings.RecentFilesDirectories.Count == 0)
            yield break;

        var recentFiles = await _searchService.GetRecentFilesAsync(
            panelSettings.RecentFilesDirectories, panelSettings.RecentFilesCount, panelSettings.RecentFilesMaxAgeMinutes);

        for (var i = 0; i < recentFiles.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested) yield break;

            var uiResult = SearchResultHelper.CreateUiResult(recentFiles[i], string.Empty, i, isApplication: false, scope: null);
            var relativeTime = FormatRelativeTime(recentFiles[i].Metadata.Modified);
            if (!string.IsNullOrEmpty(relativeTime))
                uiResult.ParentDir = $"{relativeTime} - {uiResult.ParentDir}";

            yield return uiResult;
        }
    }

    // internal so the quick panel can render its second line the same way, for every tab rather than
    // just this one.
    internal static string FormatRelativeTime(DateTime modified)
    {
        if (modified == DateTime.MinValue) return string.Empty;

        var totalSeconds = (long)Math.Max(0, (DateTime.Now - modified).TotalSeconds);

        if (totalSeconds < 60)
            return string.Format(TranslationManager.Instance["StartupPanel_SecondsAgo"], totalSeconds);

        var totalMinutes = totalSeconds / 60;
        if (totalMinutes < 60)
            return string.Format(TranslationManager.Instance["StartupPanel_MinutesAgo"], totalMinutes);

        if (totalMinutes < 1440)
        {
            var hours = totalMinutes / 60;
            var minutes = totalMinutes % 60;
            return minutes == 0
                ? string.Format(TranslationManager.Instance["StartupPanel_HoursAgo"], hours)
                : string.Format(TranslationManager.Instance["StartupPanel_HoursMinutesAgo"], hours, minutes);
        }

        var days = totalMinutes / 1440;
        var remHours = (totalMinutes % 1440) / 60;
        return remHours == 0
            ? string.Format(TranslationManager.Instance["StartupPanel_DaysAgo"], days)
            : string.Format(TranslationManager.Instance["StartupPanel_DaysHoursAgo"], days, remHours);
    }
}

// The built-in "Last Directory" tab -- lists the contents of whatever folder a native file dialog (any
// app's, not just SwiftList's own UI) was last navigated to while SwiftList's dialog-interception hook
// was tracking it. Reads InlineSearchManager's own ExplorerTracker, which mirrors the Hook process's
// live state over IPC (see InlineSearchManager's ctor) -- no separate round trip needed since that
// tracker is already running by the time the quick window (and this tab) can exist.
internal sealed class LastDirectoryTabSource : ITabSource
{
    public const string SourceId = "__builtin::LastDirectory";

    // A folder can have far more entries than a startup-panel tab should ever show at once.
    private const int MaxItems = 100;

    public string Label => TranslationManager.Instance["StartupPanel_TabLastDirectory"];
    public string Id => SourceId;

    public void Close()
    {
        var settings = UserSettings.Load();
        settings.StartupPanel.LastDirectoryEnabled = false;
        settings.Save();
    }

    public async IAsyncEnumerable<AppSearchResult> LoadItemsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var path = InlineSearchManager.Instance.ExplorerTracker.LastActiveExplorerPath;
        // The Desktop is where Explorer/dialogs land by default when nothing more specific has been
        // browsed to yet, so treating it as "last visited" would make this tab show up constantly with
        // a location the user didn't actually navigate to.
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path) || IsCurrentUserDesktop(path))
            yield break;

        var drive = (Path.GetPathRoot(path) ?? string.Empty).TrimEnd('\\', ':');
        var exclusions = ExclusionRuleSet.From(UserSettings.Load());
        var index = 0;

        // Advanced by hand rather than with foreach, because a yield cannot sit inside a try that has a
        // catch, and the walk genuinely can throw part way through: a folder can be pulled away or its
        // permissions changed while it is being read. Guarding each step keeps whatever was already
        // yielded and stops there, where a guard around the whole loop would have to discard it.
        IEnumerator<FileSystemInfo>? entries = null;
        try
        {
            entries = new DirectoryInfo(path).EnumerateFileSystemInfos().GetEnumerator();
        }
        catch (Exception ex)
        {
            Logger.Log($"[LastDirectoryTabSource] Failed to list '{path}': {ex.Message}", LogLevel.Error);
            yield break;
        }

        using (entries)
        {
            while (index < MaxItems && !cancellationToken.IsCancellationRequested)
            {
                FileSystemInfo entry;
                try
                {
                    if (!entries.MoveNext()) break;
                    entry = entries.Current;
                }
                catch (Exception ex)
                {
                    Logger.Log($"[LastDirectoryTabSource] Stopped listing '{path}': {ex.Message}", LogLevel.Error);
                    break;
                }

                // A raw filesystem walk sees everything, unlike the real index (which skips hidden/system
                // entries and anything the user has excluded) -- apply the same two filters here so this
                // tab doesn't surface things like $RECYCLE.BIN that a normal search never would.
                if (FileSystemItemFilter.IsHiddenOrSystem(entry.Attributes))
                    continue;

                var isDir = entry is DirectoryInfo;
                if (exclusions.IsExcludedPath(entry.FullName, isDir))
                    continue;

                var item = new SearchResult
                {
                    Name = entry.Name,
                    Path = entry.FullName,
                    IsDir = isDir,
                    Drive = drive,
                    Attributes = entry.Attributes,
                    Metadata = new PluginSdk.Abstractions.FileMetadata(
                        entry is FileInfo fi ? fi.Length : 0,
                        entry.CreationTime,
                        entry.LastWriteTime,
                        entry.LastAccessTime),
                };

                yield return SearchResultHelper.CreateUiResult(item, string.Empty, index, isApplication: false, scope: null);
                index++;
            }
        }

        await Task.CompletedTask;
    }

    private static bool IsCurrentUserDesktop(string path)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return string.Equals(path.TrimEnd('\\'), desktop.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    }
}

// Wraps a plugin-contributed IStartupPanelTabProvider (see PluginSdk.Abstractions.Plugins). Closing this
// tab is a panel-local "don't show it for now" choice, not a plugin-level decision -- it writes to
// StartupPanel.ClosedTabIds, never to UserSettings.DisabledPluginComponents. That other list is a load-
// time gate: a provider disabled there never becomes a candidate tab at all (see
// PluginManager.StartupPanelTabProviders), so it can't reach this class in the first place. Conflating
// the two would mean closing one tab in the live panel silently re-labels the whole plugin component as
// "disabled" in the unrelated Plugin Management settings page.
internal sealed class PluginTabSource : ITabSource
{
    private readonly PluginSdk.Abstractions.Plugins.IStartupPanelTabProvider _provider;

    public PluginTabSource(PluginSdk.Abstractions.Plugins.IStartupPanelTabProvider provider) => _provider = provider;

    public string Label => _provider.Name;
    public string Id => ComponentId(_provider);

    // Shared with StartupPanelPluginTabViewModel, which reads/writes the same ClosedTabIds entries so
    // the Settings page's "reopen" checkboxes and this tab's x button agree on identity.
    public static string ComponentId(PluginSdk.Abstractions.Plugins.IStartupPanelTabProvider provider)
        => $"{ComponentFilter.GetDllName(provider)}::{PluginComponentType.StartupPanelTabProvider}::{provider.GetType().Name}";

    public void Close()
    {
        var settings = UserSettings.Load();
        var id = ComponentId(_provider);
        if (settings.StartupPanel.ClosedTabIds.Contains(id, StringComparer.OrdinalIgnoreCase))
            return;

        settings.StartupPanel.ClosedTabIds.Add(id);
        settings.Save();
    }

    public async IAsyncEnumerable<AppSearchResult> LoadItemsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Advanced by hand for the same reason the last-directory source is: a yield cannot sit inside a
        // try that has a catch, and a plugin can throw at any point in its enumeration rather than only
        // when it starts. A provider that fails half way keeps whatever it already yielded and loses the
        // rest, which costs its own tab and nothing else on the panel.
        IAsyncEnumerator<PluginSdk.Abstractions.ISearchResult> items;
        try
        {
            items = _provider.GetItemsAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.Log($"[PluginTabSource] {_provider.GetType().Name}.GetItemsAsync() failed: {ex.Message}", LogLevel.Error);
            yield break;
        }

        var index = 0;
        try
        {
            while (true)
            {
                PluginSdk.Abstractions.ISearchResult current;
                try
                {
                    if (!await items.MoveNextAsync().ConfigureAwait(true)) break;
                    current = items.Current;
                }
                catch (Exception ex)
                {
                    Logger.Log($"[PluginTabSource] {_provider.GetType().Name}.GetItemsAsync() failed: {ex.Message}", LogLevel.Error);
                    break;
                }

                // Shared with the quick panel, which shows the same plugins' entries: see
                // PluginResultMapper for why the mapping lives outside this class.
                yield return PluginResultMapper.ToUiResult(current, index);
                index++;
            }
        }
        finally
        {
            await items.DisposeAsync().ConfigureAwait(true);
        }
    }
}
