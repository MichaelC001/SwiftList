namespace SwiftList.PluginSdk.Services;

/// <summary>
/// A decoupled service allowing plugins to register custom directories for global indexing and real-time monitoring.
/// </summary>
public static class DirectoryIndexerService
{
    /// <summary>
    /// Delegate set by the host application to handle directory registration.
    /// Parameters: (pluginId, directoryPath, recursive, filterPattern)
    /// </summary>
    public static Action<string, string, bool, string>? RegisterDirectoryAction { get; set; }

    /// <summary>
    /// Delegate set by the host application to clear directory registrations for a plugin.
    /// Parameters: (pluginId)
    /// </summary>
    public static Action<string>? UnregisterDirectoriesAction { get; set; }

    /// <summary>
    /// Delegate set by the host application to perform target directory search.
    /// Parameters: (pluginId, query, token)
    /// </summary>
    public static Func<string, string, CancellationToken, Task<List<Abstractions.ISearchResult>>>? SearchPluginDirectoriesFunc { get; set; }

    /// <summary>
    /// Delegate set by the host application to list a directory out of the file index.
    /// Parameters: (directoryPath, recursive, filterPattern, limit, token)
    /// </summary>
    public static Func<string, bool, string, int, CancellationToken, IAsyncEnumerable<Abstractions.ISearchResult>>? EnumerateDirectoryFunc { get; set; }

    /// <summary>
    /// Event fired when a monitored directory's content changes.
    /// Parameters: (pluginId)
    /// </summary>
    public static event Action<string>? DirectoryChanged;

    /// <summary>
    /// Invokes the DirectoryChanged event. Should only be called by the host application.
    /// </summary>
    public static void NotifyDirectoryChanged(string pluginId) => DirectoryChanged?.Invoke(pluginId);

    /// <summary>
    /// Registers a directory to be indexed and monitored by the host system (service or app manager).
    /// </summary>
    public static void RegisterDirectory(string pluginId, string directoryPath, bool recursive = true, string filterPattern = "*") => RegisterDirectoryAction?.Invoke(pluginId, directoryPath, recursive, filterPattern);

    /// <summary>
    /// Unregisters all directories registered by the specified plugin.
    /// </summary>
    public static void UnregisterDirectories(string pluginId) => UnregisterDirectoriesAction?.Invoke(pluginId);

    /// <summary>
    /// Searches within all directories registered by this plugin, honouring the <c>recursive</c> and
    /// <c>filterPattern</c> each was registered with. Answered from the host's file index wherever one
    /// covers the directory (see <see cref="EnumerateDirectoryAsync"/> for that routing and for what
    /// the pattern means); matching is the host's own fuzzy, alias-aware matching, and an empty query
    /// keeps everything. Directories are returned alongside files -- drop them with
    /// <see cref="Abstractions.ISearchResult.IsDir"/> if unwanted.
    /// </summary>
    public static async Task<List<Abstractions.ISearchResult>> SearchDirectoriesAsync(string pluginId, string query, CancellationToken token = default)
    {
        if (SearchPluginDirectoriesFunc == null) return new List<Abstractions.ISearchResult>();
        return await SearchPluginDirectoriesFunc(pluginId, query, token);
    }

    /// <summary>
    /// Lists a directory's contents from the host's file index instead of the filesystem: for a local
    /// drive the host indexes (the usual case) this costs no disk I/O at all, which is the whole point
    /// of preferring it over <c>Directory.EnumerateFileSystemEntries</c>. A directory no index covers
    /// (an unconfigured network share, a drive indexing is disabled for) is walked live instead, so a
    /// caller never has to decide which of the two applies.
    /// <para>
    /// Results stream as they are produced. <paramref name="filterPattern"/> is a FILE pattern -- one
    /// or more Win32 wildcards separated by ';' or ',' (e.g. <c>"*.exe;*.lnk"</c>, default <c>"*"</c>)
    /// -- so directories are always returned whatever it says (drop them with
    /// <see cref="Abstractions.ISearchResult.IsDir"/> if unwanted), and
    /// <paramref name="recursive"/> descends regardless of what it selects. Hidden and system
    /// entries are never returned (the same always-on filter the host applies to every search result),
    /// though a hidden directory is still descended into -- only the entry's own attributes count. The
    /// user's exclusion settings are not applied, since the caller named one exact directory to look at.
    /// </para>
    /// <para>
    /// <paramref name="limit"/> caps how many entries are returned (0 = no cap). Worth setting for a
    /// recursive listing of a large tree: <c>EnumerateDirectoryAsync(@"C:\", recursive: true)</c> is a
    /// legitimate request for every single entry on the volume, and it will deliver exactly that.
    /// </para>
    /// </summary>
    public static IAsyncEnumerable<Abstractions.ISearchResult> EnumerateDirectoryAsync(string directoryPath, bool recursive = false, string filterPattern = "*", int limit = 0, CancellationToken token = default)
        => EnumerateDirectoryFunc?.Invoke(directoryPath, recursive, filterPattern, limit, token) ?? EmptyResults();

    private static async IAsyncEnumerable<Abstractions.ISearchResult> EmptyResults()
    {
        await Task.CompletedTask;
        yield break;
    }
}
