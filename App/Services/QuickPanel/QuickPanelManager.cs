using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using SwiftList.App.ViewModels.QuickPanel;
using SwiftList.App.Views.QuickPanel;

namespace SwiftList.App.Services.QuickPanel;

/// <summary>
/// Owns the quick panel: its lifetime and where it docks. The hotkey that opens it belongs to the hook
/// service, which sends QuickPanelHotkey when the configured combination fires.
/// </summary>
/// <remarks>
/// The panel is created once and reused, hidden rather than closed, matching the quick window. That is
/// also why Alt+F4 is suppressed on it: an OS close would leave this holding a dead window.
///
/// Show returns without opening when there is nothing to show. An empty shell over the window in front
/// would only be in the way.
/// </remarks>
public sealed class QuickPanelManager : IDisposable
{

    // Floors for the quarter-of-the-host sizing: a panel docked to a small window still needs enough
    // room for a row to read as a row.
    // A quarter of the host's AREA, which is half of each side rather than a quarter: a quarter per
    // side would come to a sixteenth of the window, which is what it looked like.
    private const double PanelSideFactor = 0.5;

    private const double MinPanelWidth = 280;
    private const double MinPanelHeight = 200;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    public static QuickPanelManager? Instance { get; private set; }

    private QuickPanelWindow? _window;
    private QuickPanelViewModel? _viewModel;

    public QuickPanelManager() => Instance = this;


    public void Toggle()
    {
        // Read the foreground window before showing anything: the panel docks to whatever was in front
        // at the moment it was asked for, and showing first would make that "whatever was in front
        // before us", which is only the same window by luck.
        var host = GetForegroundWindow();

        if (_window is { IsVisible: true })
        {
            Hide();
            return;
        }

        var settings = Core.UserSettings.Load();
        if (!settings.QuickPanel.Enabled) return;

        // The same window the panel would dock to is the one that decides whether it may open at all and
        // which workspace it opens on, so its process is read once here and carried through both.
        var process = ProcessNameOf(host);
        if (Core.QuickPanelTabSelection.IsBlocked(process, settings))
        {
            Core.Logger.Log($"[QuickPanel] '{process}' is blacklisted, so not opening.", Core.LogLevel.Debug);
            return;
        }

        Show(host, process);
    }

    /// <summary>The foreground window's process name, which is what the workspace rules match on.</summary>
    private static string? ProcessNameOf(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;

        try
        {
            Core.Hook.ExplorerNativeHooks.GetWindowThreadProcessId(hwnd, out var pid);
            return pid != 0 ? System.Diagnostics.Process.GetProcessById((int)pid).ProcessName : null;
        }
        catch
        {
            // The window can be gone by the time its process is asked for. Null means "no app in front
            // worth naming", which every rule below already treats as "no claim, no block".
            return null;
        }
    }

    private bool _showing;

    private async void Show(IntPtr host, string? process)
    {
        // Show awaits a real load, and the hotkey that got here can be pressed again while it does --
        // at which point Toggle still sees a window that is not visible yet and starts a second one.
        // Two loads racing into the same collections is not a state worth reasoning about.
        if (_showing) return;
        _showing = true;
        try
        {
            await ShowCoreAsync(host, process);
        }
        finally
        {
            _showing = false;
        }
    }

    private async Task ShowCoreAsync(IntPtr host, string? process)
    {
        _viewModel ??= new QuickPanelViewModel();

        // Every open, not just the first: the panel is reused rather than rebuilt, so without this it
        // would keep showing whatever was true the first time it was ever opened. Awaited before the
        // window appears because the decision below depends on the result.
        await _viewModel.RefreshAsync(process);

        // Nothing to show, nothing to open: the panel exists to put content over the window in front,
        // and flashing an empty shell over it would only be in the way.
        if (!_viewModel.HasContent)
        {
            Core.Logger.Log("[QuickPanel] Nothing to show, so not opening.", Core.LogLevel.Debug);
            return;
        }


        if (_window == null)
        {
            _window = new QuickPanelWindow(_viewModel);
            // Losing the foreground dismisses the panel, the way the inline window goes when the user
            // clicks away. Subscribed once at construction rather than per show: the window is reused,
            // so wiring it on each Show would stack a fresh handler every time.
            _window.Deactivated += (_, _) =>
            {
                // Not while the window is being dragged: DragMove runs a modal loop the window comes
                // out of deactivated, which is not the user clicking away.
                if (_window is { IsDraggingWindow: true }) return;

                // Nor while the action flyout is up. It hangs its key handler on this window and
                // needs it alive to reach it, so hiding here would take the menu down with the panel
                // and leave every shortcut on it looking dead.
                if (SwiftList.App.Services.ShellMenu.ActionFlyout.ActionFlyout.IsOpen) return;
                Hide();
            };
        }

        // Positioned before Show: placing it afterwards lets the window paint once at its old location
        // and jump, which reads as a flicker every time the panel opens.
        PositionAgainst(host);

        // And laid out before Show, for the same reason one step further in. A hidden window does not
        // lay itself out, so the groups replaced above are still only a collection: the containers on
        // screen are the previous open's, at the previous open's size, and they are what the first
        // painted frame would show before the post-show pass replaced them. The quick window's own Show
        // forces the same pass for the same reason.
        _window.UpdateLayout();
        _window.Show();

        // After Show, and only then: the window has to exist as a real HWND before it can be given the
        // foreground. ShowActivated="False" means it comes up without focus by design, so this is what
        // actually hands it over.
        _window.ActivateAndFocus();

    }

    private bool _hiding;

    public void Hide()
    {
        if (_window == null || _hiding) return;

        // Hiding the window raises Deactivated, which is itself wired to this method, so without the
        // guard the first hide re-enters and deactivates the view model twice.
        _hiding = true;
        try
        {
            _window.Hide();
        }
        finally
        {
            _hiding = false;
        }

        // After the window is gone, never before: clearing while it is still up would lay the panel out
        // empty in front of the user on the way out. Everything it shows is loaded fresh on the next
        // open anyway, so holding the last workspace's groups behind a hidden window buys nothing and
        // costs a frame of the wrong content if anything renders before that load lands.
        //
        // Only if it is still gone. Window.Hide() pumps, so a hotkey press queued behind it can run an
        // entire Show -- awaits included -- before this line is reached, and clearing then empties a
        // panel that is on screen and freshly loaded. That is the "no tabs, nothing to show" state that
        // turned up over a window the user had just clicked back into.
        if (_window is { IsVisible: false })
            _viewModel?.Clear();
    }

    /// <summary>Docks the panel inside the host window's bottom-right corner.</summary>
    /// <remarks>
    /// GetWindowRect is in physical pixels while WPF's Left/Top are in device-independent units, so the
    /// rect is scaled by the host's own DPI rather than this window's: on a mixed-DPI setup the two can
    /// differ, and it is the host's corner being aimed at. Falls back to the working area's corner when
    /// there is no usable foreground window, which is what happens if the panel is triggered from the
    /// desktop itself.
    /// </remarks>
    private void PositionAgainst(IntPtr host)
    {
        if (_window == null) return;

        var margin = 12.0;

        if (host != IntPtr.Zero && GetWindowRect(host, out var rect))
        {
            var dpi = GetDpiForWindow(host);
            var scale = dpi > 0 ? 96.0 / dpi : 1.0;

            var right = rect.Right * scale;
            var bottom = rect.Bottom * scale;
            var hostWidth = (rect.Right - rect.Left) * scale;
            var hostHeight = (rect.Bottom - rect.Top) * scale;

            // A quarter of the window it docks to, floored so it stays usable against a small host.
            // Both axes, as asked: the panel is a quarter of the window it docks to. Fewer rows than fit
            // simply leave the rest of the panel empty, which is what a fixed proportion means.
            _window.Width = Math.Max(MinPanelWidth, hostWidth * PanelSideFactor);
            _window.Height = Math.Max(MinPanelHeight, hostHeight * PanelSideFactor);

            _window.Left = right - _window.Width - margin;
            _window.Top = bottom - _window.Height - margin;
            return;
        }

        var work = SystemParameters.WorkArea;
        _window.Left = work.Right - _window.Width - margin;
        _window.Top = work.Bottom - _window.Height - margin;
    }

    public void Dispose()
    {

        _window?.Close();
        _window = null;

        if (ReferenceEquals(Instance, this)) Instance = null;
    }
}
