using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.App.Services;

namespace SwiftList.App.ViewModels.Search.StartupPanel;

// One entry in the startup panel's tab strip (shown above the quick window's results when the search
// box is empty and at least one tab has content). SelectCommand switches the panel's Results to this
// tab's items; CloseCommand disables the underlying source and asks the controller to drop this tab.
public class StartupPanelTabViewModel : ViewModelBase
{
    public string Label { get; }

    /// <summary>The source's own stable id, which is what StartupPanel.TabOrder is written in.</summary>
    /// <remarks>
    /// Carried on the tab rather than looked up from the controller's parallel list: dragging reorders
    /// the tab view models in place, so the strip is the only thing that knows the new order and it has
    /// to be able to name what it holds.
    /// </remarks>
    public string Id { get; }

    public ICommand CloseCommand { get; }
    public ICommand SelectCommand { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    // Base (design) sizes at the default search bar height -- scaled by the same UiMetrics.Scale
    // factor the quick window's results rows/fonts already use, so the tab strip grows/shrinks along
    // with them instead of staying visually fixed while everything below it resizes.
    private const double BaseFontSize = 14;
    private const double BaseCloseButtonSize = 16;
    private const double BaseCloseIconFontSize = 9;
    private const double BaseUnderlineHeight = 2;
    private const double BaseBottomPadding = 10;
    private const double BaseCloseButtonBottomMargin = 8;

    public double ScaledFontSize => Math.Round(BaseFontSize * UiMetrics.Scale);
    public double ScaledCloseButtonSize => Math.Round(BaseCloseButtonSize * UiMetrics.Scale);
    public double ScaledCloseIconFontSize => Math.Round(BaseCloseIconFontSize * UiMetrics.Scale);
    public double ScaledUnderlineHeight => Math.Max(1.0, Math.Round(BaseUnderlineHeight * UiMetrics.Scale));

    // Thickness properties (rather than plain doubles) since these bind directly to WPF Padding/Margin
    // setters -- WPF's ThicknessConverter parses a comma-separated string like any other markup value,
    // so exposing the ready-made string sidesteps needing a generic double-to-Thickness IValueConverter
    // for what only this one template actually needs.
    public string ScaledBottomPaddingThickness => $"0,0,0,{Math.Round(BaseBottomPadding * UiMetrics.Scale)}";
    public string ScaledCloseButtonMarginThickness => $"8,0,0,{Math.Round(BaseCloseButtonBottomMargin * UiMetrics.Scale)}";
    public string ScaledUnderlineMarginThickness => $"0,{-ScaledUnderlineHeight},0,0";

    public StartupPanelTabViewModel(string label, Action onClose, Action onSelect, string id = "")
    {
        Label = label;
        Id = id;
        CloseCommand = new RelayCommand(onClose);
        SelectCommand = new RelayCommand(onSelect);
    }

    // Tab instances persist across searches (unlike AppSearchResult rows, which get rebuilt fresh on
    // every search and so implicitly pick up UiMetrics.Scale's current value) -- called whenever
    // QuickSearchViewModel.RefreshLayoutSettings() re-applies scale from settings, so an already-bound
    // tab's Scaled* properties actually refresh instead of staying frozen at whatever they read on
    // first bind. Empty property name means "every property on this object changed" -- simpler and
    // safer against drift than re-listing each Scaled* property name here individually.
    public void RefreshScale() => OnPropertyChanged(string.Empty);
}
