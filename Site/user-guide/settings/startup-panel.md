# Startup Panel

A tab strip shown above the result list in the quick window whenever the search box is empty,
giving quick access to recent files, favorites, and history without typing a query.

- **Enable the startup panel** — master switch; off means the panel never activates at all,
  regardless of the per-tab settings below.

Four sub-tabs: **Recent Files**, **Last Directory**, **Plugin Tabs**, and **Tab Order**.

## Recent Files

- **Enable panel** — checkbox; shows the tab when the search box is empty.
- **Directories** — folders to watch, one per line (same add/edit/remove-row and bulk-text editing
  as [Exclusion Rules](./index-drives#exclusion-rules)). Can include local drives, mapped network drives,
  and WSL paths (`\\wsl$\...` or `\\wsl.localhost\...`) — matched against whichever of those you've
  already configured and indexed under [Index](./index-drives).
- **Maximum number of files to show** — range 1–100, default 10.
- **Time range (minutes)** — only files modified within this many minutes of now are eligible, on
  top of the count cap above. Range 1–43200 (30 days), default 60.

Files only: a folder's own modified time changes whenever anything is added to or removed from it, so
folders were among the newest things in any directory being worked in and took the top of a list that
exists to show what you were working *on*.

Each entry's second line is prefixed with a relative time — "X seconds/minutes/hours/days ago" —
built from that file's last-modified timestamp already stored in the index, so it costs no extra
disk access. Hours and days drop the smaller unit when it's zero (e.g. "2 hours ago" instead of
"2 hours 0 minutes ago").

## Last Directory

- **Enable panel** — checkbox, on by default.

Shows the contents of whatever folder a native file open/save dialog was last navigated to, while
SwiftList's own dialog-interception feature was active — a quick way back to wherever you were just
browsing through a file picker, without needing to remember the path. The same hidden/system-file
filtering and [Exclusion Rules](./index-drives#exclusion-rules) the real index applies also apply
here, so things like `$RECYCLE.BIN` don't show up. This tab doesn't appear at all if that hasn't
happened yet this session, the folder no longer exists, or it's your Desktop (too often just the
default landing spot, not a real navigation target).

## Plugin Tabs

Plugin-provided tabs (e.g. History, Favorites) each show a **×** button in the live panel to hide
them for now. This is a panel-local "hide it" choice — separate from disabling the plugin component
itself in [Plugins](./plugins), which stops it from being used at all. A tab closed this way is
listed here, grouped by the plugin that provides it, unchecked; check it to bring it back.

Only tabs whose plugin component is currently enabled show up in this list — one disabled entirely
under [Plugins](./plugins) never becomes a tab candidate in the first place.

## Tab Order

The same up/down-arrow (or drag-to-reorder) list used elsewhere in Settings (see [Favorites](./favorites)), but
covering the panel's tab strip as a whole — both built-in tabs (Recent Files, Last Directory) and
every currently-visible plugin tab, all in one flat list, unlike Plugin Tabs above which groups by
plugin. Move a tab up or down to change where it lands relative to the others; left to right in this
list is left to right in the live panel.

Only tabs that would actually show right now are listed — a Recent Files/Last Directory tab you've
disabled above, or a plugin tab closed with its **×** button, has nothing to order until it's
re-enabled or reopened.
