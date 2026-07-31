using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using SwiftList.App.ViewModels.QuickPanel;
using SwiftList.App.Views.QuickPanel;

namespace SwiftList.App.Services.QuickPanel;

/// <summary>
/// Owns the quick panel: the F2 trigger, the window's lifetime, and where it docks.
/// </summary>
/// <remarks>
/// PROTOTYPE. Two things here are deliberately provisional.
///
/// F2 is hardcoded and registered with RegisterHotKey rather than going through the hook service like
/// every real hotkey does. The hook service route needs a key to be recognised there, a new IPC message
/// to carry it, a case in the paired serializer, and the service restarted before any of it can be
/// tried; RegisterHotKey is self-contained in the app and can be thrown away whole when this graduates
/// to a configurable hotkey.
///
/// The panel is also created once and reused, hidden rather than closed, matching the quick window. A
/// prototype that rebuilt it per invocation would reload the tabs from scratch every time, which hides
/// exactly the kind of state bug worth finding early.
/// </remarks>
public sealed class QuickPanelManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0xB101;
    private const uint MOD_NONE = 0x0000;
    private const uint VK_F2 = 0x71;

    // Floors for the quarter-of-the-host sizing: a panel docked to a small window still needs enough
    // room for a row to read as a row.
    // A quarter of the host's AREA, which is half of each side rather than a quarter: a quarter per
    // side would come to a sixteenth of the window, which is what it looked like.
    private const double PanelSideFactor = 0.5;

    private const double MinPanelWidth = 280;
    private const double MinPanelHeight = 200;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

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

    private readonly HwndSource _messageSource;
    private QuickPanelWindow? _window;
    private QuickPanelViewModel? _viewModel;

    public QuickPanelManager()
    {
        // A message-only window to own the hotkey. The app's real windows come and go (and the quick
        // window is hidden most of the time), so hanging a process-lifetime registration off any of
        // them would tie it to that window's lifetime for no reason.
        _messageSource = new HwndSource(new HwndSourceParameters("SwiftListQuickPanelHotkey")
        {
            WindowStyle = 0,
            ParentWindow = (IntPtr)(-3), // HWND_MESSAGE
        });
        _messageSource.AddHook(OnMessage);

        if (!RegisterHotKey(_messageSource.Handle, HotkeyId, MOD_NONE, VK_F2))
        {
            Core.Logger.Log($"[QuickPanel] F2 is already taken by something else, so the panel has no trigger. Error={Marshal.GetLastWin32Error()}", Core.LogLevel.Warn);
        }

        Instance = this;
    }

    private IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_HOTKEY || (int)wParam != HotkeyId) return IntPtr.Zero;

        handled = true;
        Toggle();
        return IntPtr.Zero;
    }

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

        Show(host);
    }

    private void Show(IntPtr host)
    {
        _viewModel ??= new QuickPanelViewModel();

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
        _window.Show();

        // After Show, and only then: the window has to exist as a real HWND before it can be given the
        // foreground. ShowActivated="False" means it comes up without focus by design, so this is what
        // actually hands it over.
        _window.ActivateAndFocus();

        _ = _viewModel.ActivateAsync();
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
            _viewModel?.Deactivate();
        }
        finally
        {
            _hiding = false;
        }
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
        UnregisterHotKey(_messageSource.Handle, HotkeyId);
        _messageSource.RemoveHook(OnMessage);
        _messageSource.Dispose();

        _window?.Close();
        _window = null;

        if (ReferenceEquals(Instance, this)) Instance = null;
    }
}
