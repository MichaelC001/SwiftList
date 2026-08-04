namespace SwiftList.Core.SearchIndex.Query;

// Splits an optional trailing "<query> :a,b,c" suffix off a raw search query into raw tokens --
// deliberately dumb: it has no idea what a token means (that's up to whichever IQueryTokenProvider
// plugin claims it). The suffix must be the query's last whitespace-separated token so it never
// gets misread out of the middle of an otherwise-unrelated search term.
public static class SearchQuerySortParser
{
    public static string Strip(string query, out IReadOnlyList<string> tokens, char prefixChar = ':')
    {
        tokens = Array.Empty<string>();

        var trimmed = query.TrimEnd();
        var lastSpaceIndex = trimmed.LastIndexOf(' ');
        var lastToken = lastSpaceIndex >= 0 ? trimmed[(lastSpaceIndex + 1)..] : trimmed;

        if (lastToken.Length < 2 || lastToken[0] != prefixChar)
            return query;

        var parts = lastToken[1..].Split(',');
        if (parts.Any(p => p.Length == 0))
            return query;

        tokens = parts;
        return lastSpaceIndex >= 0 ? trimmed[..lastSpaceIndex] : string.Empty;
    }

    // Strips a leading "*" -- the marker that opts one search out of the user's own exclusion rules
    // (see SearchService.SearchStreamingAsync's bypassExclusions parameter). Callers must run this
    // BEFORE the query is used for anything else (the actual search call, AND whatever gets stored as
    // an AppSearchResult's SearchQuery for highlighting) -- the character itself is never part of the
    // match/highlight text, only a query-string-level signal.
    public static string StripExclusionBypass(string query, out bool bypassExclusions)
    {
        bypassExclusions = query.Length > 0 && query[0] == '*';
        return bypassExclusions ? query[1..] : query;
    }
}
