using System.Diagnostics;
using System.Windows;
using SwiftList.Core;
using SwiftList.Core.Services;
using SwiftList.Core.Services.LocalSend.Models;
using SwiftList.App.Services;
using SwiftList.App.ViewModels.Search;
using Application = System.Windows.Application;
using MessageBox = SwiftList.App.Views.Controls.Dialogs.CustomMessageBox;
using SwiftList.App.Services.AppWindow;
using SwiftList.App.Services.Pipe;
using SwiftList.App.Services.Plugin;
using SwiftList.App.Services.ShellIcons;
using SwiftList.App.Services.Theme;
using SwiftList.App.Services.Update;
using SwiftList.App.Services.UrlProtocol;
using SwiftList.App.Services.ShellMenu.QuickNav;
using SwiftList.PluginSdk.Abstractions.Plugins.WindowAdapters;
using SwiftList.Core.Hook.Ipc;
using SwiftList.App.ViewModels.Search.Mapping;
using SwiftList.App.ViewModels.Settings.General;
namespace SwiftList.App;

public partial class App : Application
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    [System.Runtime.InteropServices.DllImport("shell32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    private Mutex? _appMutex;
    public static HookIpcClient? HookClient { get; private set; }

    // Held for the process lifetime so its hotkey registration and message window stay alive.
    private Services.QuickPanel.QuickPanelManager? _quickPanelManager;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // SwiftList never set an explicit AppUserModelID, so Windows infers one on its own (commonly
        // derived from the exe's own path) -- the taskbar's default/resting icon for windows from a
        // path Windows treats as an "installed app" (Program Files + Start Menu registration) came
        // from that inferred identity rather than the live window icon ThemedWindowIconHelper sets,
        // even though title bar/Alt-Tab (which read the live window directly) were already correct.
        // Owning the identity explicitly is also just standard practice for a real desktop app
        // (correct taskbar grouping/pinning/jump-list/notification behavior).
        try
        {
            // Derived from the assembly name (App.csproj's <AssemblyName>) rather than a hardcoded
            // literal, so the two can't drift apart if the assembly is ever renamed. A null Name here
            // would mean the executing assembly has no name at all, which can't happen in practice;
            // the surrounding try/catch is the fallback if it somehow did.
            var appId = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name!;
            SetCurrentProcessExplicitAppUserModelID(appId);
        }
        catch { /* best-effort; taskbar grouping falls back to Windows' own inference */ }

        // Only this thread (the Dispatcher), not Process.PriorityClass -- keeps input/rendering responsive
        // under CPU contention without making the whole process compete unfairly against everything else.
        Thread.CurrentThread.Priority = ThreadPriority.Highest;

        // Initialize logger first so we can log elevation decisions and issues

        Logger.Initialize("app.log", overwrite: true);
        var settings = UserSettings.Load();
        Logger.MinimumLevel = SettingsOptionGenerator.ParseLogLevel(settings.LogLevel);
        // Everything this process matches outside the search pipeline -- plugin catalog items,
        // favorites, shell-menu filtering, display highlighting -- reads this rather than the
        // per-request value, which only ever reaches the search pipeline's own async flow.
        SearchContext.DefaultFuzzyMatchEnabled = settings.EnableFuzzyMatch;
        StartupManager.SetEnabled(settings.StartWithWindows);
        Logger.Log("=========================================");
        Logger.Log($"Application starting with arguments: {string.Join(" ", e.Args)}");
        Logger.Log($"[App] Running as Administrator: {ElevationManager.IsRunningAsAdmin()}");

        // Single instance check per user session

        // We append the username to guarantee multi-user isolation on the same machine

        var mutexName = $@"Local\SwiftList_App_{Environment.UserName}";
        _appMutex = new Mutex(true, mutexName, out var createdNew);
        if (!createdNew)
        {
            try
            {
                var current = Process.GetCurrentProcess();
                foreach (var proc in Process.GetProcessesByName(current.ProcessName))
                {
                    if (proc.Id != current.Id)
                    {
                        AllowSetForegroundWindow(proc.Id);
                    }
                }
            }

            catch { }

            // Send activation command to the already running process and then exit immediately.
            // A swiftlist:// launch arg is forwarded as-is so the running instance can route it;
            // anything else (a plain second launch) falls back to the bare activate signal.
            var launchUri = e.Args.Length > 0 && UriRouter.IsSwiftListUri(e.Args[0]) ? e.Args[0] : null;
            await AppPipeService.SendActivateSignalAsync(launchUri);
            Shutdown();
            return;
        }

        HookClient = new HookIpcClient();

        HookClient.OnMouseDoubleClick += (x, y) =>
        {
            if (!UserSettings.Load().Hotkeys.QuickNavTriggerOnDoubleClick) return;
            if (Views.InlineSearchWindow.Helpers.InlineSearchWindowNativeMethods.IsPointInsideWindow(x, y)) return;
            var trk = InlineSearchManager.Instance.ExplorerTracker;
            var proc = GetProcessNameOfWindow(trk.ActiveHwnd);
            var cls = GetClassNameOfWindow(trk.ActiveHwnd);
            if (QuickNavigationTriggerGate.CanShow(trk.ActiveHwnd, proc, cls, trk.IsDesktop, x, y, MouseTriggerType.DoubleClick))
                Dispatcher.BeginInvoke(() => QuickNavigationMenu.Show(x, y));
        };

        HookClient.OnMouseMiddleClick += (x, y) =>
        {
            if (!UserSettings.Load().Hotkeys.QuickNavTriggerOnMiddleClick) return;
            if (Views.InlineSearchWindow.Helpers.InlineSearchWindowNativeMethods.IsPointInsideWindow(x, y)) return;
            var trk = InlineSearchManager.Instance.ExplorerTracker;
            var proc = GetProcessNameOfWindow(trk.ActiveHwnd);
            var cls = GetClassNameOfWindow(trk.ActiveHwnd);
            if (QuickNavigationTriggerGate.CanShow(trk.ActiveHwnd, proc, cls, trk.IsDesktop, x, y, MouseTriggerType.MiddleClick)
                || FileDialogQuickNavGate.CanShow(trk.ActiveHwnd, proc, cls, x, y))
                Dispatcher.BeginInvoke(() => QuickNavigationMenu.Show(x, y));
        };

        HookClient.OnActivated += () => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (InlineSearchManager.Instance.IsInlineSearchActive)
            {
                InlineSearchManager.Instance.FocusSearchBox();
            }
            else
            {
                var quickSearchWindow = Current.MainWindow as QuickSearchWindow;
                quickSearchWindow?.ToggleVisibility();
            }
        }));
        HookClient.OnQuickPanelHotkey += () => Dispatcher.BeginInvoke(
            new Action(() => _quickPanelManager?.Toggle()));

        HookClient.Start();

        // The quick panel. Built here rather than lazily on the first hotkey so the handler above always
        // has something to call; it creates no window of its own until it is first opened.
        _quickPanelManager = new Services.QuickPanel.QuickPanelManager();

        // Set up global exception handlers
        AppDomain.CurrentDomain.UnhandledException += (s, args) => LogException("AppDomain UnhandledException", args.ExceptionObject as Exception);
        DispatcherUnhandledException += (s, args) => { LogException("DispatcherUnhandledException", args.Exception); args.Handled = true; };
        TaskScheduler.UnobservedTaskException += (s, args) => { LogException("TaskScheduler UnobservedTaskException", args.Exception); args.SetObserved(); };

        // Force load all plugins (actions and alias providers) on startup
        _ = PluginManager.Instance;
        _ = Task.Delay(10000).ContinueWith(_ => Win32Api.TrimWorkingSet());

        try
        {
            PluginSdk.Services.TranslationService.LookupFunc = key => TranslationManager.Instance[key];
            PluginSdk.Services.TranslationService.CurrentCultureFunc = () => TranslationManager.Instance.CurrentCulture;
            PluginSdk.Services.LocalSendTransferService.OpenSendWindowFunc = (files, text) =>
                Core.Services.LocalSend.LocalSendServiceManager.Instance.OpenSendWindow(files, text);
            PluginSdk.Services.SearchRefreshService.RefreshMatchingFunc = queryMatches =>
                // Callers may invoke this from a background thread (e.g. after an async fetch
                // completes), so marshal onto the UI thread here rather than requiring every caller
                // to remember to do so themselves.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (Window window in Windows)
                    {
                        if (window.DataContext is QuickSearchViewModel quickVm)
                        {
                            var currentQuery = quickVm.SearchQuery;
                            if (queryMatches(currentQuery))
                                quickVm.Search.PerformSearch(currentQuery);
                        }
                        else if (window.DataContext is SearchViewModel searchVm)
                        {
                            var currentQuery = searchVm.AdvancedQuery;
                            if (queryMatches(currentQuery))
                                searchVm.PerformSearch(currentQuery);
                        }
                    }
                }));
            PluginSdk.Services.IconService.GetIconFunc = (path, isDir) => ShellIconHelper.GetIconForPath(path, isDir);
            PluginSdk.Services.IconService.GetIconCacheOnlyFunc = (path, isDir) =>
            {
                var icon = ShellIconHelper.GetIconFromCacheOnly(path, isDir, out var needsLoad);
                return (icon, needsLoad);
            };
            PluginSdk.Services.IconService.GetThumbnailFunc = (path, size) => ShellImageListInterop.TryGetPreviewThumbnail(path, size);
            PluginSdk.Services.FileMetadataService.BatchLookupFunc = FileMetadataBridge.GetMetadataBatchAsync;
            // Cached across calls: this feed's own doc comment calls it "the host's static list of
            // searchable settings entries", but the naive version (call BuildAllEntries fresh every
            // time) silently broke that -- CoreExtensions' SearchSettingsInstantProvider calls
            // GetEntries() on every debounced keystroke of a "set ..." query in the main search window,
            // which was re-running BuildAllEntries(vm: null)'s PluginLoaderHelper.BuildPluginList
            // reflection scan (AppDomain.GetAssemblies + two GetTypes() passes per plugin DLL) per
            // keystroke -- independent of whether Settings was even open, and worse than the
            // once-per-window-open cost issue #186 was about. Safe to cache: with vm: null, none of the
            // built entries' Activate/Reveal delegates (which close over live PluginInfoViewModel/etc.
            // instances) are ever invoked -- JumpToEntry always rebuilds fresh against the real live vm
            // before activating anything, using the index purely as a positional lookup -- so only the
            // translated Label/Breadcrumb/Index actually returned here need to stay current. Invalidated
            // on language change (labels/breadcrumbs are translated at build time) and on
            // PluginManager.ComponentsRefreshed: unlike the Plugins-section entries (which include every
            // component regardless of IsEnabled, only ever toggling a flag PluginLoaderHelper doesn't
            // even expose here), PluginManager.QuickPanelTabProviders -- which the QuickPanel-section
            // entries are built from -- IS enabled-filtered, so disabling a quick-panel-tab-providing
            // component genuinely changes this feed's membership, not just some unexposed flag on it.
            List<PluginSdk.Services.SettingsSearchEntryInfo>? cachedSettingsSearchEntries = null;
            TranslationManager.Instance.PropertyChanged += (_, _) => cachedSettingsSearchEntries = null;
            PluginManager.Instance.ComponentsRefreshed += () => cachedSettingsSearchEntries = null;
            PluginSdk.Services.SettingsSearchService.GetEntriesFunc = () =>
            {
                if (cachedSettingsSearchEntries != null)
                    return cachedSettingsSearchEntries;

                // No live SettingsWindow is guaranteed to exist here (Settings may never have been
                // opened yet), so this passes vm: null -- BuildAllEntries then builds the Plugins/
                // Hotkeys-actions sections straight from PluginManager.Instance/
                // UserSettings instead of a live window's collections, and conservatively excludes any
                // conditionally-visible static entry (e.g. the WSL tab) it can't evaluate without one.
                var entries = SettingsWindowSearchExtensions.BuildAllEntries(vm: null);
                var list = new List<PluginSdk.Services.SettingsSearchEntryInfo>(entries.Count);
                for (var i = 0; i < entries.Count; i++)
                    list.Add(new PluginSdk.Services.SettingsSearchEntryInfo(entries[i].Label, entries[i].SectionLabel, i));
                cachedSettingsSearchEntries = list;
                return cachedSettingsSearchEntries;
            };
            PluginSdk.Logger.LogAction = (msg, lvl) => Logger.Log(msg, (LogLevel)(int)lvl);
            TranslationManager.Instance.ReloadTranslations();
            Logger.Log("[App] TranslationManager initialized.");

            // Preload app searchable items now that translations are fully loaded and settled
            SearchableItemMapper.Preload();

            var startupThemeId = settings.ThemeFollowSystem
                ? ThemeManager.Instance.ResolveLightDarkThemeId(SystemThemeWatcher.IsSystemLight, settings)
                : settings.Theme;
            ThemeManager.Instance.Initialize(startupThemeId);
            ThemeManager.Instance.InitializeSystemFollow();
            Logger.Log($"[App] ThemeManager initialized with theme: {startupThemeId}");
        }
        catch (Exception ex)
        {
            Logger.Log($"[App] Failed to initialize TranslationManager or ThemeManager: {ex.Message}", LogLevel.Error);
        }

        // Start the activation named pipe server to listen to subsequent launches

        _ = AppPipeService.StartPipeServerAsync();
        _ = AppSearchPipeService.StartPipeServerAsync(); // exposes the full window's search to external clients (see AppSearchPipeService)
        AppStartupServiceBootstrapper.EnsureServiceStarted();
        UrlProtocolManager.EnsureRegistered();
        Logger.Log("Starting normal WPF GUI client mode.");
        base.OnStartup(e);

        // After QuickSearchWindow is created (via StartupUri), start InlineSearchManager. base.OnStartup
        // above does NOT create the StartupUri window synchronously -- that happens once the Dispatcher
        // message loop actually starts, which is still later than this point -- hence deferring to
        // DispatcherPriority.Loaded rather than running inline here.
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (Current.MainWindow is QuickSearchWindow quickSearchWindow)
            {
                InlineSearchManager.Instance.Start();
                Logger.Log("[App] InlineSearchManager started.");
            }

            // This process won the single-instance mutex above, so if it was itself launched via a
            // swiftlist:// link (rather than a second instance forwarding one through the pipe -- see
            // the mutex branch above), route it here. Must run in this same deferred callback: routing
            // to the quick/full search window needs MainWindow already set, which (see comment above)
            // isn't guaranteed yet any earlier than this.
            if (e.Args.Length > 0 && UriRouter.IsSwiftListUri(e.Args[0]))
                UriRouter.Route(e.Args[0]);
        }), System.Windows.Threading.DispatcherPriority.Loaded);

        // Background update check on startup
        UpdateCheckService.RunOnStartupAsync();

        // LocalSend transfer service runs in App process
        Core.Services.LocalSend.LocalSendServiceManager.Instance.ApplySettings(settings);
        Core.Services.LocalSend.LocalSendServiceManager.Instance.ProgressChanged += OnLocalSendProgressChanged;
        Core.Services.LocalSend.LocalSendServiceManager.Instance.SessionCanceled += OnLocalSendSessionCanceled;
        Core.Services.LocalSend.LocalSendServiceManager.Instance.TextReceived += OnLocalSendTextReceived;
        Core.Services.LocalSend.LocalSendServiceManager.Instance.UploadRequested += OnLocalSendUploadRequested;
        Core.Services.LocalSend.LocalSendServiceManager.Instance.SendRequested += OnLocalSendSendRequested;
    }

    private static Views.LocalSend.LocalSendProgressWindow? _activeLocalSendProgressWindow;
    private static Core.Services.LocalSend.LocalSendProgressArgs? _pendingProgressArgs;
    private static bool _isProgressDispatchPending;

    private static void OnLocalSendProgressChanged(object? sender, Core.Services.LocalSend.LocalSendProgressArgs e)
    {
        _pendingProgressArgs = e;

        if (e.IsAllDone || e.IsFinished || !_isProgressDispatchPending)
        {
            _isProgressDispatchPending = true;
            Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                _isProgressDispatchPending = false;
                var argsToUpdate = _pendingProgressArgs;
                if (argsToUpdate == null) return;

                if (_activeLocalSendProgressWindow == null || !_activeLocalSendProgressWindow.IsLoaded)
                {
                    _activeLocalSendProgressWindow = new Views.LocalSend.LocalSendProgressWindow();
                    _activeLocalSendProgressWindow.Closed += (_, _) => _activeLocalSendProgressWindow = null;
                    _activeLocalSendProgressWindow.Show();
                }

                _activeLocalSendProgressWindow.UpdateProgress(argsToUpdate);
            }));
        }
    }

    private static void OnLocalSendSessionCanceled(object? sender, string sessionId) => Current.Dispatcher.BeginInvoke(new Action(() => _activeLocalSendProgressWindow?.HandleSessionCanceled(sessionId)));

    private static void OnLocalSendTextReceived(object? sender, (string SenderAlias, string Text, bool IsLink) e) => Current.Dispatcher.BeginInvoke(new Action(() =>
    {
        if (e.IsLink)
        {
            var title = TranslationManager.Instance["Settings_LocalSend_LinkReceivedTitle"];
            var openText = TranslationManager.Instance["Settings_LocalSend_OpenInBrowser"];
            var cancelText = TranslationManager.Instance["Common_Close"];
            var msg = $"{e.SenderAlias}:\n{e.Text}";

            var result = MessageBox.ShowCustom(
                msg, title, openText, cancelText, MessageBoxImage.Information);

            if (result == MessageBoxResult.OK)
            {
                try { Process.Start(new ProcessStartInfo(e.Text) { UseShellExecute = true }); }
                catch { }
            }
        }
        else
        {
            var title = TranslationManager.Instance["Settings_LocalSend_TextReceivedTitle"];
            var copyText = TranslationManager.Instance["Settings_LocalSend_CopyToClipboard"];
            var cancelText = TranslationManager.Instance["Common_Close"];
            var msg = $"{e.SenderAlias}:\n{e.Text}";

            var result = MessageBox.ShowCustom(
                msg, title, copyText, cancelText, MessageBoxImage.Information);

            if (result == MessageBoxResult.OK)
            {
                try
                {
                    System.Windows.Clipboard.SetText(e.Text);
                }
                catch (Exception ex)
                {
                    Logger.Log($"[App] Failed to set clipboard text: {ex.Message}", LogLevel.Warn);
                }
            }
        }
    }));

    private static void OnLocalSendSendRequested(object? sender, (IReadOnlyList<string>? Files, string? Text) e) => Current.Dispatcher.BeginInvoke(new Action(() =>
    {
        var sendWin = new Views.LocalSend.LocalSendSendWindow(e.Files, e.Text);
        sendWin.Show();
        sendWin.Activate();
    }));

    private static void OnLocalSendUploadRequested(object? sender, LocalSendUploadRequestArgs e) => Current.Dispatcher.BeginInvoke(new Action(() =>
    {
        var win = new Views.LocalSend.LocalSendReceiveRequestWindow(e.Dto);
        var res = win.ShowDialog();

        if (res == true)
        {
            if (win.Result == Views.LocalSend.LocalSendReceiveResult.AcceptCustomDir && !string.IsNullOrWhiteSpace(win.CustomDirectory))
            {
                e.CustomDownloadDirectory = win.CustomDirectory;
            }
            e.Respond(true);
        }
        else
        {
            e.Respond(false);
        }
    }));

    public static void HideInlineSearch() => InlineSearchManager.Instance.CloseInlineSearch();

    private static string GetProcessNameOfWindow(IntPtr hwnd)
    {
        try { Core.Hook.ExplorerNativeHooks.GetWindowThreadProcessId(hwnd, out var pid); return pid != 0 ? Process.GetProcessById((int)pid).ProcessName : "Unknown"; }
        catch { return "Unknown"; }
    }

    private static string GetClassNameOfWindow(IntPtr hwnd)
    {
        var sb = new System.Text.StringBuilder(256);
        return hwnd != IntPtr.Zero && Core.Hook.ExplorerNativeHooks.GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "Unknown";
    }

    private static void LogException(string source, Exception? ex)
    {
        var details = ex != null ? ex.ToString() : "Null exception object";
        Logger.Log($"CRITICAL CRASH ({source}):\n{details}", LogLevel.Error);
        MessageBox.Show(string.Format(TranslationManager.Instance["Crash_Message"], source, ex?.Message, Logger.LogDir), TranslationManager.Instance["Crash_Title"], MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public static void ShowSettingsWindow(string? targetSection = null) => AppWindowManager.ShowSettingsWindow(targetSection);
    public static void ShowSearchWindow() => AppWindowManager.ShowSearchWindow();
    public static void CloseAllManagedWindows() => AppWindowManager.CloseAllManagedWindows();

    protected override void OnExit(ExitEventArgs e)
    {
        Core.Services.LocalSend.LocalSendServiceManager.Instance.Stop();
        HookClient?.Stop(); HookClient?.Dispose(); HookClient = null;
        AppPipeService.StopServer(); AppSearchPipeService.StopServer(); InlineSearchManager.Instance.Dispose(); CloseAllManagedWindows();
        if (_appMutex != null) { try { _appMutex.ReleaseMutex(); } catch { } _appMutex.Dispose(); }
        base.OnExit(e);
    }
}
