# URI Protocol (swiftlist://)

SwiftList registers itself as the handler for a `swiftlist://` link — no separate installer step,
it's set up automatically the first time the app runs. This lets anything that can open a link
(a browser, a shortcut, another app, a script) jump straight into a specific part of SwiftList,
instead of only being reachable through a hotkey.

If SwiftList isn't already running, opening a `swiftlist://` link starts it and then follows the
link. If it's already running, the running instance handles the link directly — it never starts a
second copy.

## Routes

| Link | What it does |
|---|---|
| `swiftlist://` | Activates the quick search window — same as summoning it with its hotkey. |
| `swiftlist://search/[keyword]` | Activates the quick search window with `[keyword]` pre-filled. |
| `swiftlist://fullsearch/[keyword]` | Opens the full search window with `[keyword]` pre-filled. |
| `swiftlist://settings/page/[section]` | Opens Settings to a specific top-level section. |
| `swiftlist://settings/entry/[index]` | Opens Settings and jumps straight to one specific setting, highlighted. |

```
swiftlist://search/report
swiftlist://settings/page/Appearance
```

The first activates the quick search window already filtered to "report"; the second opens
Settings directly on the Appearance page.

`[section]` matches one of the top-level sidebar entries: `Service`, `Index`, `General`,
`Appearance`, `Hotkeys`, `Plugins`, `Favorites`, `History`, `QuickPanel`, `About` — not
case-sensitive.

`[index]` isn't meant to be typed by hand — it's a number [Settings Search](./instant-answers)
generates itself for whatever setting you picked, so selecting one of its results round-trips
straight back to that exact row. It isn't stable across restarts, so don't rely on a specific
number staying the same.

## Unrecognized links

Anything that doesn't match a known route — a typo, an unsupported section, garbage after
`swiftlist://` — is silently ignored. Since any website or app can invoke this protocol without
asking you first, a bad or unexpected link should never do anything surprising; it's logged for
your own troubleshooting, but nothing else happens.
