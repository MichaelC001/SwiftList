using System.Collections.ObjectModel;
using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.App.Services;

namespace SwiftList.App.ViewModels.Settings.Plugins;

/// <summary>
/// Represents the strongly-typed categories of plugin components.
/// </summary>
public enum PluginComponentType
{
    Action,
    DynamicActionProvider,
    InstantProvider,
    SearchableItemProvider,
    FilterProvider,
    ColumnProvider,
    AliasProvider,
    ActivePathCollector,
    FileDialogAdapter,
    InlineSearchAdapter,
    FilePreviewProvider,
    QuickNavigationProvider,
    ThumbnailProvider,
    QueryTokenProvider,
    StartupPanelTabProvider,
    /// <summary>Translation providers are displayed read-only; they cannot be disabled.</summary>
    TranslationProvider,
    /// <summary>Theme providers are displayed read-only; they cannot be disabled.</summary>
    ThemeProvider
}

/// <summary>
/// Represents a group of plugin components of the same type.
/// </summary>
public class PluginComponentGroupViewModel : ViewModelBase
{
    public PluginComponentGroupViewModel(PluginComponentType componentType, List<PluginComponentViewModel> components)
    {
        ComponentType = componentType;
        Components = new ObservableCollection<PluginComponentViewModel>(components);
        ToggleAllCommand = new RelayCommand(ToggleAllComponents);

        // TranslationProvider/ThemeProvider components have no checkbox at all (see IsToggleable),
        // so there's nothing for a select-all button to toggle in those groups.
        foreach (var component in Components.Where(c => c.IsToggleable))
            component.PropertyChanged += OnComponentIsEnabledChanged;
    }

    public PluginComponentType ComponentType { get; }
    public string GroupName => TranslationManager.Instance[$"Plugins_Type{ComponentType}"];
    public ObservableCollection<PluginComponentViewModel> Components { get; }

    // A single toggleable component has nothing to "select all" -- its own checkbox already does that.
    public bool HasToggleableComponents => Components.Count(c => c.IsToggleable) > 1;
    public bool AreAllToggleableComponentsEnabled => Components.Where(c => c.IsToggleable).All(c => c.IsEnabled);
    public string SelectAllToggleLabel => TranslationManager.Instance[AreAllToggleableComponentsEnabled ? "Common_DeselectAll" : "Common_SelectAll"];

    public ICommand ToggleAllCommand { get; }

    private void OnComponentIsEnabledChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PluginComponentViewModel.IsEnabled))
            OnPropertyChanged(nameof(SelectAllToggleLabel));
    }

    private void ToggleAllComponents()
    {
        var setTo = !AreAllToggleableComponentsEnabled;
        foreach (var component in Components.Where(c => c.IsToggleable))
            component.IsEnabled = setTo;
    }
}

/// <summary>
/// Represents a loaded plugin with its name, version, source DLL, and grouped sub-components.
/// </summary>
public class PluginInfoViewModel : ViewModelBase
{
    private bool _isExpanded = true;

    public PluginInfoViewModel(
        string name,
        string version,
        string dllFileName,
        string sdkVersion,
        List<PluginComponentViewModel> components,
        List<PluginConfigFieldViewModel> configFields,
        string description = "")
    {
        Name = name;
        Version = version;
        DllFileName = dllFileName;
        SdkVersion = sdkVersion;
        RawComponents = components;
        ConfigFields = new ObservableCollection<PluginConfigFieldViewModel>(configFields);
        Description = description;
        ToggleAllComponentsCommand = new RelayCommand(ToggleAllComponents);

        // Group components by type
        var groups = components
            .GroupBy(c => c.ComponentType)
            .OrderBy(g => g.Key)
            .Select(g => new PluginComponentGroupViewModel(g.Key, g.ToList()))
            .ToList();

        ComponentGroups = new ObservableCollection<PluginComponentGroupViewModel>(groups);

        // TranslationProvider/ThemeProvider components have no checkbox at all (see IsToggleable),
        // so there's nothing for the plugin-wide select-all button to toggle for those.
        foreach (var component in RawComponents.Where(c => c.IsToggleable))
            component.PropertyChanged += OnComponentIsEnabledChanged;
    }

    public string Name { get; }
    public string Description { get; }
    public string Version { get; }
    public string DllFileName { get; }
    public string SdkVersion { get; }
    public List<PluginComponentViewModel> RawComponents { get; }
    public ObservableCollection<PluginComponentGroupViewModel> ComponentGroups { get; }
    public ObservableCollection<PluginConfigFieldViewModel> ConfigFields { get; }

    public bool HasConfigFields => ConfigFields.Count > 0;
    public bool HasNoComponents => RawComponents.Count == 0;

    // Plugin-wide select-all/deselect-all, toggling every component across every group at once --
    // separate from each PluginComponentGroupViewModel's own per-group toggle. Same single-item
    // exception as the per-group button.
    public bool HasToggleableComponents => RawComponents.Count(c => c.IsToggleable) > 1;
    public bool AreAllToggleableComponentsEnabled => RawComponents.Where(c => c.IsToggleable).All(c => c.IsEnabled);
    public string SelectAllToggleLabel => TranslationManager.Instance[AreAllToggleableComponentsEnabled ? "Common_DeselectAll" : "Common_SelectAll"];

    public ICommand ToggleAllComponentsCommand { get; }

    private void OnComponentIsEnabledChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PluginComponentViewModel.IsEnabled))
            OnPropertyChanged(nameof(SelectAllToggleLabel));
    }

    private void ToggleAllComponents()
    {
        var setTo = !AreAllToggleableComponentsEnabled;
        foreach (var component in RawComponents.Where(c => c.IsToggleable))
            component.IsEnabled = setTo;
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    private bool _isConfigOpen;

    /// <summary>
    /// Whether this plugin's config fields are showing inside its card.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="IsExpanded"/>, which shows the component list: a plugin can have
    /// components worth toggling and no config at all, or the reverse, and collapsing one should not
    /// take the other with it. Both start closed, so a card opens showing the same summary it always did.
    /// </remarks>
    public bool IsConfigOpen
    {
        get => _isConfigOpen;
        set => SetProperty(ref _isConfigOpen, value);
    }

    // A plugin schema with 2+ top-level Group fields renders them as tabs (like the Hotkeys page)
    // instead of stacking every group's contents vertically down the page. A single group, or none,
    // isn't worth a tab bar, so those still render inline via ConfigFields as before.
    public bool HasMultipleConfigGroups => ConfigFields.Count(f => f.IsGroup) > 1;
    public List<PluginConfigFieldViewModel> ConfigGroups => ConfigFields.Where(f => f.IsGroup).ToList();
    public List<PluginConfigFieldViewModel> NonGroupConfigFields => ConfigFields.Where(f => !f.IsGroup).ToList();

    private PluginConfigFieldViewModel? _selectedConfigGroup;
    public PluginConfigFieldViewModel? SelectedConfigGroup
    {
        get => _selectedConfigGroup ??= ConfigGroups.FirstOrDefault();
        set => SetProperty(ref _selectedConfigGroup, value);
    }

    private ICommand? _selectConfigGroupCommand;
    public ICommand SelectConfigGroupCommand => _selectConfigGroupCommand ??= new RelayCommand<PluginConfigFieldViewModel>(g => SelectedConfigGroup = g);
}

/// <summary>
/// Represents a single sub-component of a plugin (action, provider, etc.) that can be enabled/disabled.
/// </summary>
public class PluginComponentViewModel : ViewModelBase
{
    private bool _isEnabled;

    public PluginComponentViewModel(string componentId, PluginComponentType componentType, string displayName, bool isEnabled, string description = "")
    {
        ComponentId = componentId;
        ComponentType = componentType;
        DisplayName = displayName;
        _isEnabled = isEnabled;
        Description = description;
    }

    /// <summary>The stable unique ID used to persist the disabled state.</summary>
    public string ComponentId { get; }

    /// <summary>The category/type of this component (strongly-typed enum).</summary>
    public PluginComponentType ComponentType { get; }

    public string DisplayName { get; }

    public string Description { get; }

    /// <summary>
    /// Whether the user can toggle this component on/off.
    /// TranslationProvider and ThemeProvider components are shown read-only and cannot be disabled.
    /// </summary>
    public bool IsToggleable => ComponentType != PluginComponentType.TranslationProvider && ComponentType != PluginComponentType.ThemeProvider;

    /// <summary>Set once the user actually flips this checkbox. Lets Save() apply only components the
    /// user touched in this page, instead of blindly re-asserting this snapshot's IsEnabled for every
    /// component -- which would clobber changes made through other channels (e.g. closing a Startup
    /// Panel tab's x button, or the Startup Panel settings page's own re-enable checkbox) in the same
    /// Settings window session.</summary>
    public bool IsDirty { get; private set; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
                IsDirty = true;
        }
    }
}
