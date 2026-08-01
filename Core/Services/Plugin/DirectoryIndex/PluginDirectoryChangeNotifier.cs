using SwiftList.Core.DriveMonitoring;

using SwiftList.Core.Indexer.NetworkDrive;

using SwiftList.Core.Services.Network;

using SwiftList.Core.Services.Search;
namespace SwiftList.Core.Services.Plugin.DirectoryIndex;

/// <summary>
/// Turns "something under a plugin's registered directory changed" into at most one notification per
/// quiet period, from two kinds of source at once: the per-directory FileSystemWatcher this registry
/// already runs, and the indexes themselves reporting that they just took an update in.
/// <para>
/// The index half exists because a watcher event and the index are not in step: a watcher fires the
/// instant the filesystem changes, while the USN journal is read on a poll, so a plugin that re-listed
/// immediately could read an index that has not caught up yet and miss the file that triggered it. An
/// index signal cannot be early by construction -- it IS the index having changed -- and the debounce
/// below settles the two into one refresh. It also covers a stretch where the watcher itself was down
/// (buffer overflow, share disconnected), which today is only noticed on its next reconnect.
/// </para>
/// <para>
/// The watcher half stays because it is the only signal for a directory no index covers -- a drive
/// indexing is off for, an unconfigured share, a path that does not exist yet. Everywhere else the
/// index sees every kind of change, edits to a file's contents included: those refresh its size and
/// timestamps (see UsnIndexerExtensions' metadata pass) and bump the drive's revision like any other.
/// </para>
/// </summary>
internal sealed class PluginDirectoryChangeNotifier : IDisposable
{
    // Long enough to outlast the USN monitor's own poll (200ms-1s, see UsnMonitor) so a watcher event
    // and the index update it will produce collapse into ONE notification, taken after the index has
    // caught up rather than before. Each new event restarts it, so a bulk copy notifies once, at the
    // end, instead of once per file.
    private const int QuietPeriodMs = 1200;

    private readonly KeyedDebouncer<string> _debouncer = new(QuietPeriodMs, StringComparer.OrdinalIgnoreCase);
    private readonly Func<IReadOnlyList<(string PluginId, string Path)>> _registrations;
    private readonly Dictionary<string, long> _lastLocalRevisions = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private CancellationTokenSource? _localStatusCts;
    private bool _subscribedToNetwork;

    public PluginDirectoryChangeNotifier(Func<IReadOnlyList<(string PluginId, string Path)>> registrations)
        => _registrations = registrations;

    /// <summary>One notification for this plugin once its directories have been quiet for a moment.</summary>
    public void Report(string pluginId)
        => _debouncer.Schedule(pluginId, () => PluginSdk.Services.DirectoryIndexerService.NotifyDirectoryChanged(pluginId));

    /// <summary>
    /// Starts listening to the indexes, if not already. Called when a directory is registered rather
    /// than from the constructor: with nothing registered there is nobody to notify, and the local half
    /// holds a pipe subscription open for as long as it runs.
    /// </summary>
    public void EnsureIndexSubscriptions()
    {
        lock (_gate)
        {
            if (!_subscribedToNetwork)
            {
                // Network drives, WSL distros and folder indexes all publish through here when their
                // index takes an update (scan, checkpoint, watcher-driven incremental publish).
                UserNetworkDriveSearch.StatusesChanged += OnNetworkStatuses;
                _subscribedToNetwork = true;
            }
            if (_localStatusCts == null)
            {
                _localStatusCts = new CancellationTokenSource();
                _ = Task.Run(() => WatchLocalIndexAsync(_localStatusCts.Token));
            }
        }
    }

    /// <summary>Stops listening once nothing is registered any more.</summary>
    public void StopIndexSubscriptions()
    {
        lock (_gate)
        {
            if (_subscribedToNetwork)
            {
                UserNetworkDriveSearch.StatusesChanged -= OnNetworkStatuses;
                _subscribedToNetwork = false;
            }
            _localStatusCts?.Cancel();
            _localStatusCts?.Dispose();
            _localStatusCts = null;
            _lastLocalRevisions.Clear();
        }
    }

    private void OnNetworkStatuses(IReadOnlyList<NetworkIndexStatus> statuses)
    {
        foreach (var status in statuses)
            ReportSource(status.Drive);
    }

    // The elevated service publishes a status update after every applied change batch (see
    // UsnIndexerExtensions.ApplyUsnRecords/ApplyFolderChange), which is the only signal this process
    // gets that a local drive's index moved. Which drive moved is read from its revision, bumped by
    // exactly those two -- the status carries no change list, and asking for one would mean a second,
    // far chattier subscription for something no caller here needs.
    private async Task WatchLocalIndexAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await SearchStatusStream.SubscribeAsync(OnLocalStatus, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Logger.Log($"[IndexManager] Index change subscription dropped, retrying: {ex.Message}", LogLevel.Debug);
            }

            // The service being down/restarting is routine (upgrade, manual restart); the per-directory
            // watchers keep working meanwhile, so this only has to come back eventually.
            try
            {
                await Task.Delay(5000, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void OnLocalStatus(Indexer.Usn.UsnIndexer.IndexerStatus status)
    {
        if (status.Drives == null)
            return;

        foreach (var drive in status.Drives)
        {
            if (string.IsNullOrEmpty(drive.Drive))
                continue;
            lock (_gate)
            {
                // First sight of a drive is not a change: it is this subscription starting up, and
                // re-listing every plugin's directories for that would be a refresh nobody asked for.
                var known = _lastLocalRevisions.TryGetValue(drive.Drive, out var previous);
                _lastLocalRevisions[drive.Drive] = drive.Revision;
                if (!known || previous == drive.Revision)
                    continue;
            }
            ReportSource(drive.Drive);
        }
    }

    private void ReportSource(string sourceKey)
    {
        foreach (var pluginId in PluginsUnderSource(sourceKey, _registrations()))
            Report(pluginId);
    }

    /// <summary>
    /// The plugins whose registered directories are affected by a change in the source keyed
    /// <paramref name="sourceKey"/> -- a drive letter, a WSL UNC root or a folder-index path.
    /// </summary>
    internal static IEnumerable<string> PluginsUnderSource(string sourceKey, IReadOnlyList<(string PluginId, string Path)> registrations)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (pluginId, path) in registrations)
        {
            if (SourceTouchesPath(sourceKey, path) && seen.Add(pluginId))
                yield return pluginId;
        }
    }

    // Either nesting direction counts: the registered directory sits inside the source that changed, or
    // it contains that source (a plugin watching D:\ is affected by a folder index under D:\Projects).
    // Compared with a trailing separator on both sides, so "D:\Foo" never matches a sibling "D:\FooBar".
    internal static bool SourceTouchesPath(string sourceKey, string path)
    {
        if (string.IsNullOrEmpty(sourceKey) || string.IsNullOrEmpty(path))
            return false;

        var root = WithSeparator(sourceKey.Length == 1 ? sourceKey + Path.VolumeSeparatorChar : sourceKey);
        var directory = WithSeparator(path);
        return directory.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            || root.StartsWith(directory, StringComparison.OrdinalIgnoreCase);
    }

    private static string WithSeparator(string value)
        => value.EndsWith(Path.DirectorySeparatorChar) ? value : value + Path.DirectorySeparatorChar;

    public void Dispose()
    {
        StopIndexSubscriptions();
        _debouncer.Dispose();
    }
}
