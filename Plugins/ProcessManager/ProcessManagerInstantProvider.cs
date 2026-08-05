using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using SwiftList.PluginSdk.Abstractions.Plugins;
using SwiftList.PluginSdk.Services;

namespace SwiftList.Plugins.ProcessManager;

public class ProcessManagerInstantProvider : IInstantResultProvider
{
    public string Name => TranslationService.Get("ProcessManager_Name");

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    private static string GetSafeMainWindowTitle(Process proc)
    {
        IntPtr hWnd;
        try
        {
            hWnd = proc.MainWindowHandle;
        }
        catch
        {
            return string.Empty;
        }

        if (hWnd == IntPtr.Zero)
            return string.Empty;

        var titleLength = GetWindowTextLength(hWnd);
        if (titleLength == 0)
            return string.Empty;

        var capacity = titleLength + 1;
        var text = new StringBuilder(capacity);
        return GetWindowText(hWnd, text, capacity) == 0 ? string.Empty : text.ToString();
    }

    // Falls back to the default even if an empty string was already persisted before RequireNonEmpty
    // started enforcing this at save time -- an empty keyword should never silently make this
    // unreachable.
    private static string GetTriggerKeyword()
    {
        var value = PluginSettingsService.GetSetting("SwiftList.Plugins.ProcessManager", "TriggerKeyword", "ps");
        return string.IsNullOrWhiteSpace(value) ? "ps" : value;
    }

    private static string GetProcessPath(Process proc)
    {
        try
        {
            return proc.MainModule?.FileName ?? string.Empty;
        }
        catch
        {
            return TranslationService.Get("ProcessManager_AccessDenied");
        }
    }

    // Lower tier ranks first: 0 = literal process-name match, 1 = literal PID match, 2 = literal
    // window-title match, 3 = fuzzy/alias fallback on name or title -- the same literal/fuzzy/alias
    // tiering BrowserDataInstantProvider uses, so e.g. a window titled in Chinese is still reachable by
    // typing its pinyin, just ranked behind anything that matched literally instead of competing with
    // it on plain alphabetical order. Returns null when nothing matches at any tier.
    // windowTitle is often empty (background/non-windowed processes); Contains("", ...) would trivially
    // match everything, and FuzzyMatchService.IsMatch isn't designed for an empty pattern/text either,
    // so it's only checked when non-empty.
    internal static int? GetMatchTier(string processName, string pid, string windowTitle, string searchTerm)
    {
        if (processName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            return 0;
        if (pid.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            return 1;
        if (!string.IsNullOrEmpty(windowTitle) && windowTitle.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            return 2;
        if (FuzzyMatchService.IsMatch(searchTerm, processName))
            return 3;
        if (!string.IsNullOrEmpty(windowTitle) && FuzzyMatchService.IsMatch(searchTerm, windowTitle))
            return 3;

        return null;
    }

    public IEnumerable<InstantResultItem> GetInstantResults(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            yield break;

        var keyword = GetTriggerKeyword();
        if (string.IsNullOrWhiteSpace(keyword))
            yield break;

        var trimmed = query.Trim();
        var isPsQuery = string.Equals(trimmed, keyword, StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith(keyword + " ", StringComparison.OrdinalIgnoreCase);

        if (!isPsQuery)
            yield break;

        var searchTerm = "";
        if (trimmed.StartsWith(keyword + " ", StringComparison.OrdinalIgnoreCase))
        {
            searchTerm = trimmed.Substring(keyword.Length + 1).Trim();
        }

        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch
        {
            yield break;
        }

        var matches = new List<(Process Process, int Tier)>();

        foreach (var proc in processes)
        {
            try
            {
                if (string.IsNullOrEmpty(searchTerm))
                {
                    matches.Add((proc, 0));
                    continue;
                }

                var tier = GetMatchTier(proc.ProcessName, proc.Id.ToString(), GetSafeMainWindowTitle(proc), searchTerm);
                if (tier.HasValue)
                    matches.Add((proc, tier.Value));
            }
            catch
            {
                // Process might have already exited
            }
        }

        // Lower tier first (stronger match), then alphabetically by process name within the same tier.
        matches.Sort((a, b) =>
        {
            var tierCompare = a.Tier.CompareTo(b.Tier);
            return tierCompare != 0 ? tierCompare : string.Compare(a.Process.ProcessName, b.Process.ProcessName, StringComparison.OrdinalIgnoreCase);
        });

        // Limit results to 100 items to keep search extremely snappy
        var results = matches.Select(m => m.Process).Take(100);

        var pathKey = TranslationService.Get("ProcessManager_Path");
        var windowKey = TranslationService.Get("ProcessManager_Window");

        foreach (var proc in results)
        {
            var pid = 0;
            var processName = "Unknown";
            var windowTitle = "";

            try
            {
                pid = proc.Id;
                processName = proc.ProcessName;
                windowTitle = GetSafeMainWindowTitle(proc);
            }
            catch
            {
                continue;
            }

            var path = GetProcessPath(proc);
            var title = $"{processName}.exe (PID: {pid})";
            var desc = string.IsNullOrWhiteSpace(windowTitle)
                ? $"{pathKey}: {path}"
                : $"{windowKey}: {windowTitle} | {pathKey}: {path}";

            var hasRealIcon = !string.IsNullOrEmpty(path) && !path.StartsWith("[");

            yield return new InstantResultItem
            {
                Title = title,
                Description = desc,
                IconData = hasRealIcon ? $"path:{path}" : "M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.61l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z",
                IconColor = hasRealIcon ? null : "AccentRed",
                ActionType = "Execute",
                ActionArgument = $"kill:{pid}",
                TabCompletion = $"{keyword} {processName}"
            };
        }
    }

    public bool[]? GetHighlightMask(string text, string query)
    {
        if (string.IsNullOrEmpty(query)) return null;
        var keyword = GetTriggerKeyword();
        if (string.IsNullOrWhiteSpace(keyword)) return null;
        var trimmed = query.Trim();
        var mask = new bool[text.Length];
        if (!trimmed.StartsWith(keyword + " ", StringComparison.OrdinalIgnoreCase)) return mask;

        var searchTerm = trimmed.Substring(keyword.Length + 1).Trim();
        if (string.IsNullOrEmpty(searchTerm)) return mask;

        return FuzzyMatchService.GetHighlightMask(text, searchTerm) ?? mask;
    }
}
