using System.Windows.Input;
using SwiftList.App.Helpers;

namespace SwiftList.App.ViewModels.QuickPanel;

/// <summary>One workspace, shown as one entry in the panel's tab strip.</summary>
/// <remarks>
/// Not <see cref="Search.StartupPanel.StartupPanelTabViewModel"/>, which the strip was built against
/// while that panel stood in as a data source. Same word, two concepts: a startup panel tab is one
/// content source, a quick panel tab is a whole workspace of them. The startup panel's version also
/// carries a close button and the quick window's scale factors, neither of which mean anything here.
/// </remarks>
public class QuickPanelTabViewModel : ViewModelBase
{
    public QuickPanelTabViewModel(string id, string label, Action onSelect)
    {
        Id = id;
        Label = label;
        SelectCommand = new RelayCommand(onSelect);
    }

    public string Id { get; }

    public string Label { get; }

    public ICommand SelectCommand { get; }

    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
