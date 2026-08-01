namespace SwiftList.Core.Indexer.NetworkDrive;

// The half of NetworkIndexerPublisher that answers "did this index take content in, and where" for
// subscribers -- see NetworkIndexStatus.Revision/ChangedDirectories, and UsnIndexer.RecordDriveChange
// for the local-drive shape this mirrors. Split out of NetworkIndexerPublisher.cs to keep that file
// under the project's line limit. Every method here runs with _gate already held.
internal sealed partial class NetworkIndexerPublisher
{
    /// <summary>Replaces a drive's status, carrying its change tracking onto the new object.</summary>
    /// <remarks>
    /// NetworkIndexerHelper.CreateStatus builds a fresh status every time, so without this the revision
    /// would reset on every state change. It must only ever go up: a subscriber compares it to the last
    /// one it saw, and one that went backwards would make a freshly rebuilt index look like a revision
    /// it had already handled -- and its directory list look like it covered a span it knows nothing
    /// about, which reads as "nothing you care about changed".
    /// </remarks>
    private void StoreStatus(string drive, NetworkIndexStatus fresh)
    {
        if (_statuses.TryGetValue(drive, out var previous))
        {
            fresh.Revision = previous.Revision;
            fresh.ChangedDirectories = previous.ChangedDirectories;
        }
        _statuses[drive] = fresh;
    }

    /// <summary>
    /// Marks this drive's index as having taken content in, and says where.
    /// <paramref name="changedDirectories"/> null means "somewhere, unknown" -- a full rescan or a
    /// checkpoint, where the whole tree is in play.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT called from <see cref="SetStatus"/>, or from anything else that only reports
    /// progress, a state transition or an error: a subscriber diffs the revision to tell "this index
    /// moved" from "a status arrived", and bumping it for a progress tick would make one scan look
    /// like a thousand separate changes to everything watching a directory on this drive.
    /// </remarks>
    private void RecordChange(string drive, IReadOnlyCollection<string>? changedDirectories)
    {
        if (!_statuses.TryGetValue(drive, out var status))
            return;

        status.Revision++;
        if (changedDirectories == null)
            status.ChangedDirectories.RecordUnknown(status.Revision);
        else
            status.ChangedDirectories.Record(status.Revision, changedDirectories);
    }
}
