using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.App.ViewModels.Settings;
using SwiftList.App.ViewModels.Settings.Plugins;
using SwiftList.App.ViewModels.Settings.QuickPanel;
using SwiftList.Core;

using SwiftList.Core.SearchIndex;
namespace SwiftList.App;

// Title-bar search box + results popup for SettingsWindow, as extension methods (matching RuntimeIndex's
// BucketExtensions/QueryExtensions split) instead of an extra partial-class file, to stay under the
// file-length convention. SettingsWindow.xaml.cs itself must stay partial (the WPF/XAML tooling
// requires it), but this second file is not that generated half -- it's purely this session's own
// split, so it follows the same composition/extension-method pattern as everywhere else in the project.
// The three XAML-wired event handlers (TextChanged/PreviewKeyDown/MouseUp) stay as thin instance methods
// on SettingsWindow itself, since XAML event wiring resolves by reflection and can't target an
// extension method; everything they call into lives here.
internal static class SettingsWindowSearchExtensions
{
    public static void CloseSearchPopup(this SettingsWindow window) => window.SearchResultsPopup.IsOpen = false;

    // "Section", "Section > Tab", or "Section > Tab > SubTab" -- entries whose own LabelKey names a
    // tab/sub-tab leave TabLabelKey/SubTabLabelKey null (see SettingsSearchEntry), so their breadcrumb
    // stops at the parent instead of repeating the result's own label back at the user.
    // Internal, not private: also used by App.xaml.cs to build the entry list exposed to plugins via
    // PluginSdk.Services.SettingsSearchService.
    internal static string BuildBreadcrumb(SettingsSearchEntry entry)
    {
        var parts = new List<string> { TranslationManager.Instance[$"Settings_{entry.Section}"] };
        if (entry.TabLabelKey != null)
            parts.Add(TranslationManager.Instance[entry.TabLabelKey]);
        if (entry.SubTabLabelKey != null)
            parts.Add(TranslationManager.Instance[entry.SubTabLabelKey]);
        return string.Join(" › ", parts);
    }

    // Every currently-reachable settings entry, static + dynamic, in one fixed order: this is the single
    // source of truth for the in-app search box, JumpToEntry's index resolution, and the SDK-facing
    // SettingsSearchService feed (see App.xaml.cs), so all three agree on what "entry N" means and none
    // of them silently omits the plugin or hotkey-action entries the other two include.
    // vm is null for the SDK feed (no live SettingsWindow may exist yet) -- the three dynamic sections
    // then fall back to building their own data straight from PluginManager.Instance/UserSettings
    // instead of reading the live window's already-built collections, since Activate/Reveal only matter
    // once a real window exists to apply them to anyway (see ActivateSearchResult).
    //
    // evaluateConditionalVisibility governs ONLY the static IsVisible-gated entries (WSL tab, manual-theme
    // rows, ...), independently of vm's own null-ness: the SDK feed always builds with vm: null, which
    // unconditionally excludes these entries (no live window to evaluate their predicate against) --
    // JumpToEntry must reproduce that exact same exclusion to keep its indices aligned with the SDK's,
    // even though it DOES have a real, live vm available (needed for the Plugins/Hotkeys
    // dynamic sections below, whose Reveal step looks up a container by reference-equality against the
    // live window's own bound collection -- rebuilding fresh throwaway objects there, as vm: null would,
    // makes ContainerFromItem find nothing and silently skips the highlight). Defaults to true (evaluate
    // for real) for the in-app search box's own live, no-index-round-trip usage.
    internal static List<SettingsSearchResultItem> BuildAllEntries(SettingsViewModel? vm, bool evaluateConditionalVisibility = true)
    {
        var results = new List<SettingsSearchResultItem>();
        foreach (var entry in SettingsSearchIndex.Entries)
        {
            // Entries like the WSL tab are only reachable while their own section is actually shown
            // (IsVisible null for the overwhelming majority of entries, which are always reachable).
            // Without vm there's no way to evaluate the predicate, so such an entry is conservatively
            // excluded rather than shown as a dead link.
            if (entry.IsVisible != null && (!evaluateConditionalVisibility || vm == null || !entry.IsVisible(vm)))
                continue;

            results.Add(new SettingsSearchResultItem(TranslationManager.Instance[entry.LabelKey], BuildBreadcrumb(entry), entry.Section, entry.Activate, entry.TargetElementName));
        }

        // These three sources have no static Entries above -- their labels only exist at runtime
        // (whatever plugins happen to be loaded) -- so build from the same live models each page
        // renders from when one is available, else from PluginManager.Instance/UserSettings directly.
        var pluginsSectionLabel = TranslationManager.Instance["Settings_Plugins"];
        var plugins = vm?.Plugins.Plugins ?? (IEnumerable<PluginInfoViewModel>)PluginLoaderHelper.BuildPluginList(UserSettings.Load());
        foreach (var plugin in plugins)
        {
            var capturedPlugin = plugin;
            // Selecting is what showing a plugin means now that the page is a list beside a detail pane;
            // it used to expand that plugin's card in a column of all of them.
            void RevealPlugin(SettingsViewModel settings)
            {
                settings.Plugins.SelectedPlugin = capturedPlugin;
                capturedPlugin.IsConfigTab = false;
            }

            results.Add(new SettingsSearchResultItem(plugin.Name, pluginsSectionLabel, "Plugins", RevealPlugin,
                Reveal: new SettingsSearchDynamicReveal("PluginsList", capturedPlugin)));

            foreach (var component in plugin.RawComponents)
            {
                // Searched for across the whole page (empty list name), not inside the plugin's own row:
                // RevealPlugin above has already selected the plugin, so its components are rendered in
                // the detail pane by the time this runs -- but that pane is a sibling of the list, not
                // something reachable from the row's container.
                results.Add(new SettingsSearchResultItem(component.DisplayName, $"{pluginsSectionLabel} › {plugin.Name}", "Plugins", RevealPlugin,
                    Reveal: new SettingsSearchDynamicReveal(string.Empty, component)));
            }

            var configTabLabel = TranslationManager.Instance["Common_Configure"];

            void AddConfigFields(IEnumerable<PluginConfigFieldViewModel> fields, string parentBreadcrumb, PluginConfigFieldViewModel? ownerGroup = null)
            {
                foreach (var field in fields)
                {
                    var currentGroup = field.IsGroup ? field : ownerGroup;
                    var label = field.Label;
                    if (!string.IsNullOrWhiteSpace(label))
                    {
                        var breadcrumb = string.IsNullOrWhiteSpace(field.GroupName) || field.IsGroup
                            ? parentBreadcrumb
                            : $"{parentBreadcrumb} › {field.GroupName}";

                        var capturedGroup = currentGroup;
                        void RevealPluginConfig(SettingsViewModel settings)
                        {
                            settings.Plugins.SelectedPlugin = capturedPlugin;
                            capturedPlugin.IsConfigTab = true;
                            if (capturedGroup != null && capturedPlugin.ConfigGroups.Contains(capturedGroup))
                            {
                                capturedPlugin.SelectedConfigGroup = capturedGroup;
                            }
                        }

                        results.Add(new SettingsSearchResultItem(label, breadcrumb, "Plugins", RevealPluginConfig,
                            Reveal: new SettingsSearchDynamicReveal(string.Empty, field)));
                    }

                    if (field.Children is { Count: > 0 })
                    {
                        AddConfigFields(field.Children, parentBreadcrumb, currentGroup);
                    }
                }
            }

            if (plugin.ConfigFields is { Count: > 0 })
            {
                AddConfigFields(plugin.ConfigFields, $"{pluginsSectionLabel} › {plugin.Name} › {configTabLabel}");
            }
        }

        var hotkeysSectionLabel = TranslationManager.Instance["Settings_Hotkeys"];
        var pluginActionsTabLabel = TranslationManager.Instance["Hotkeys_Tab_PluginActions"];
        var pluginActionGroups = vm?.Hotkeys.PluginActionGroups ?? (IEnumerable<PluginActionGroupViewModel>)HotkeySettingsViewModel.BuildPluginActionGroups(UserSettings.Load().Hotkeys.PluginActionHotkeys);
        foreach (var group in pluginActionGroups)
        {
            var capturedGroup = group;
            void SelectPluginActionsTab(SettingsViewModel v) => v.Hotkeys.SelectedTab = "PluginActions";

            results.Add(new SettingsSearchResultItem(group.PluginName, $"{hotkeysSectionLabel} › {pluginActionsTabLabel}", "Hotkeys", SelectPluginActionsTab,
                Reveal: new SettingsSearchDynamicReveal("PluginActionGroupsList", capturedGroup)));

            foreach (var action in group.Items)
            {
                results.Add(new SettingsSearchResultItem(action.DisplayName, $"{hotkeysSectionLabel} › {pluginActionsTabLabel} › {group.PluginName}", "Hotkeys", SelectPluginActionsTab,
                    Reveal: new SettingsSearchDynamicReveal("PluginActionGroupsList", capturedGroup, action)));
            }
        }

        var quickPanelSectionLabel = TranslationManager.Instance["Settings_QuickPanel"];
        var quickPanelPluginTabsLabel = TranslationManager.Instance["QuickPanel_PluginTabs"];
        var quickPanelPluginTabs = vm?.QuickPanel.PluginTabs ?? QuickPanelPluginTabCatalog.Available(UserSettings.Load().QuickPanel);
        foreach (var pluginTab in quickPanelPluginTabs)
        {
            var capturedTab = pluginTab;
            void SelectPluginTabSection(SettingsViewModel v) => v.QuickPanel.SelectedSection = "PluginTabs";

            results.Add(new SettingsSearchResultItem(capturedTab.Name, $"{quickPanelSectionLabel} › {quickPanelPluginTabsLabel}", "QuickPanel", SelectPluginTabSection,
                Reveal: new SettingsSearchDynamicReveal("QuickPanelPluginTabsList", capturedTab)));
        }

        return results;
    }

    public static void OnSettingsSearchTextChanged(this SettingsWindow window)
    {
        var query = window.TxtSettingsSearch.Text?.Trim() ?? string.Empty;
        if (query.Length == 0)
        {
            window.CloseSearchPopup();
            return;
        }

        var vm = window.DataContext as SettingsViewModel;
        var results = BuildAllEntries(vm)
            .Where(r => FuzzyMatcher.IsMatch(query, r.Label))
            .ToList();

        window.LstSearchResults.ItemsSource = results;
        window.LstSearchResults.Visibility = results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        window.TxtSearchNoResults.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        window.SearchResultsPopup.IsOpen = true;
        // Highlights the top result so Enter picks it immediately, matching Windows 11 Settings search.
        // Doesn't navigate by itself -- only Enter/click (see ActivateSearchResult) commits a result.
        window.LstSearchResults.SelectedIndex = results.Count > 0 ? 0 : -1;
    }

    // Wired to PreviewKeyDown, not KeyDown: the TextBox's default template hosts its text in a
    // ScrollViewer (PART_ContentHost), whose own class handler consumes Up/Down/PageUp/PageDown for
    // scrolling and marks them Handled before a bubbling KeyDown on the TextBox itself would ever see
    // them. Tunneling PreviewKeyDown fires first, top-down, so we get first refusal on those keys.
    public static void OnSettingsSearchKeyDown(this SettingsWindow window, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (window.LstSearchResults.SelectedItem is SettingsSearchResultItem item)
                window.ActivateSearchResult(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Down && window.LstSearchResults.Items.Count > 0)
        {
            // Wraps: Down past the last result loops back to the first.
            window.LstSearchResults.SelectedIndex = (window.LstSearchResults.SelectedIndex + 1) % window.LstSearchResults.Items.Count;
            window.ScrollSelectedResultIntoView();
            e.Handled = true;
        }
        else if (e.Key == Key.Up && window.LstSearchResults.Items.Count > 0)
        {
            // Wraps: Up past the first result loops back to the last.
            var count = window.LstSearchResults.Items.Count;
            window.LstSearchResults.SelectedIndex = (window.LstSearchResults.SelectedIndex - 1 + count) % count;
            window.ScrollSelectedResultIntoView();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // Clearing the text fires TxtSettingsSearch_TextChanged, which closes the popup.
            window.TxtSettingsSearch.Text = string.Empty;
            e.Handled = true;
        }
    }

    // Setting SelectedIndex from code doesn't scroll -- that only happens as a side effect of the
    // ListBox's own internal keyboard handling, which we bypass entirely (see OnSettingsSearchKeyDown
    // above; the ListBox itself never has focus).
    private static void ScrollSelectedResultIntoView(this SettingsWindow window)
    {
        if (window.LstSearchResults.SelectedItem != null)
            window.LstSearchResults.ScrollIntoView(window.LstSearchResults.SelectedItem);
    }

    // Deliberately not driven by SelectionChanged: SelectedIndex also changes for the "highlight the
    // top result" default and for Up/Down navigation, neither of which should navigate away. Mouse
    // clicks and Enter both funnel through here instead, only on an explicit commit.
    public static void OnSettingsSearchResultsMouseUp(this SettingsWindow window, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.OriginalSource is FrameworkElement { DataContext: SettingsSearchResultItem item })
            window.ActivateSearchResult(item);
    }

    // Internal, not private: also called directly by SettingsWindow.JumpToEntry (swiftlist://settings/
    // entry/<index>), which builds a SettingsSearchResultItem from a SettingsSearchIndex entry rather
    // than from a live text search.
    internal static void ActivateSearchResult(this SettingsWindow window, SettingsSearchResultItem item)
    {
        if (window.DataContext is SettingsViewModel vm)
            item.Activate?.Invoke(vm);

        window.SelectSection(item.Section);
        // Clearing the text fires TxtSettingsSearch_TextChanged, which closes the popup.
        window.TxtSettingsSearch.Text = string.Empty;

        // Switching section/tab alone doesn't reset scroll position -- a page's ScrollViewer just
        // clamps whatever offset it already had to the newly-visible content's (possibly shorter)
        // bounds, which can land anywhere rather than on the matched setting. Defer to ContextIdle so
        // the tab-switch layout pass (triggered by the DataTrigger bindings above) has already run;
        // BringIntoView walks up to whichever ancestor ScrollViewer actually owns the scrolling. The
        // highlight flash (see SettingsSearchHighlight) mirrors Windows 11 Settings' search behavior.
        if (item.TargetElementName != null)
        {
            var targetName = item.TargetElementName;
            var section = item.Section;
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ResolveNamedElement(window.GetSectionPage(section), targetName) is FrameworkElement target)
                {
                    target.BringIntoView();
                    SettingsSearchHighlight.Show(target);
                }
            }), DispatcherPriority.ContextIdle);
        }
        else if (item.Reveal != null)
        {
            var reveal = item.Reveal;
            var section = item.Section;
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                var page = window.GetSectionPage(section);
                if (page == null) return;

                // An empty list name means "search the whole page for this item" rather than "find its
                // row inside that named list". The plugins page needs it: its list on the left holds one
                // row per plugin, but a component lives in the detail pane beside it, which is a
                // different element entirely -- so there is no single ItemsControl containing both, and
                // the row container would never have the component under it to find.
                if (reveal.ListElementName.Length == 0)
                {
                    if (FindDescendantByDataContext(page, reveal.GroupItem) is not { } found) return;
                    found.BringIntoView();
                    SettingsSearchHighlight.Show(found);
                    return;
                }

                if (page.FindName(reveal.ListElementName) is not ItemsControl list
                    || list.ItemContainerGenerator.ContainerFromItem(reveal.GroupItem) is not FrameworkElement groupContainer)
                    return;

                // The child row (e.g. a plugin component under its card, or a hotkey action under its
                // group) only exists once any Activate-triggered expansion (e.g. Plugins.IsExpanded) has
                // been measured/arranged -- that flip already happened synchronously above, but its
                // visual tree needs this same deferred pass to actually materialize.
                var target = reveal.ChildItem != null ? FindDescendantByDataContext(groupContainer, reveal.ChildItem) : null;
                target ??= groupContainer;
                target.BringIntoView();
                SettingsSearchHighlight.Show(target);
            }), DispatcherPriority.ContextIdle);
        }
    }

    // "TabSearchHistory/ChkEnable" resolves one FindName hop at a time -- the second segment names an
    // element declared inside HistoryListControl's own XAML, a separate NameScope from the settings
    // page hosting it, so the page's FindName can't reach it directly.
    private static FrameworkElement? ResolveNamedElement(FrameworkElement? root, string path)
    {
        var current = root;
        foreach (var segment in path.Split('/'))
        {
            if (current?.FindName(segment) is not FrameworkElement next)
                return null;
            current = next;
        }
        return current;
    }

    private static FrameworkElement? FindDescendantByDataContext(DependencyObject root, object dataContext)
    {
        var childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { } fe && ReferenceEquals(fe.DataContext, dataContext))
                return fe;

            if (FindDescendantByDataContext(child, dataContext) is FrameworkElement found)
                return found;
        }
        return null;
    }

    // Internal, not private: also called by SettingsWindow.ApplySelectedSection, whose lazy PageXxx
    // properties this switch resolves through -- that's the actual point where an untouched tab's page
    // gets constructed for the first time (see SettingsWindow.xaml.cs's PageXxx properties).
    internal static FrameworkElement? GetSectionPage(this SettingsWindow window, string section) => section switch
    {
        "Service" => window.PageService,
        "Index" => window.PageIndex,
        "General" => window.PageGeneral,
        "Appearance" => window.PageAppearance,
        "Hotkeys" => window.PageHotkeys,
        "Plugins" => window.PagePlugins,
        "History" => window.PageHistory,
        "Favorites" => window.PageFavorites,
        "QuickPanel" => window.PageQuickPanel,
        "LocalSend" => window.PageLocalSend,
        "About" => window.PageAbout,
        _ => null,
    };
}
