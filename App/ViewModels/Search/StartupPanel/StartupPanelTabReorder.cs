namespace SwiftList.App.ViewModels.Search.StartupPanel;

// What dragging a tab in the strip does to StartupPanel.TabOrder. A pure function of "what the strip
// holds now" and "what was stored before", separate from StartupPanelController both to keep that file
// under the repo's per-file line limit and because this is the one part of a drag worth testing without
// a strip to drag.
internal static class StartupPanelTabReorder
{
    /// <summary>
    /// The stored order after the strip has been rearranged: what is on screen, in the order it is now
    /// in, followed by every id that was stored but has no tab this time round.
    /// </summary>
    /// <remarks>
    /// A source that yielded nothing produces no tab at all (see StartupPanelController), so an id can
    /// be stored and absent. It cannot have been positioned by a drag that could not reach it, and it
    /// keeps its place relative to the other absent ones -- but it does end up after everything just
    /// arranged, because "most-preferred first" is a statement about the tabs the user could see.
    /// </remarks>
    public static List<string> Apply(IEnumerable<string> stripIds, IEnumerable<string>? storedOrder)
    {
        var arranged = stripIds
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var seen = new HashSet<string>(arranged, StringComparer.OrdinalIgnoreCase);
        var kept = (storedOrder ?? Enumerable.Empty<string>())
            .Where(id => !string.IsNullOrEmpty(id) && !seen.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return arranged.Concat(kept).ToList();
    }
}
