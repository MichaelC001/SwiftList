namespace SwiftList.Core.Indexer.Usn;

// The status UsnIndexer publishes, and the snapshot it publishes it as. Split out of UsnIndexer.cs
// purely to keep that file under the repo's line limit; these stay nested types of UsnIndexer so
// every caller keeps saying UsnIndexer.IndexerStatus.
public partial class UsnIndexer
{
    public class IndexerStatus
    {
        public string State { get; set; } = "idle";
        public int Progress { get; set; } = 0;
        public int TotalFiles { get; set; } = 0;
        public int TotalDirs { get; set; } = 0;
        public double ElapsedTime { get; set; } = 0.0;
        public bool IsMaintenanceBusy { get; set; }
        public List<string> ActiveDrives { get; set; } = new();
        public List<DriveIndexStatus> Drives { get; set; } = new();
    }

    public class DriveIndexStatus
    {
        public string Drive { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public string Kind { get; set; } = "LocalNtfs";
        public string State { get; set; } = "unknown";
        public int Files { get; set; }
        public int Dirs { get; set; }
        public string CachePath { get; set; } = string.Empty;
        // Bumped once per applied change batch on this drive, so a subscriber can tell "this drive's
        // index just moved" from "this status arrived for some other reason". Files/Dirs cannot answer
        // that: editing a file in place changes its size and timestamps in the index without changing
        // either count. Monotonic per process, meaningless across restarts -- only differences matter.
        public long Revision { get; set; }

        /// <summary>Where the revisions above changed things -- see <see cref="DriveChangedDirectories"/>.</summary>
        /// <remarks>
        /// A revision says the drive moved; this says where, so a subscriber watching one directory is
        /// not woken by every unrelated write on the volume it happens to sit on.
        /// </remarks>
        public DriveChangedDirectories ChangedDirectories { get; set; } = new();
    }

    public IndexerStatus SnapshotStatus()
    {
        lock (LockObj)
        {
            return new IndexerStatus
            {
                State = Status.State,
                Progress = Status.Progress,
                TotalFiles = Status.TotalFiles,
                TotalDirs = Status.TotalDirs,
                ElapsedTime = Status.ElapsedTime,
                IsMaintenanceBusy = Status.IsMaintenanceBusy,
                ActiveDrives = Status.ActiveDrives.ToList(),
                Drives = Status.Drives.Select(d => new DriveIndexStatus
                {
                    Drive = d.Drive,
                    Enabled = d.Enabled,
                    Kind = d.Kind,
                    State = d.State,
                    Files = d.Files,
                    Dirs = d.Dirs,
                    CachePath = d.CachePath,
                    Revision = d.Revision,
                    // Copied, not shared: the live one keeps being appended to under LockObj while a
                    // subscriber is still reading the status it was handed.
                    ChangedDirectories = d.ChangedDirectories.Clone()
                }).ToList()
            };
        }
    }

    /// <summary>
    /// Marks this drive's index as having moved, and says where. <paramref name="changedDirectories"/>
    /// null means the batch could not be pinned to directories -- see
    /// <see cref="DriveChangedDirectories.RecordUnknown"/>.
    /// </summary>
    /// <remarks>Caller holds <see cref="LockObj"/>, which is what keeps the two writes one step.</remarks>
    internal void RecordDriveChange(string drive, IReadOnlyCollection<string>? changedDirectories)
    {
        var item = Status.Drives.FirstOrDefault(d => d.Drive.Equals(drive, StringComparison.OrdinalIgnoreCase));
        if (item == null)
            return;

        item.Revision++;
        if (changedDirectories == null)
            item.ChangedDirectories.RecordUnknown(item.Revision);
        else
            item.ChangedDirectories.Record(item.Revision, changedDirectories);
    }
}
