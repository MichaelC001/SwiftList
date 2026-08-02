using SwiftList.Core.Indexer.NetworkDrive;

namespace SwiftList.Core.Indexer.NetworkDrive;

// Telling subscribers where a network, WSL or folder index just changed. The mirror of
// UsnIndexer.RaiseDirectoriesChanged for the indexes this process holds itself -- no pipe in between,
// so the event goes straight out. Split from NetworkIndexerPublisher.cs to keep that file under the
// project's line limit.
internal sealed partial class NetworkIndexerPublisher
{
    /// <summary>
    /// Says this drive's index took content in, and where. <paramref name="changedDirectories"/> null
    /// means a whole tree was replaced and anything under it may have moved.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT raised from SetStatus or anything else that only reports progress, a state
    /// transition or an error: a subscriber acting on those would re-list its directories once per
    /// progress tick of a scan.
    /// </remarks>
    private void RaiseDirectoriesChanged(string drive, IReadOnlyCollection<string>? changedDirectories)
    {
        try
        {
            _raiseDirectoriesChanged(drive, changedDirectories);
        }
        catch (Exception ex)
        {
            Logger.Log($"[NetworkIndexer] A directory-change subscriber threw: {ex.Message}", LogLevel.Error);
        }
    }
}
