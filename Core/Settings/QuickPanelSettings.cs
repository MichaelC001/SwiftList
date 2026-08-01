namespace SwiftList.Core;

/// <summary>What a quick panel group orders its items by.</summary>
public enum QuickPanelSortMode
{
    ModifiedDescending,
    NameAscending,
}

/// <summary>Where a folder source's items come from, and in what order.</summary>
public enum QuickPanelSourceKind
{
    /// <summary>Only what changed recently, newest first -- answered from the index, not by walking.</summary>
    RecentFiles,
    /// <summary>Everything in the folder, by name: a folder used as a shortcut bar.</summary>
    Launcher,
    /// <summary>
    /// Everything in the folder, newest first. Distinct from RecentFiles, which drops anything older
    /// than its age limit: this one never hides a file, it only decides what to show first.
    /// </summary>
    AllByModified,
}

/// <summary>
/// Backs the quick panel: the floating panel docked over whatever window is in front. Each tab is a
/// workspace -- its own sources, its own order, its own display preferences -- and the panel shows one
/// tab at a time.
/// </summary>
public class QuickPanelSettings
{
    public bool Enabled { get; set; } = true;

    public List<QuickPanelTab> Tabs { get; set; } = new() { QuickPanelTab.CreateDefault() };

    /// <summary>The tab the panel reopens on. Falls back to the first tab when it no longer exists.</summary>
    public string ActiveTabId { get; set; } = string.Empty;

    /// <summary>
    /// Further applications the panel refuses to open over, by process name ("chrome" or "chrome.exe",
    /// either way). Added ON TOP of <see cref="UserSettings.BlacklistedProcesses"/>, never instead of
    /// it: whatever is blocked globally is blocked here too. This list exists for the apps that only
    /// this panel has a reason to avoid -- it docks itself onto the window in front, so a full-screen
    /// player or a game is ruined by it without deserving a global block.
    /// </summary>
    public List<string> BlacklistedProcesses { get; set; } = new();
}

/// <summary>One workspace, shown as one tab in the panel's tab strip.</summary>
public class QuickPanelTab
{
    /// <summary>Stable identity, so renaming a tab keeps its group preferences.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Empty falls back to a translated default, so a never-renamed tab follows the UI language.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Off keeps the workspace configured but out of the way: no tab for it in the panel, and no
    /// process of its claims it. For the workspace you set up for a project you are not on this month,
    /// where deleting it means rebuilding the source list to get it back.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Applications this workspace belongs to, by process name. Summon the panel while one of them is
    /// in front and it opens on this tab instead of wherever it was left -- which is the whole point of
    /// having workspaces docked onto the window in front: the app you are in already says which set of
    /// folders you mean. Empty means the tab is only ever reached by hand.
    /// </summary>
    public List<string> Processes { get; set; } = new();

    public List<QuickPanelFolderSource> Folders { get; set; } = new();

    /// <summary>
    /// Display order, most-preferred first, over every kind of source at once. An id that isn't listed
    /// (a folder just added, a plugin source that appeared) keeps its discovery-order position after
    /// everything listed -- same rule as <see cref="StartupPanelSettings.TabOrder"/>. Plugin-provided
    /// sources will share this id space when they arrive; folder sources use their own Id today.
    /// </summary>
    public List<string> GroupOrder { get; set; } = new();

    /// <summary>
    /// Sources hidden from this tab. A single list rather than an Enabled flag per source: the built-in
    /// and (later) plugin sources have no settings object of their own to carry one, and splitting the
    /// two would mean asking two different questions to learn whether one group is shown.
    /// </summary>
    public List<string> DisabledGroupIds { get; set; } = new();

    public Dictionary<string, QuickPanelGroupPreference> GroupPreferences { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The tab a fresh install starts with: the three folders most people keep things in, as recent
    /// files. Neither built-in source is included -- both are one button away, and a panel that opens
    /// with someone's favorites and every document Windows has ever logged is guessing at what they
    /// wanted rather than showing what they asked for.
    /// </summary>
    public static QuickPanelTab CreateDefault() => new()
    {
        Id = NewId(),
        Folders =
        {
            QuickPanelFolderSource.For(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            // No Environment.SpecialFolder.Downloads (it predates that folder existing) -- UserProfile +
            // "Downloads" is the same fallback the rest of this codebase already uses.
            QuickPanelFolderSource.For(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")),
            QuickPanelFolderSource.For(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
        },
    };

    /// <summary>Short, stable, and never shown -- only ever compared.</summary>
    public static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 8);

    /// <summary>
    /// A copy that shares nothing mutable with this one, ids included. What the settings page edits, so
    /// that adding a folder or deleting a workspace is staged like every other setting rather than
    /// taking effect the moment it is typed -- these objects live inside the process-wide UserSettings,
    /// and mutating them in place is both instantly live and unaffected by Cancel.
    /// </summary>
    public QuickPanelTab Clone() => new()
    {
        Id = Id,
        Name = Name,
        Enabled = Enabled,
        Processes = new List<string>(Processes),
        Folders = Folders.Select(f => new QuickPanelFolderSource
        {
            Id = f.Id,
            Path = f.Path,
            Kind = f.Kind,
            Recursive = f.Recursive,
            FilterPattern = f.FilterPattern,
            MaxItems = f.MaxItems,
            MaxAgeMinutes = f.MaxAgeMinutes,
        }).ToList(),
        GroupOrder = new List<string>(GroupOrder),
        DisabledGroupIds = new List<string>(DisabledGroupIds),
        GroupPreferences = GroupPreferences.ToDictionary(
            pair => pair.Key,
            pair => new QuickPanelGroupPreference
            {
                DisplayName = pair.Value.DisplayName,
                Sort = pair.Value.Sort,
                ThumbnailView = pair.Value.ThumbnailView,
                Expanded = pair.Value.Expanded,
            },
            StringComparer.OrdinalIgnoreCase),
    };
}

/// <summary>A folder the user added as a source. One folder is one group.</summary>
public class QuickPanelFolderSource
{
    /// <summary>
    /// Its identity for order/visibility/preferences, generated once. Not the path: editing the path of
    /// a source you already renamed and reordered should keep it the same source.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public QuickPanelSourceKind Kind { get; set; } = QuickPanelSourceKind.RecentFiles;

    public bool Recursive { get; set; }

    /// <summary>One or more Win32 file patterns separated by ';' or ',' -- see FilterPatternHelper.</summary>
    public string FilterPattern { get; set; } = "*";

    /// <summary>How many entries this group shows at most. 0 means everything the source has.</summary>
    public int MaxItems { get; set; } = 20;

    /// <summary>Only entries changed within this many minutes qualify. 0 means no age limit at all.</summary>
    public int MaxAgeMinutes { get; set; }

    public static QuickPanelFolderSource For(string path, QuickPanelSourceKind kind = QuickPanelSourceKind.RecentFiles)
        => new() { Id = QuickPanelTab.NewId(), Path = path, Kind = kind };
}

/// <summary>
/// How one group is displayed, stored per source id so it applies to folder sources, the built-in ones
/// and (later) plugin sources alike. What the user does to a group in the panel itself -- reorder it,
/// rename it, collapse it, switch it to a list -- lands here and survives the panel closing.
/// </summary>
public class QuickPanelGroupPreference
{
    /// <summary>
    /// Overrides the group heading. Empty means the source's own default name, which for the built-in
    /// sources is translated and therefore follows the UI language -- a name typed here does not.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    public QuickPanelSortMode Sort { get; set; } = QuickPanelSortMode.ModifiedDescending;

    public bool ThumbnailView { get; set; } = true;

    public bool Expanded { get; set; } = true;
}
