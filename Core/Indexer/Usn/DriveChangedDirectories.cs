namespace SwiftList.Core.Indexer.Usn;

/// <summary>
/// Where a drive's index has actually changed, newest last, so a subscriber can tell whether a
/// revision bump has anything to do with the directories it cares about.
/// </summary>
/// <remarks>
/// A drive revision on its own only says "this drive moved", and every subscriber matching that
/// against its own paths had to assume the worst: on a working C: drive, a temp file or a log line is
/// indistinguishable from someone installing an application. This carries the directories with the
/// revision that changed them, so the question can be answered instead of guessed.
///
/// Directories, not files, and distinct per batch: a bulk copy of a thousand files into one folder is
/// one entry. That is what keeps a bound this small from being hit in practice, which matters because
/// the whole list rides on every status message.
///
/// Not thread-safe on its own: every mutation and every <see cref="Clone"/> happens under
/// UsnIndexer.LockObj, the same lock the revision it is keyed by is bumped under.
/// </remarks>
public sealed class DriveChangedDirectories
{
    /// <summary>How many directories are remembered before the oldest start falling off.</summary>
    public const int Capacity = 64;

    private readonly List<Entry> _entries = new();

    /// <summary>One directory, and the revision whose batch changed something inside it.</summary>
    public readonly record struct Entry(long Revision, string Directory);

    /// <summary>
    /// The oldest revision this list still describes in full. A reader whose last seen revision is
    /// older than this has a gap: something fell off the end, or a batch arrived that could not be
    /// resolved to directories at all, and it must assume it missed a change rather than trust what
    /// is left here.
    /// </summary>
    public long CoveredFromRevision { get; private set; }

    public IReadOnlyList<Entry> Entries => _entries;

    /// <summary>Records where <paramref name="revision"/>'s batch changed things.</summary>
    public void Record(long revision, IReadOnlyCollection<string> directories)
    {
        foreach (var directory in directories)
        {
            if (!string.IsNullOrEmpty(directory))
                _entries.Add(new Entry(revision, directory));
        }
        Trim();
    }

    /// <summary>
    /// Records that <paramref name="revision"/> changed something, somewhere unknown -- a batch too
    /// wide to enumerate, or one whose parents the index could not resolve to paths.
    /// </summary>
    /// <remarks>
    /// Everything older goes with it. Keeping those entries would be worse than keeping none: a reader
    /// spanning this revision would read the remaining list as the complete set of places that changed
    /// and skip the one it was watching, which is exactly the failure this type exists to prevent.
    /// </remarks>
    public void RecordUnknown(long revision)
    {
        _entries.Clear();
        CoveredFromRevision = revision + 1;
    }

    /// <summary>
    /// Whether this list describes everything that happened after <paramref name="lastSeenRevision"/>,
    /// i.e. whether a reader at that revision can trust it to say where the changes were.
    /// </summary>
    public bool Covers(long lastSeenRevision) => CoveredFromRevision <= lastSeenRevision + 1;

    /// <summary>The directories changed after <paramref name="lastSeenRevision"/>, newest last.</summary>
    /// <remarks>
    /// Filtered by revision rather than drained, because the same list is broadcast to every
    /// subscriber: one of them reading it must not take it away from the others, and each is at its
    /// own last-seen revision anyway.
    /// </remarks>
    public IEnumerable<string> DirectoriesAfter(long lastSeenRevision)
    {
        foreach (var entry in _entries)
        {
            if (entry.Revision > lastSeenRevision)
                yield return entry.Directory;
        }
    }

    /// <summary>Rebuilds one that came off the wire, exactly as it was sent.</summary>
    /// <remarks>
    /// Verbatim rather than replayed through <see cref="Record"/>: the sender already trimmed, and
    /// re-trimming a list that arrived at capacity would drop its oldest revision a second time and
    /// silently narrow what the receiver believes is covered.
    /// </remarks>
    internal static DriveChangedDirectories Restore(long coveredFromRevision, IEnumerable<Entry> entries)
    {
        var restored = new DriveChangedDirectories { CoveredFromRevision = coveredFromRevision };
        restored._entries.AddRange(entries);
        return restored;
    }

    public DriveChangedDirectories Clone()
    {
        var clone = new DriveChangedDirectories { CoveredFromRevision = CoveredFromRevision };
        clone._entries.AddRange(_entries);
        return clone;
    }

    // Whole revisions only, never half of one: a revision with some of its directories dropped would
    // read as "these are the only places it touched" and hide the rest, so the partial one is dropped
    // outright and CoveredFromRevision moves past it.
    private void Trim()
    {
        if (_entries.Count <= Capacity)
            return;

        // The last entry that has to go to get back to Capacity -- one before the first kept, not the
        // first kept itself, or the revision straddling the boundary would be dropped whole when only
        // its tail needed to survive.
        var lastDropped = _entries[_entries.Count - Capacity - 1].Revision;
        var keepFrom = 0;
        while (keepFrom < _entries.Count && _entries[keepFrom].Revision <= lastDropped)
            keepFrom++;

        _entries.RemoveRange(0, keepFrom);
        CoveredFromRevision = _entries.Count > 0 ? _entries[0].Revision : lastDropped + 1;
    }
}
