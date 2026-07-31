using System.Windows;
using System.Windows.Interop;

namespace SwiftList.App.Helpers.Visuals;

/// <summary>
/// Blocks two OS-level WM_SYSCOMMAND triggers: Alt+Space (SC_KEYMENU), which pops up the OS-drawn
/// system menu (Restore/Move/Size/Minimize/Maximize/Close) as a jarring blank box clipped by the
/// window's own borderless/rounded corners instead of a real title bar; and Alt+F4 (SC_CLOSE), which
/// would let the OS close the window out from under the app's own show/hide lifecycle. Every other
/// WM_SYSCOMMAND subcommand is left untouched.
/// </summary>
/// <remarks>
/// Used by every custom-chrome window except the two the user opens and is done with, the settings
/// window and the full search window: those are ordinary resizable windows, so Alt+F4 and Alt+Space
/// behaving normally is the right thing there rather than something to suppress.
///
/// The quick window is the one that genuinely cannot do without this, being the application's
/// MainWindow with no ShutdownMode set, so letting Alt+F4 through would take the whole tray app down
/// with it. Attaching it to the inline window has never reliably covered that one: unresolved, and
/// left as it is rather than pursued here.
/// </remarks>
public static class SystemMenuBlocker
{
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_KEYMENU = 0xF100;
    private const int SC_CLOSE = 0xF060;

    public static void Attach(Window window)
    {
        if (PresentationSource.FromVisual(window) is HwndSource hwndSource)
        {
            Hook(hwndSource);
        }
        else
        {
            window.SourceInitialized += (s, e) =>
            {
                if (PresentationSource.FromVisual(window) is HwndSource src)
                    Hook(src);
            };
        }
    }

    private static void Hook(HwndSource hwndSource) => hwndSource.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
    {
        if (msg == WM_SYSCOMMAND)
        {
            var command = (int)wParam & 0xFFF0;
            if (command == SC_KEYMENU || command == SC_CLOSE)
                handled = true;
        }
        return IntPtr.Zero;
    });
}
