namespace SwiftList.App.ViewModels.Settings.QuickPanel;

/// <summary>One plugin source offered to a workspace, and whether that workspace takes it.</summary>
/// <remarks>
/// Deliberately not the same thing as the plugin page's own enable/disable, which governs whether the
/// provider is loaded at all. This is "does THIS workspace include it", the same question adding a
/// folder answers, and a source can be in one workspace and not another.
/// </remarks>
public sealed class QuickPanelPluginSourceOption : ViewModelBase
{
    private readonly Action<string, bool> _toggle;

    public QuickPanelPluginSourceOption(string id, string name, bool isIncluded, Action<string, bool> toggle)
    {
        Id = id;
        Name = name;
        _isIncluded = isIncluded;
        _toggle = toggle;
    }

    public string Id { get; }

    public string Name { get; }

    private bool _isIncluded;

    public bool IsIncluded
    {
        get => _isIncluded;
        set
        {
            if (!SetProperty(ref _isIncluded, value)) return;
            _toggle(Id, value);
        }
    }
}
