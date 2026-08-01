namespace SwiftList.App.ViewModels.Settings.QuickPanel;

/// <summary>One plugin-provided tab, and whether the strip currently shows it.</summary>
/// <remarks>
/// Deliberately not the same question as the plugin page's own enable/disable, which governs whether the
/// provider is loaded at all. This one is only about the strip: a tab closed with its x is unticked
/// here, and ticking it puts the tab back.
///
/// Ticked by default, unlike a folder, which the user goes and adds: this tab is there because a plugin
/// they installed offers it.
/// </remarks>
public sealed class QuickPanelPluginTabOption : ViewModelBase
{
    public QuickPanelPluginTabOption(string id, string name, bool isOpen)
    {
        Id = id;
        Name = name;
        _isOpen = isOpen;
    }

    public string Id { get; }

    public string Name { get; }

    private bool _isOpen;

    public bool IsOpen
    {
        get => _isOpen;
        set => SetProperty(ref _isOpen, value);
    }
}
