# UI & Preview Extensions

## Result display

### `ISidebarFilterProvider`

Adds categorizing filter groups to the results sidebar (e.g. date-range or size buckets).

```csharp
interface ISidebarFilterProvider
{
    int SortOrder { get; } // default 100; lower renders first
    IEnumerable<SidebarFilterGroup> GetFilterGroups();
}
```

`SidebarFilterGroup` has a `Header`, an `AllowMultiSelect` flag (default `false`; opts the group into
letting more than one item be selected at once, combined with OR — leave it off for items whose
meaning only makes sense one at a time, e.g. overlapping/cumulative date ranges), and a list of
`SidebarFilterItem`s (Id, DisplayName, optional icon, and an optional async `FilterPredicate` over the
current result list). The host shows a clear button on a group once it has a selection, so a provider
doesn't need an "All"/"Any" pseudo-item of its own.

### `IResultColumnProvider`

Injects extra columns into the results grid view (file size, modified date, custom metadata, ...).

```csharp
interface IResultColumnProvider
{
    IEnumerable<ResultColumnDefinition> GetColumns();
    string GetCellValue(ISearchResult result, string columnId);
}
```

`ResultColumnDefinition` carries a column id, header text, width, and optional
`VisibilityPredicate`/`SortComparer` delegates.

## Startup Panel

### `IStartupPanelTabProvider`

Contributes a tab to the quick window's Startup Panel — the tab strip shown above the result list
when the search box is empty (see [Startup Panel](../../user-guide/settings/startup-panel)).
CoreExtensions' History and Favorites tabs are both built on this; see
[Example Plugins](../examples#coreextensions-actions-and-the-shell-context-menu) for a walkthrough.

```csharp
interface IStartupPanelTabProvider : IPluginComponent
{
    IAsyncEnumerable<ISearchResult> GetItemsAsync(CancellationToken cancellationToken = default);
}
```

`GetItemsAsync()` is called each time the panel is activated, not cached. It streams rather than
returning a finished set: the tab appears when the first item arrives and fills in as the rest do, so
a provider that has to go and look costs only its own tab's completeness, never the panel's
appearance. One with everything already in memory can yield straight from a list and pays nothing for
the shape. The token is cancelled when the panel closes or is reactivated — honour it rather than
enumerate on for a panel nobody is looking at.

A tab that yields nothing is left out of the strip entirely rather than shown empty. The user can
hide a tab from the live panel with its **×** button independently of disabling the component
altogether in Settings → Plugins — the two are deliberately separate; the host uses the component's
concrete class type name (`GetType().Name`) as the stable key to persist the closed state.

## Quick Panel

### `IQuickPanelSourceProvider`

Contributes a source to the [Quick Panel](../../user-guide/settings/quick-panel) — the floating panel
docked over whatever window is in front. A source becomes one group there, with its own heading, and
the host renders the entries through its own result rows, so icons, opening, and the actions menu all
come for free. CoreExtensions ships three: Windows Recent Items, History and Favorites.

```csharp
interface IQuickPanelSourceProvider : IPluginComponent
{
    Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default);
}
```

`GetEntriesAsync()` is called every time the panel is summoned. Deliberately not the streaming shape
`IStartupPanelTabProvider` uses: this panel orders and caps a source's entries as a set (newest first,
or by name, at most so many), so it cannot show half of one without re-sorting the group on every
arrival. That costs nothing in latency — every source of every workspace loads on its own task and
the panel opens on the first one to arrive, so a provider that has to go and look delays only its own
group. Honour the token all the same: it is cancelled when the panel closes.

Fill in `ISearchResult.Metadata`'s `Modified` where the source knows one and the group's default
newest-first order uses it; leave it at its default and the entries keep the order they were returned
in. A source that returns nothing produces no group, and a workspace whose sources all return nothing
gets no tab.

Where the source appears is the user's: they add it to whichever workspaces they want under Settings
→ Quick Panel → Plugin sources, and each of those remembers its own position, whether it is hidden,
what it is called and how it is displayed — all keyed by the component id, alongside their own
folders.

## Preview & thumbnails

### `IFilePreviewProvider`

Renders a custom WPF `UIElement` in the QuickLook preview pane (see
[Actions Menu & Preview](../../user-guide/actions-and-preview#quicklook-preview)) for file types
you want to handle specially.

```csharp
interface IFilePreviewProvider
{
    string Name { get; }
    int Priority { get; } // default 0; higher runs first
    bool CanPreview(string path, bool isDir);
    UIElement CreatePreview(string path, bool isDir);
    bool RendersExternally { get; } // default false
}
```

`Priority` is only the *default* order — the user can freely reorder providers (including relative
to yours) from Settings → General →
[Preview & Thumbnails](../../user-guide/settings/general#preview-thumbnails), which wins over
whatever `Priority` returns. Don't assume your provider's declared priority is the order it actually
runs in.

Two optional companion interfaces refine preview behavior:

- **`IPreviewSessionAware`** — implement this on the preview provider itself if it holds onto
  expensive out-of-process resources (a hosted native handler, a file lock); `EndPreviewSession()`
  is called once the whole preview session ends, not on every individual preview swap. The one
  exception: for a provider with `RendersExternally` true, the host calls it on every swap away
  from that provider too, not just session end — see below.
- **`IReusablePreview`** — implement this on the `UIElement` returned from `CreatePreview` if it
  can re-point itself at a new file instead of being rebuilt from scratch: `TrySetTarget(path,
  isDir)` returns `true` if it handled the change in place, `false` to tell the host to build a
  fresh preview instead.

`RendersExternally` is for a provider whose real preview surface is a separate, externally-managed
window rather than the `UIElement` `CreatePreview` returns — e.g. handing the file off to another
application entirely. When the winning provider has this set, the host hides its own preview panel
instead of displaying `CreatePreview`'s content (which is then never actually shown, so it can be
a trivial placeholder). Pair it with **`IReceivesPreviewPanelBounds`** to get the exact screen
rectangle (physical pixels) the host's own panel would have occupied, so the external window can be
positioned there instead of wherever it would otherwise appear:

```csharp
interface IReceivesPreviewPanelBounds
{
    void OnPreviewPanelBoundsAvailable(int left, int top, int width, int height);
}
```

See the bundled (experimental) QuickLook Bridge plugin for a real example: it detects an external
[QuickLook](https://github.com/QL-Win/QuickLook) app over its own named pipe and, if reachable,
docks that app's window into the host panel's spot for every file/folder — see [Actions Menu &
Preview → External preview via QuickLook](../../user-guide/actions-and-preview#external-preview-via-quicklook-optional)
for the user-facing behavior. Note this is a different thing from SwiftList's own built-in preview
pane, which is also informally called "QuickLook" throughout this codebase and docs.

### `IThumbnailProvider`

Overrides the icon/thumbnail shown for matching results.

```csharp
interface IThumbnailProvider : IPluginComponent
{
    int Priority { get; } // default 0; higher runs first
    bool CanProvideThumbnail(string path, bool isDir);
    ImageSource? GetThumbnail(string path, int size);
}
```

Same caveat as `IFilePreviewProvider.Priority` above: it's only the default order, and the user can
override it from Settings → General →
[Preview & Thumbnails](../../user-guide/settings/general#preview-thumbnails) (the same tab hosts both
providers' order lists).

## Themes & localization

### `IThemeProvider` / `ITheme`

Registers one or more custom WPF resource dictionaries as selectable themes (shown in
**Settings → General → Interface theme**).

```csharp
interface IThemeProvider
{
    string Name { get; }
    IEnumerable<ITheme> GetThemes();
}

interface ITheme
{
    string Id { get; }
    string DisplayName { get; }
    bool IsDark { get; }
    double WindowOpacity { get; } // default 1.0
    ResourceDictionary GetResources();
}
```

### `ITranslationProvider`

Supplies UI strings for a given culture — for the plugin's own UI, or (as with `PinyinAlias`) just
its own display name. See [Example Plugins](../examples) for a plugin that implements this
alongside an unrelated interface on the same class.

```csharp
interface ITranslationProvider
{
    string Name { get; }
    IReadOnlyList<string> SupportedCultures { get; } // e.g. "zh-CN", "en-US"
    IReadOnlyDictionary<string, string> GetTranslations(string cultureName);
}
```

`TranslationService.LoadEmbeddedTranslations` (see [Host Services](./services)) is the standard way
to back this with JSON files embedded in your plugin DLL.
