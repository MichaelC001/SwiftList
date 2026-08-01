using SwiftList.Core.Services.Plugin.DirectoryIndex;

using SwiftList.Core.Wire;
namespace SwiftList.Core.Services.Search;

/// <summary>
/// Client half of "list this directory without touching the disk": streams a directory's entries out of
/// the service's index, and walks the real filesystem only for a directory no index covers. Routing is
/// the same rule the rest of search uses (<see cref="SearchServiceHelper.CheckNeedsLiveSearch"/>) --
/// a local drive enabled for indexing is indexed in full, so its listing always comes from the index.
/// <para>
/// The user's exclusion settings deliberately play no part here: the caller named one exact directory,
/// which is the same "show me what is actually in this place" intent that already bypasses them for a
/// path-mode query. Hidden and system entries ARE dropped though, exactly as they are for every other
/// search result -- that filter is a separate, always-on one, not part of those settings. It applies to
/// an entry's own attributes only: a hidden directory is still walked through, just never returned.
/// </para>
/// <para>
/// Every call re-decides on its own, and nothing about a fallback is remembered: a directory answered
/// by a live walk today (index still building, service restarting) is answered from the index the
/// moment the index can answer it, with no cache to invalidate and no state to reset.
/// </para>
/// </summary>
public static class IndexedDirectoryEnumerator
{
    // limit <= 0 means "everything". Note it bounds RESULTS, not work: an index-side walk still visits
    // the subtree until that many entries have passed the filters.
    public static async Task EnumerateAsync(string directoryPath, bool recursive, string filterPattern,
        Action<SearchResult> onResult, int limit = 0, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            return;
        var path = Path.GetFullPath(directoryPath);

        if (SearchServiceHelper.CheckNeedsLiveSearch(path, ExclusionRuleSet.From(UserSettings.Load())))
        {
            await Task.Run(() => ScanLive(path, recursive, filterPattern, onResult, limit, token), token).ConfigureAwait(false);
            return;
        }

        var notIndexed = false;
        try
        {
            await SearchPipeClient.SendSearchPipeCommandAsync(new SearchRequestMessage
            {
                Id = SearchRequestId.EnumerateDir,
                DirectoryFilter = path,
                Query = filterPattern,
                Recursive = recursive,
                Limit = limit
            }, onResult, token, onNotIndexed: () => notIndexed = true).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The service being down/mid-restart is the one case where the index genuinely has nothing
            // to say about a drive it does cover, so this walks the disk rather than reporting an empty
            // directory. Callers that were doing their own Directory.EnumerateFiles before this existed
            // are no worse off than they were.
            Logger.Log($"[IndexedDirectoryEnumerator] Index enumeration of '{path}' failed, falling back to a live walk: {ex.Message}", LogLevel.Warn);
            notIndexed = true;
        }

        // The service answered, and answered "no loaded index holds this path" -- an index still being
        // built, or the brief swap window of a rebuild. It emits nothing in that case, so falling back
        // here cannot duplicate anything already delivered.
        if (notIndexed)
            await Task.Run(() => ScanLive(path, recursive, filterPattern, onResult, limit, token), token).ConfigureAwait(false);
    }

    // Matches the index-side walk's semantics on purpose (DirectoryEnumerator): directories are listed
    // alongside files and filtered by name like files are, and recursion is never gated by the filter.
    private static void ScanLive(string path, bool recursive, string filterPattern, Action<SearchResult> onResult, int limit, CancellationToken token)
    {
        if (!Directory.Exists(path))
            return;
        var patterns = FilterPatternHelper.SplitOrNullIfMatchAll(filterPattern);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            AttributesToSkip = 0
        };

        var emitted = 0;
        foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos("*", options))
        {
            token.ThrowIfCancellationRequested();
            // AttributesToSkip stays 0 on purpose: the walk must still go THROUGH hidden directories
            // (AppData is one), it just must not return them or any other hidden/system entry -- same
            // entry-level-only rule the index-side walk applies.
            if (FileSystemItemFilter.IsHiddenOrSystem(info.Attributes))
                continue;
            if (patterns != null && !FilterPatternHelper.Matches(info.Name, patterns))
                continue;
            onResult(ToResult(info));
            if (limit > 0 && ++emitted >= limit)
                return;
        }
    }

    private static SearchResult ToResult(FileSystemInfo info)
    {
        var isDir = (info.Attributes & FileAttributes.Directory) != 0;
        var root = Path.GetPathRoot(info.FullName) ?? string.Empty;
        return new SearchResult
        {
            Name = info.Name,
            Path = info.FullName,
            IsDir = isDir,
            Drive = root.Length >= 2 && root[1] == Path.VolumeSeparatorChar ? root.Substring(0, 1) : string.Empty,
            Attributes = info.Attributes,
            Metadata = new PluginSdk.Abstractions.FileMetadata(
                info is FileInfo file ? file.Length : 0,
                info.CreationTime,
                info.LastWriteTime,
                info.LastAccessTime)
        };
    }
}
