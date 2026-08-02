using SwiftList.Core.Indexer.NetworkDrive.Walk;
namespace SwiftList.Core.Indexer.NetworkDrive;

// Status/index publishing for NetworkIndexer -- extracted into its own class (composition, not a
// partial class) to keep NetworkIndexer.cs under the project's line limit. Shares NetworkIndexer's own
// _gate/_statuses/_indexes dictionaries by reference rather than owning copies, since both types need
// to observe the same live state.
// Partial: the change-tracking half (StoreStatus/RecordChange) lives in
// NetworkIndexerPublisherChangeTracking.cs, to keep this file under the project's line limit.
internal sealed partial class NetworkIndexerPublisher
{
    private readonly object _gate;
    private readonly Dictionary<string, NetworkIndexStatus> _statuses;
    private readonly Dictionary<string, NetworkIndex> _indexes;
    private readonly Action<string> _ensureWatcher;
    private readonly Func<IReadOnlyList<NetworkIndexStatus>> _getStatuses;
    private readonly Action<IReadOnlyList<NetworkIndexStatus>> _raiseStatusesChanged;
    private readonly Action<string, string> _queueRefresh;
    private readonly Action<string, IReadOnlyCollection<string>?> _raiseDirectoriesChanged;
    // Drives where PublishIncrementalUpdate skipped a real, watcher-detected filesystem change because a
    // rescan was in progress -- see PublishIncrementalUpdate's own comment on why skipping THAT change is
    // safe (the rescan's own walk normally re-observes it independently), which is true except for a
    // change landing in a directory the walk already finished visiting (or diff-reused, skipping re-
    // listing it) before the change happened. OnRefreshFinished consults this once the rescan that was
    // running actually completes, and if set, queues one more lightweight follow-up refresh -- cheap
    // (TreeDiffBaseline reuses everything that didn't actually change) and guaranteed to observe whatever
    // the skipped watcher event was about, since it runs against this drive's live current state.
    private readonly HashSet<string> _missedDuringRescan = new(StringComparer.OrdinalIgnoreCase);

    public NetworkIndexerPublisher(
        object gate,
        Dictionary<string, NetworkIndexStatus> statuses,
        Dictionary<string, NetworkIndex> indexes,
        Action<string> ensureWatcher,
        Func<IReadOnlyList<NetworkIndexStatus>> getStatuses,
        Action<IReadOnlyList<NetworkIndexStatus>> raiseStatusesChanged,
        Action<string, string> queueRefresh,
        Action<string, IReadOnlyCollection<string>?> raiseDirectoriesChanged)
    {
        _gate = gate;
        _statuses = statuses;
        _indexes = indexes;
        _ensureWatcher = ensureWatcher;
        _getStatuses = getStatuses;
        _raiseStatusesChanged = raiseStatusesChanged;
        _queueRefresh = queueRefresh;
        _raiseDirectoriesChanged = raiseDirectoriesChanged;
    }

    public void SetStatus(string drive, string state, int? items, string? error)
    {
        lock (_gate)
        {
            // A scan already in flight when its drive got removed from config (Configure() deletes the
            // entry synchronously) keeps running cooperatively for a bit until its token check trips --
            // any status/progress callback it fires in that window must not resurrect an entry for a
            // drive the user just disabled.
            if (!_statuses.TryGetValue(drive, out var current))
                return;
            // No RecordChange: this reports progress, a state transition or an error, none of which is
            // the index taking content in. Bumping the revision here is what used to make every scan
            // look like a thousand separate changes to anything watching a directory on this drive.
            _statuses[drive] = (NetworkIndexerHelper.CreateStatus(
                drive, state, items ?? current.Items, null, current, error ?? string.Empty));
        }
        PublishStatusesChanged();
    }

    // Whatever's currently loaded for this drive (a completed index, or an interrupted checkpoint) becomes
    // TreeBuilder's diff baseline for the refresh about to run -- see TreeDiffBaseline.
    public FileRecordStore? GetPreviousStore(string drive)
    {
        lock (_gate)
            return _indexes.TryGetValue(drive, out var index) ? index.ToStore() : null;
    }

    public void OnRefreshFinished(string drive, NetworkIndex index)
    {
        NetworkIndex? old;
        bool stillTracked;
        bool missedDuringThisRescan;
        lock (_gate)
        {
            // Mirrors SetStatus's guard: a scan already in flight when its drive got removed from config
            // (Configure() deletes _statuses[drive] synchronously) must not resurrect it here, and must not
            // re-attach a watcher below -- that watcher is what used to keep a disabled drive refreshing
            // itself forever via file-system-change events, long after Configure() tore everything else down.
            stillTracked = _statuses.ContainsKey(drive);
            missedDuringThisRescan = _missedDuringRescan.Remove(drive);
            if (stillTracked)
            {
                _indexes.TryGetValue(drive, out old);
                _indexes[drive] = index;
                _statuses[drive] = (NetworkIndexerHelper.CreateStatus(drive, "ready", index.Count, index, null));
                // A whole tree replaced at once: what moved inside it is not knowable from here, so
                // this says so rather than guessing, and every subscriber re-lists.
                RaiseDirectoriesChanged(drive, null);
            }
            else
            {
                old = null;
            }
        }
        if (!stillTracked)
        {
            index.Dispose();
            return;
        }
        // Dispose OUTSIDE the lock: LiveIndex.Dispose() takes its own write lock and can briefly block
        // on an in-flight search holding its read lock -- doing that while holding _gate would stall
        // every other drive's status/index access for no reason.
        if (old != null && !ReferenceEquals(old, index))
            old.Dispose();
        _ensureWatcher(drive);
        PublishStatusesChanged();

        // A watcher-detected change was skipped (not lost -- the in-memory delta it applied lived on the
        // OLD index just disposed above) while this rescan was running. The walk that just finished
        // normally re-observes the same change on its own, EXCEPT when it landed in a directory the walk
        // had already finished visiting, or one TreeDiffBaseline reused instead of re-listing -- queuing
        // one more (typically cheap, diff-reuse-dominated) refresh is the simplest way to guarantee that
        // gap gets closed instead of silently sitting stale until something else happens to touch it again.
        if (missedDuringThisRescan)
            _queueRefresh(drive, "watcher change during rescan");
    }

    // Mirrors UsnIndexerExtensions.MarkMissedIfRebuilding for local drives: decides -- synchronously, at
    // the moment a watcher-detected change is actually applied to the in-memory index, not later when
    // PublishIncrementalUpdate's own debounced timer happens to fire -- whether this drive is currently
    // being rescanned, and flags the change as missed if so. Deciding this late (as PublishIncrementalUpdate
    // used to do entirely on its own) left a gap: if the in-flight rescan finished before the debounce
    // timer fired, OnRefreshFinished had already flipped this drive's status to "ready" and consumed
    // (found empty) _missedDuringRescan by the time the stale check finally ran, so the change was never
    // flagged -- and, since the state no longer read "indexing" either, PublishIncrementalUpdate's own
    // guard below didn't skip it either, letting it persist a save built from the by-then-disposed old
    // index (Count 0) and regress the freshly-finished drive's status back down. WatcherManager calls this
    // before scheduling a debounced publish at all, so a change caught mid-rescan is flagged immediately
    // and its publish is skipped from the start, closing that window regardless of how the debounce timer
    // and the rescan's own completion happen to interleave.
    public bool MarkMissedIfRescanning(string drive)
    {
        lock (_gate)
        {
            var isTracked = _statuses.TryGetValue(drive, out var status);
            var isRescanning = isTracked && status!.State == "indexing";
            if (isRescanning)
                _missedDuringRescan.Add(drive);
            return isRescanning;
        }
    }

    public void PublishIncrementalUpdate(string drive, NetworkIndex index, IReadOnlyCollection<string>? changedDirectories = null)
    {
        // Same fix, same reason as UsnIndexer.UpdateDriveCounts's markReady guard: the watcher for this
        // drive is already live from the moment it's configured (Scheduler.StartRefresh attaches it
        // before that drive's own initial refresh is even queued), and PublishCheckpoint's own "cached
        // index already complete, don't touch it yet" branch leaves _indexes[drive] -- and so `index`
        // here, since WatcherManager mutates that same cached instance in place -- pointing at the OLD,
        // full-size index for a re-scan's ENTIRE duration. Persisting and publishing this watcher-detected
        // change against that stale base would overwrite the in-progress scan's own Items/State with the
        // old total and force the row back to "ready" mid-scan (the up/down flicker this guard exists to
        // prevent), AND could regress the on-disk cache back to older data if this save lands after a
        // fresher checkpoint. This is now mostly a safety net for a narrower timing: MarkMissedIfRescanning
        // above already flags+skips a change detected WHILE a rescan is running; this still catches a
        // change scheduled just BEFORE a rescan started, whose debounced publish then fires DURING it.
        bool skip;
        lock (_gate)
        {
            var isTracked = _statuses.TryGetValue(drive, out var stateCheck);
            skip = !isTracked || stateCheck!.State == "indexing";
            if (skip && isTracked)
                _missedDuringRescan.Add(drive);
        }
        if (skip)
            return;

        IndexerHelper.Save(index);
        lock (_gate)
        {
            if (!_statuses.TryGetValue(drive, out var current) || current.State == "indexing")
                return;
            _statuses[drive] = (NetworkIndexerHelper.CreateStatus(drive, "ready", index.Count, index, current));
            // The one path here that knows where: these come from the watcher events this publish is
            // the debounced tail of, so a plugin watching one folder on a share is woken by changes in
            // that folder and by nothing else on the share.
            RaiseDirectoriesChanged(drive, changedDirectories);
        }
        PublishStatusesChanged();
    }

    public void PublishCheckpoint(string drive, FileRecordStore store, NetworkDriveWalkStats stats, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        // A checkpoint is always a partial, in-progress snapshot (IsComplete is never true here). If
        // what's currently cached for this drive is a fully complete, trusted index, a checkpoint from a
        // resume/re-validation pass that later gets interrupted must not regress it back to a smaller,
        // partial view -- skip persisting this one (to memory and disk) entirely, so the last known-good
        // complete index keeps serving searches until a full pass actually finishes and can genuinely
        // replace it. Checked BEFORE building anything below: NetworkIndex.FromStore always writes
        // straight to this drive's cache path (unconditionally, as part of just constructing it), which
        // the currently-cached complete index still has memory-mapped -- building it regardless and only
        // discarding the in-memory result afterward still clobbered that good on-disk cache with this
        // partial one, contradicting the "skip persisting to disk" this comment already promised.
        NetworkIndex? currentBeforeSave;
        lock (_gate)
            _indexes.TryGetValue(drive, out currentBeforeSave);

        if (currentBeforeSave != null && currentBeforeSave.IsComplete)
        {
            lock (_gate)
            {
                // Re-checked here, inside the same lock the Stop button's own status revert uses --
                // Cancel() is synchronous and its visibility to IsCancellationRequested is immediate, so
                // if CancelDrive's revert has already run by the time this write would happen, this is
                // guaranteed to observe it and back off instead of clobbering "cached" back to
                // "indexing" a moment after the user stopped it. Also backs off if the drive was removed
                // from config entirely (mirrors OnRefreshFinished's guard) -- Configure() deletes
                // _statuses[drive] synchronously, and this checkpoint's own cancellation token may not
                // have tripped yet.
                if (token.IsCancellationRequested || !_statuses.ContainsKey(drive))
                    return;
                _statuses[drive] = (NetworkIndexerHelper.CreateStatus(drive, "indexing", CountLiveRecords(store), currentBeforeSave, null));
            }
            PublishStatusesChanged();
            return;
        }

        // `index` owns a mmap-backed LiveIndex now (unlike the old engine's plain in-memory checkpoint) --
        // every path below that doesn't end up storing it into _indexes must still Dispose it.
        NetworkIndex? index = null;
        var stored = false;
        try
        {
            // Release the currently-cached index's memory mapping BEFORE NetworkIndex.FromStore below
            // writes a fresh file over this exact cache path -- a still-open memory mapping on the
            // destination (even one opened with FileShare.Delete, which only guarantees the RENAME half
            // of the swap succeeds) can make the swap's own backup-file cleanup, or the rename itself,
            // fail outright with "the file to be replaced is in use" on some Windows/filesystem
            // combinations (confirmed reliably on a network share from a Windows 10 VM; not reproducible
            // on Windows 11). currentBeforeSave is known non-complete here (the alreadyComplete case
            // already returned above), so there's nothing worth keeping alive in memory past this point
            // regardless of what happens next. Goes through ReleaseCachedIndex (not a direct Dispose())
            // so a concurrent GetPreviousStore/search sees "nothing cached" rather than a disposed
            // instance still sitting in _indexes for the remainder of this write.
            var released = ReleaseCachedIndex(drive);

            index = NetworkIndex.FromStore(store, stats);
            IndexerHelper.Save(index);

            NetworkIndex? old = null;
            lock (_gate)
            {
                if (token.IsCancellationRequested || !_statuses.ContainsKey(drive))
                    return;
                _indexes.TryGetValue(drive, out old);
                _indexes[drive] = index;
                stored = true;
                _statuses[drive] = (NetworkIndexerHelper.CreateStatus(drive, "indexing", index.Count, index, null));
                // A checkpoint swaps in a whole partial tree, so where it moved is no more knowable
                // than for a finished rescan.
                RaiseDirectoriesChanged(drive, null);
            }
            // old is normally null (already released above); only genuinely non-null (and needing its own
            // dispose) if PublishIncrementalUpdate/OnRefreshFinished raced in and stored something new into
            // _indexes[drive] in between.
            if (old != null && !ReferenceEquals(old, index) && !ReferenceEquals(old, released))
                old.Dispose();
            PublishStatusesChanged();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Log($"[NetworkIndexer] Failed to publish checkpoint for {drive}: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            if (!stored)
                index?.Dispose();
        }
    }

    // A cheap count straight from the scan's own in-memory store (no LiveIndex/mmap needed) -- matches
    // NetworkIndex.Count's own "-1 for the root row" convention, since this feeds the exact same status
    // display that would otherwise read index.Count.
    private static int CountLiveRecords(FileRecordStore store)
    {
        var count = 0;
        foreach (var record in store.Records)
            if (!record.IsDeleted)
                count++;
        return Math.Max(0, count - 1);
    }

    // Removes and disposes whatever's currently cached for this drive, if anything -- shared by
    // PublishCheckpoint's own periodic writes and DriveRefreshRunner's final write at the end of a full
    // refresh (wired in as NetworkIndex.Build's beforeFinalWrite callback), both of which need this
    // drive's cache path free of any memory mapping right before they write a fresh file over it.
    internal NetworkIndex? ReleaseCachedIndex(string drive)
    {
        NetworkIndex? existing;
        lock (_gate)
            _indexes.Remove(drive, out existing);
        existing?.Dispose();
        return existing;
    }

    public void PublishStatusesChanged()
    {
        try
        {
            _raiseStatusesChanged(_getStatuses());
        }
        catch
        {
        }
    }
}
