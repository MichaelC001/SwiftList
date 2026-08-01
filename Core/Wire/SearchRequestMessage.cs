namespace SwiftList.Core.Wire;

public enum SearchRequestId : byte
{
    Ping = 0,
    Status = 1,
    SubscribeStatus = 9,
    Rebuild = 2,
    GetMachineSettings = 3,
    SetMachineSettings = 4,
    Search = 5,
    SearchDir = 6,
    RebuildDrive = 7,
    DeleteDriveIndex = 8,
    Initialize = 10,
    GetFileMetadata = 11,
    ClearServiceLog = 12,
    GetRecentFiles = 13,
    ClearPathCaches = 14,
    LaunchHook = 15,
    CancelDriveIndex = 16,
    // Streams a directory's entries out of the index (DirectoryFilter = the directory, Query = the
    // filename filter, Recursive = descend). Streaming, like Search/SearchDir -- not a Process() case.
    EnumerateDir = 17
}

public struct SearchRequestMessage
{
    public SearchRequestId Id { get; set; }
    public int Limit { get; set; }
    public int AppLimit { get; set; }
    public string? Query { get; set; }
    public string? DirectoryFilter { get; set; }
    public string? Drive { get; set; }
    public MachineSettings? MachineSettings { get; set; }
    public List<string>? DisabledAliasComponents { get; set; }
    public List<string>? FilePaths { get; set; }
    // Target directories for GetRecentFiles -- distinct from FilePaths above (individual file paths
    // for GetFileMetadata) since the two requests take different kinds of list.
    public List<string>? Directories { get; set; }
    // GetRecentFiles' max-age cutoff, in minutes.
    public int MaxAgeMinutes { get; set; }
    // LaunchHook: whether the caller wants the hook elevated (only honored if that session's user is
    // genuinely an administrator -- see HookProcessBroker).
    public bool RequestElevation { get; set; }

    // Search/SearchDir: the user's fuzzy-matching preference, carried per request because the service
    // runs as a different (elevated) identity and cannot read this user's settings file. Deliberately
    // phrased as the negative ("exact") rather than "fuzzy": this is a struct, so it cannot carry a
    // field initializer, and a caller that forgets to set it must fall back to the historical fuzzy
    // behavior -- which only the negative phrasing gives, since default(bool) is false.
    public bool ExactMatch { get; set; }

    // EnumerateDir: whether to descend into subdirectories. Same struct-default reasoning as
    // ExactMatch above -- a caller that forgets it gets the cheap single-level listing, not a
    // full subtree walk.
    public bool Recursive { get; set; }
}
