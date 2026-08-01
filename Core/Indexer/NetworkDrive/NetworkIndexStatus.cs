using SwiftList.Core.Indexer.Usn;

namespace SwiftList.Core.Indexer.NetworkDrive;

public sealed class NetworkIndexStatus
{
    public string Drive { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int Items { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public int EnumerateErrors { get; set; }
    public int AttributeErrors { get; set; }
    public int ReparseSkipped { get; set; }
    public int SlowDirectories { get; set; }
    public string CachePath { get; set; } = string.Empty;
    public DateTime? LastUpdated { get; set; }
    public string Error { get; set; } = string.Empty;

    // Same pair, same meaning, same rules as a local drive's -- see UsnIndexer.DriveIndexStatus. A
    // status arrives here for a great many reasons (progress ticks, state changes, error reports) and
    // only some of them are the index actually taking content in; the revision is what tells those
    // apart, and ChangedDirectories is what says where.
    public long Revision { get; set; }

    public DriveChangedDirectories ChangedDirectories { get; set; } = new();

    public NetworkIndexStatus Clone() => new()
    {
        Revision = Revision,
        // Copied, not shared: the live one keeps being appended to while a subscriber reads this.
        ChangedDirectories = ChangedDirectories.Clone(),
        Drive = Drive,
        State = State,
        Items = Items,
        Skipped = Skipped,
        Errors = Errors,
        EnumerateErrors = EnumerateErrors,
        AttributeErrors = AttributeErrors,
        ReparseSkipped = ReparseSkipped,
        SlowDirectories = SlowDirectories,
        CachePath = CachePath,
        LastUpdated = LastUpdated,
        Error = Error
    };
}
