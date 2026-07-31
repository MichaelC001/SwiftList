// Both WinForms and WPF are referenced in this project, and both define UserControl.
using UserControl = System.Windows.Controls.UserControl;

namespace SwiftList.App.Views.Settings.Plugins;

// The plugin config editor as it appears inside a plugin's card. All of the behavior lives in the
// XAML's bindings and in FieldRowTemplate's own handlers, so there is nothing here but the load.
public partial class PluginConfigSection : UserControl
{
    public PluginConfigSection()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Shift+wheel scrolls a field list sideways.
    /// </summary>
    /// <remarks>
    /// A ScrollViewer does not do this on its own -- the wheel is hard-wired to vertical scrolling -- so
    /// without this the horizontal bar these lists can grow is reachable only by dragging it. Carried
    /// over from the config window this section replaced, which had the same handler for the same reason.
    ///
    /// Only the field lists scroll sideways, not the pane around them: the plugin's name, description and
    /// tab strip have nothing to scroll to, and dragging them off-screen alongside a wide field row is
    /// what happened when this lived on the outer scroller instead.
    /// </remarks>
    private void FieldScroll_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (sender is not System.Windows.Controls.ScrollViewer scrollViewer) return;
        if (System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.Shift) return;

        if (e.Delta < 0)
            scrollViewer.LineRight();
        else
            scrollViewer.LineLeft();

        e.Handled = true;
    }
}
