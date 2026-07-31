using System.Collections.ObjectModel;
using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.Core;
using SwiftList.Core.Wire;
using SwiftList.PluginSdk.Abstractions.Plugins;

namespace SwiftList.App.ViewModels.Settings.Plugins;

/// <summary>
/// ViewModel for the Plugin Management settings page.
/// Loads installed plugins and exposes their sub-components with enable/disable toggles.
/// </summary>
public class PluginManagementViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;

    public PluginManagementViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;
        Plugins = new ObservableCollection<PluginInfoViewModel>(PluginLoaderHelper.BuildPluginList(_userSettings));
        foreach (var p in Plugins)
            p.PropertyChanged += OnPluginIsExpandedChanged;
        ToggleExpandCommand = new RelayCommand<PluginInfoViewModel>(p => p?.IsExpanded = !p.IsExpanded);
        ToggleExpandAllCommand = new RelayCommand(ToggleExpandAll);
        ToggleConfigCommand = new RelayCommand<PluginInfoViewModel>(ToggleConfig);
        SaveConfigCommand = new RelayCommand<PluginInfoViewModel>(SaveConfig);

        // Dynamically refresh the plugin list when language changes to dynamically apply localized plugin names
        TranslationManager.Instance.PropertyChanged += (s, e) =>
        {
            var expandedStates = Plugins.ToDictionary(p => p.DllFileName, p => p.IsExpanded);
            foreach (var p in Plugins)
                p.PropertyChanged -= OnPluginIsExpandedChanged;
            var newList = PluginLoaderHelper.BuildPluginList(_userSettings);
            Plugins.Clear();
            foreach (var p in newList)
            {
                if (expandedStates.TryGetValue(p.DllFileName, out var isExpanded))
                {
                    p.IsExpanded = isExpanded;
                }
                p.PropertyChanged += OnPluginIsExpandedChanged;
                Plugins.Add(p);
            }
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(ExpandAllToggleLabel));
            OnPropertyChanged(nameof(DevGuideUri));
        };
    }

    public ObservableCollection<PluginInfoViewModel> Plugins { get; }

    public ICommand ToggleExpandCommand { get; }
    public ICommand ToggleExpandAllCommand { get; }

    /// <summary>Shows or hides a plugin's config fields inside its own card.</summary>
    public ICommand ToggleConfigCommand { get; }

    /// <summary>The card's OK button: writes the config fields the user edited.</summary>
    public ICommand SaveConfigCommand { get; }

    // Opening the config used to mean a modal window, and closing that window without OK rolled the
    // fields back. Inline there is no close, so hiding the section takes its place -- otherwise edits
    // abandoned by collapsing would sit in the view models, and the next card to open would show them as
    // if they had been saved.
    private void ToggleConfig(PluginInfoViewModel? plugin)
    {
        if (plugin == null) return;

        if (plugin.IsConfigOpen)
        {
            RollbackConfig(plugin);
            plugin.IsConfigOpen = false;
            return;
        }

        plugin.IsConfigOpen = true;
    }

    internal static void RollbackConfig(PluginInfoViewModel plugin)
    {
        foreach (var field in plugin.ConfigFields)
            field.Reload();
    }

    // Same three steps the modal's OK did: commit every field, persist once through the settings object
    // they share, and tell the hook process to re-read what just changed.
    private void SaveConfig(PluginInfoViewModel? plugin)
    {
        if (plugin == null || plugin.ConfigFields.Count == 0) return;

        foreach (var field in plugin.ConfigFields)
            field.Commit();

        plugin.ConfigFields[0].Settings.Save();
        App.HookClient?.SendMessage(new IpcMessage { Id = IpcMessageId.ReloadSettings });
        plugin.IsConfigOpen = false;
    }

    public bool IsEmpty => Plugins.Count == 0;

    public string HostSdkVersion { get; } = typeof(IPlugin).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    // Header-level "expand all / collapse all" for every plugin card at once, mirroring each card's
    // own "select all / deselect all" toggle for its components (PluginInfoViewModel.SelectAllToggleLabel).
    public bool AreAllExpanded => Plugins.Count > 0 && Plugins.All(p => p.IsExpanded);
    public string ExpandAllToggleLabel => TranslationManager.Instance[AreAllExpanded ? "Common_CollapseAll" : "Common_ExpandAll"];

    private void OnPluginIsExpandedChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PluginInfoViewModel.IsExpanded))
            OnPropertyChanged(nameof(ExpandAllToggleLabel));
    }

    private void ToggleExpandAll()
    {
        var expand = !AreAllExpanded;
        foreach (var p in Plugins)
            p.IsExpanded = expand;
    }

    // The dev guide link's target differs per locale (/zh-CN/ prefix), so it's derived from the
    // same translated URL string the link displays -- see AboutSettingsPage.UserGuideUri for the
    // same pattern applied to the user manual link.
    public Uri? DevGuideUri => Uri.TryCreate(TranslationManager.Instance["Plugins_DevGuideUrl"], UriKind.Absolute, out var uri) ? uri : null;

    public void Save()
    {
        // Apply only the components the user actually toggled on this page (IsDirty), merged into the
        // CURRENT disabled list rather than replacing it wholesale -- Plugins is a snapshot taken when
        // the Settings window opened, so a blind replace would silently revert any component disabled
        // or re-enabled through another channel since then (e.g. a Startup Panel tab's x button, or the
        // Startup Panel settings page's own re-enable checkbox).
        var disabled = new HashSet<string>(_userSettings.DisabledPluginComponents, StringComparer.OrdinalIgnoreCase);

        foreach (var c in Plugins.SelectMany(p => p.RawComponents).Where(c => c.IsToggleable && c.IsDirty))
        {
            if (c.IsEnabled)
                disabled.Remove(c.ComponentId);
            else
                disabled.Add(c.ComponentId);
        }

        _userSettings.DisabledPluginComponents = disabled.ToList();
    }
}
