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
    // exactly those two; WHERE it moved comes from the directories carried alongside it.
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

    // The revision half of the status only says the volume changed, and matching that against a
    // registered directory can only ask "does this directory sit on that drive" -- true of everything
    // on C:, so a temp file or a log line used to wake every plugin watching anything there, and each
    // paid a full re-listing for it. The directories carried with the revision are what turn that back
    // into a question with an answer.
    private void OnLocalStatus(Indexer.Usn.UsnIndexer.IndexerStatus status)
    {
        if (status.Drives == null)
            return;

        foreach (var drive in status.Drives)
        {
            if (string.IsNullOrEmpty(drive.Drive))
                continue;

            long previousRevision;
            lock (_gate)
            {
                // First sight of a drive is not a change: it is this subscription starting up, and
                // re-listing every plugin's directories for that would be a refresh nobody asked for.
                var known = _lastLocalRevisions.TryGetValue(drive.Drive, out previousRevision);
                _lastLocalRevisions[drive.Drive] = drive.Revision;
                if (!known || previousRevision == drive.Revision)
                    continue;
            }

            ReportLocalChange(drive, previousRevision);
        }
    }

    private void ReportLocalChange(Indexer.Usn.UsnIndexer.DriveIndexStatus drive, long previousRevision)
    {
        foreach (var pluginId in PluginsForLocalChange(drive, previousRevision, _registrations()))
            Report(pluginId);
    }

    /// <summary>
    /// The plugins a local drive's revision move actually concerns, from the directories it moved in.
    /// </summary>
    /// <remarks>
    /// Falls back to the whole drive when the change list cannot account for everything since
    /// <paramref name="previousRevision"/> -- a batch too wide to enumerate, or one whose entries have
    /// since fallen off the end. Losing precision there costs a refresh nobody needed; assuming
    /// nothing happened would cost a plugin the change it was watching for, so the imprecise answer is
    /// the safe one.
    /// </remarks>
    internal static IEnumerable<string> PluginsForLocalChange(
        Indexer.Usn.UsnIndexer.DriveIndexStatus drive,
        long previousRevision,
        IReadOnlyList<(string PluginId, string Path)> registrations)
    {
        if (!drive.ChangedDirectories.Covers(previousRevision))
        {
            foreach (var pluginId in PluginsUnderSource(drive.Drive, registrations))
                yield return pluginId;
            yield break;
        }

        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in drive.ChangedDirectories.DirectoriesAfter(previousRevision))
        {
            foreach (var (pluginId, path) in registrations)
            {
                if (SourceTouchesPath(directory, path) && reported.Add(pluginId))
                    yield return pluginId;
            }
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
