using SwiftList.Core.Services.Search;

namespace SwiftList.Core.Services.QuickPanel;

/// <summary>
/// Turns one configured quick panel source into the entries it should show. Every kind is answered
/// from the index rather than by walking the disk -- recent files through the service's own recency
/// query, the rest through <see cref="IndexedDirectoryEnumerator"/>, which falls back to a real walk
/// only where no index covers the folder.
/// </summary>
public static class QuickPanelSourceLoader
{
    public static async Task<List<SearchResult>> LoadAsync(QuickPanelFolderSource source, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(source.Path))
            return new List<SearchResult>();

        if (source.Kind == QuickPanelSourceKind.RecentFiles)
        {
            // The recency query is the index's own: it already returns newest-first across the whole
            // subtree, so recursion and ordering are not this method's to apply.
            var recent = await new SearchService()
                .GetRecentFilesAsync(new[] { source.Path }, source.MaxItems, EffectiveMaxAge(source), token)
                .ConfigureAwait(false);
            return recent;
        }

        var results = new List<SearchResult>();
        await IndexedDirectoryEnumerator.EnumerateAsync(source.Path, source.Recursive, source.FilterPattern,
            result => results.Add(result), limit: 0, token).ConfigureAwait(false);
        return Order(results, source.Kind, source.MaxItems);
    }

    /// <summary>
    /// The order a kind implies, and its cap. Recent-files sources never reach here: their order comes
    /// from the index query itself.
    /// </summary>
    internal static List<SearchResult> Order(List<SearchResult> results, QuickPanelSourceKind kind, int maxItems)
    {
        IEnumerable<SearchResult> ordered = kind == QuickPanelSourceKind.AllByModified
            ? results.OrderByDescending(r => r.Metadata.Modified)
            : results.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase);
        return (maxItems > 0 ? ordered.Take(maxItems) : ordered).ToList();
    }

    // 0 means "no age limit" in the settings, but the recency query reads 0 as "nothing qualifies", so
    // it has to be spelled as a ceiling instead. 30 days is the same bound the Startup Panel's own
    // field allows.
    private static int EffectiveMaxAge(QuickPanelFolderSource source)
        => source.MaxAgeMinutes > 0 ? source.MaxAgeMinutes : 43200;
}
