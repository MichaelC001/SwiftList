using System.Collections.ObjectModel;
using System.Windows.Input;

using SwiftList.App.Helpers;
using SwiftList.Core;
namespace SwiftList.App.ViewModels.Settings.QuickPanel;

/// <summary>
/// Backs the Quick Panel settings page: the workspace tabs down the left, the selected one's sources on
/// the right. Edits stage on these view models and only reach <see cref="UserSettings"/> when
/// <see cref="Save"/> runs, the same way every other settings page works.
/// </summary>
/// <remarks>
/// The tabs edited here are the panel's workspaces -- one set of sources per project -- and have
/// nothing to do with the Startup Panel's tab strip, where a tab is one content source. Same word, two
/// concepts; they must not be wired together.
/// </remarks>
public class QuickPanelSettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;
    private readonly List<QuickPanelTab> _models;

    public QuickPanelSettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;
        var panel = userSettings.QuickPanel;
        _enabled = panel.Enabled;

        // A settings file edited by hand (or one from before this page existed) can have no tabs at all,
        // and a panel with nowhere to put a source is not something the user can recover from here.
        _models = panel.Tabs.Count > 0 ? panel.Tabs.ToList() : new List<QuickPanelTab> { QuickPanelTab.CreateDefault() };
        foreach (var model in _models)
            Tabs.Add(new QuickPanelTabSettingsViewModel(model));

        _selectedTab = Tabs.FirstOrDefault(t => t.Id == panel.ActiveTabId) ?? Tabs.FirstOrDefault();

        AddTabCommand = new RelayCommand(AddTab);
        DuplicateTabCommand = new RelayCommand(DuplicateTab, () => SelectedTab != null);
        RemoveTabCommand = new RelayCommand(RemoveTab, () => Tabs.Count > 1 && SelectedTab != null);
        MoveTabUpCommand = new RelayCommand(() => MoveTab(-1), () => CanMoveTab(-1));
        MoveTabDownCommand = new RelayCommand(() => MoveTab(+1), () => CanMoveTab(+1));
    }

    private bool _enabled;

    /// <summary>Master switch: off and the hotkey does nothing at all.</summary>
    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public ObservableCollection<QuickPanelTabSettingsViewModel> Tabs { get; } = new();

    private QuickPanelTabSettingsViewModel? _selectedTab;
    public QuickPanelTabSettingsViewModel? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (SetProperty(ref _selectedTab, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>The tab strip only earns its space once there is more than one workspace.</summary>
    public bool HasMultipleTabs => Tabs.Count > 1;

    public ICommand AddTabCommand { get; }
    public ICommand DuplicateTabCommand { get; }
    public ICommand RemoveTabCommand { get; }
    public ICommand MoveTabUpCommand { get; }
    public ICommand MoveTabDownCommand { get; }

    public void Save()
    {
        foreach (var tab in Tabs)
            tab.Save();

        var panel = _userSettings.QuickPanel;
        panel.Enabled = Enabled;
        // Ordered by the strip, not by _models: the list on screen is what the user arranged.
        panel.Tabs = Tabs.Select(t => _models.First(m => m.Id == t.Id)).ToList();
        panel.ActiveTabId = SelectedTab?.Id ?? panel.Tabs.FirstOrDefault()?.Id ?? string.Empty;
    }

    private void AddTab()
    {
        var model = new QuickPanelTab { Id = QuickPanelTab.NewId() };
        _models.Add(model);
        var tab = new QuickPanelTabSettingsViewModel(model);
        Tabs.Add(tab);
        SelectedTab = tab;
        OnPropertyChanged(nameof(HasMultipleTabs));
    }

    // Copies the sources, not the panel's own per-group state: a workspace forked from another starts
    // with the same folders but is free to sort and collapse them differently.
    private void DuplicateTab()
    {
        if (SelectedTab == null)
            return;
        var source = _models.First(m => m.Id == SelectedTab.Id);
        SelectedTab.Save();

        var copy = new QuickPanelTab
        {
            Id = QuickPanelTab.NewId(),
            Name = SelectedTab.EffectiveName,
            DisabledGroupIds = source.DisabledGroupIds.ToList(),
        };
        var idMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in source.Folders)
        {
            var clone = QuickPanelFolderSource.For(folder.Path, folder.Kind);
            clone.Recursive = folder.Recursive;
            clone.FilterPattern = folder.FilterPattern;
            clone.MaxItems = folder.MaxItems;
            clone.MaxAgeMinutes = folder.MaxAgeMinutes;
            idMap[folder.Id] = clone.Id;
            copy.Folders.Add(clone);
        }
        // Order and hidden-list entries point at ids, which the clones do not share.
        copy.GroupOrder = source.GroupOrder.Select(id => idMap.TryGetValue(id, out var mapped) ? mapped : id).ToList();
        copy.DisabledGroupIds = copy.DisabledGroupIds.Select(id => idMap.TryGetValue(id, out var mapped) ? mapped : id).ToList();
        foreach (var (id, preference) in source.GroupPreferences)
        {
            copy.GroupPreferences[idMap.TryGetValue(id, out var mapped) ? mapped : id] = new QuickPanelGroupPreference
            {
                DisplayName = preference.DisplayName,
                Sort = preference.Sort,
                ThumbnailView = preference.ThumbnailView,
                Expanded = preference.Expanded,
            };
        }

        _models.Add(copy);
        var tab = new QuickPanelTabSettingsViewModel(copy);
        Tabs.Add(tab);
        SelectedTab = tab;
        OnPropertyChanged(nameof(HasMultipleTabs));
    }

    private void RemoveTab()
    {
        if (SelectedTab == null || Tabs.Count <= 1)
            return;
        var index = Tabs.IndexOf(SelectedTab);
        _models.RemoveAll(m => m.Id == SelectedTab.Id);
        Tabs.Remove(SelectedTab);
        SelectedTab = Tabs[Math.Min(index, Tabs.Count - 1)];
        OnPropertyChanged(nameof(HasMultipleTabs));
    }

    private bool CanMoveTab(int delta)
    {
        if (SelectedTab == null)
            return false;
        var to = Tabs.IndexOf(SelectedTab) + delta;
        return to >= 0 && to < Tabs.Count;
    }

    private void MoveTab(int delta)
    {
        if (SelectedTab == null || !CanMoveTab(delta))
            return;
        var from = Tabs.IndexOf(SelectedTab);
        Tabs.Move(from, from + delta);
    }
}
