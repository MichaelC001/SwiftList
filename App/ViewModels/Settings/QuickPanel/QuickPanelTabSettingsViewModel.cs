using System.Collections.ObjectModel;
using System.Windows.Input;

using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.Core;
namespace SwiftList.App.ViewModels.Settings.QuickPanel;

/// <summary>
/// One workspace being edited: its name and its source list, in the order the panel will show them.
/// The list is the single place every source appears -- folders the user added and the two built-in
/// ones alike -- so reordering, hiding and renaming are one gesture each rather than three lists to
/// keep in sync.
/// </summary>
public class QuickPanelTabSettingsViewModel : ViewModelBase
{
    private readonly QuickPanelTab _model;

    public QuickPanelTabSettingsViewModel(QuickPanelTab model)
    {
        _model = model;
        _name = model.Name;
        _enabled = model.Enabled;
        Processes = new ProcessBlacklistEditorViewModel(model.Processes);

        foreach (var id in QuickPanelGroupOrdering.Resolve(AvailableIds(model), model.GroupOrder, disabled: null))
            Sources.Add(BuildRow(model, id));

        AddFolderCommand = new RelayCommand(AddFolder);
        RemoveSourceCommand = new RelayCommand<QuickPanelSourceRowViewModel>(RemoveSource);
        MoveUpCommand = new RelayCommand<QuickPanelSourceRowViewModel>(row => Move(row, -1));
        MoveDownCommand = new RelayCommand<QuickPanelSourceRowViewModel>(row => Move(row, +1));
    }

    public string Id => _model.Id;

    private string _name;

    /// <summary>Empty falls back to a translated name, so an untouched tab follows the UI language.</summary>
    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
                OnPropertyChanged(nameof(EffectiveName));
        }
    }

    public string EffectiveName => string.IsNullOrWhiteSpace(Name)
        ? TranslationManager.Instance["QuickPanel_DefaultTabName"]
        : Name.Trim();

    private bool _enabled;

    /// <summary>Off keeps the workspace configured but gives it no tab in the panel.</summary>
    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public ICommand MoveUpSelfCommand { get; private set; } = null!;
    public ICommand MoveDownSelfCommand { get; private set; } = null!;
    public ICommand RemoveSelfCommand { get; private set; } = null!;

    /// <summary>
    /// Lets a workspace row carry its own reorder/delete buttons, the way the plugin array editor's
    /// master list does, instead of a toolbar under the list acting on whatever is selected. Wired by
    /// the page, which owns the list these operate on.
    /// </summary>
    internal void BindRowCommands(ICommand moveUp, ICommand moveDown, ICommand remove)
    {
        MoveUpSelfCommand = moveUp;
        MoveDownSelfCommand = moveDown;
        RemoveSelfCommand = remove;
    }

    /// <summary>
    /// The apps this workspace belongs to. The same editor the hotkey blacklist uses -- it is the same
    /// job (a list of process names), so it gets the same type-and-add list rather than a bare box.
    /// </summary>
    public ProcessBlacklistEditorViewModel Processes { get; }

    public ObservableCollection<QuickPanelSourceRowViewModel> Sources { get; } = new();

    public ICommand AddFolderCommand { get; }

    public ICommand RemoveSourceCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

    private bool HasSource(string id) => Sources.Any(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Stages everything back into the settings object this was built from.</summary>
    public void Save()
    {
        _model.Name = Name.Trim();
        _model.Enabled = Enabled;
        _model.Processes = Processes.ToSettingsList();

        foreach (var row in Sources)
            row.SaveFolderFields();

        // Rebuilt from the rows rather than patched: the list IS the order, and a source removed here
        // has to leave the list it came from too. Both lists are rebuilt from the same rows, which is
        // what keeps a folder and a plugin source in one order rather than two.
        _model.Folders = Sources.Where(r => r.IsFolderSource)
            .Select(r => _model.Folders.First(f => f.Id == r.Id)).ToList();
        _model.PluginSourceIds = Sources.Where(r => !r.IsFolderSource).Select(r => r.Id).ToList();
        _model.GroupOrder = Sources.Select(r => r.Id).ToList();
        _model.DisabledGroupIds = Sources.Where(r => !r.IsVisible).Select(r => r.Id).ToList();

        // Only what the user actually overrode: a row left at its defaults gets no entry at all. What is
        // left of a preference (sort, expanded) is the panel's own to write, so an entry that already
        // exists is patched rather than replaced.
        foreach (var row in Sources)
        {
            var custom = row.DisplayName.Trim();
            if (!_model.GroupPreferences.TryGetValue(row.Id, out var preference))
            {
                if (custom.Length == 0 && !row.ShowAsList)
                    continue;
                _model.GroupPreferences[row.Id] = preference = new QuickPanelGroupPreference();
            }
            preference.DisplayName = custom;
            preference.ThumbnailView = !row.ShowAsList;
        }

        // A source deleted here leaves nothing behind to accumulate in the settings file.
        var live = new HashSet<string>(Sources.Select(r => r.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var stale in _model.GroupPreferences.Keys.Where(k => !live.Contains(k)).ToList())
            _model.GroupPreferences.Remove(stale);
    }

    private void AddFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Multiselect = true };
        if (dialog.ShowDialog() != true)
            return;

        AddFolders(dialog.FolderNames);
    }

    /// <summary>Adds folders as sources, skipping any this workspace already has.</summary>
    /// <remarks>
    /// A folder someone has just gone and picked starts as "everything, by name": they chose that folder
    /// because of what is in it, and a shortcut bar is what a folder added by hand nearly always is.
    /// Recently-changed-files is the specialised answer -- worth having, and one dropdown away, but a
    /// poor thing to assume, since it can leave a folder full of files showing nothing at all when
    /// none of them has been touched lately.
    ///
    /// The workspace a fresh install starts with is not this: Desktop, Downloads and Documents are
    /// recent-files there deliberately, being places things arrive rather than places things are kept.
    /// </remarks>
    internal void AddFolders(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Sources.Any(r => r.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                continue;
            var folder = QuickPanelFolderSource.For(path, QuickPanelSourceKind.Launcher);
            _model.Folders.Add(folder);
            Sources.Add(QuickPanelSourceRowViewModel.ForFolder(folder));
        }
    }

    private void RemoveSource(QuickPanelSourceRowViewModel? row)
    {
        if (row == null)
            return;
        Sources.Remove(row);
        _model.Folders.RemoveAll(f => f.Id == row.Id);
        _model.PluginSourceIds.RemoveAll(id => id.Equals(row.Id, StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(PluginSources));
    }

    /// <summary>
    /// Every plugin source that could be added, each saying whether this workspace already includes it.
    /// </summary>
    /// <remarks>
    /// Rebuilt on demand rather than held: plugins can be enabled or disabled on their own page while
    /// this one is open, and a stale list would offer a source that is no longer there or hide one that
    /// just appeared.
    /// </remarks>
    public IReadOnlyList<QuickPanelPluginSourceOption> PluginSources => QuickPanelPluginSourceCatalog
        .Available()
        .Select(entry => new QuickPanelPluginSourceOption(entry.Id, entry.Name, IsIncluded(entry.Id), Toggle))
        .ToList();

    public bool HasPluginSources => PluginSources.Count > 0;

    private bool IsIncluded(string componentId)
        => Sources.Any(row => row.Id.Equals(componentId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds a plugin source to this workspace, or takes it out again.</summary>
    /// <remarks>
    /// Added at the end of the source list, which is where a newly added folder lands too: a source the
    /// user has just chosen has no position yet, and putting it anywhere other than last would be the
    /// page deciding one for them.
    /// </remarks>
    private void Toggle(string componentId, bool include)
    {
        var existing = Sources.FirstOrDefault(row => row.Id.Equals(componentId, StringComparison.OrdinalIgnoreCase));
        if (include == (existing != null)) return;

        if (include)
            Sources.Add(QuickPanelSourceRowViewModel.ForPlugin(componentId, QuickPanelPluginSourceCatalog.NameOf(componentId)));
        else
            RemoveSource(existing);

        OnPropertyChanged(nameof(PluginSources));
    }

    private void Move(QuickPanelSourceRowViewModel? row, int delta)
    {
        if (row == null)
            return;
        var from = Sources.IndexOf(row);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= Sources.Count)
            return;
        Sources.Move(from, to);
    }

    // Folders the user picked, plus whichever plugin sources this workspace includes. One id space, one
    // order: the list on screen arranges both together, and every per-group preference is keyed the same
    // way for either.
    private static IEnumerable<string> AvailableIds(QuickPanelTab model)
        => model.Folders.Select(f => f.Id).Concat(model.PluginSourceIds);

    private static QuickPanelSourceRowViewModel BuildRow(QuickPanelTab model, string id)
    {
        var folder = model.Folders.FirstOrDefault(f => f.Id == id);
        var row = folder != null
            ? QuickPanelSourceRowViewModel.ForFolder(folder)
            : QuickPanelSourceRowViewModel.ForPlugin(id, QuickPanelPluginSourceCatalog.NameOf(id));
        row.IsVisible = !model.DisabledGroupIds.Contains(id, StringComparer.OrdinalIgnoreCase);
        if (model.GroupPreferences.TryGetValue(id, out var preference))
        {
            row.DisplayName = preference.DisplayName;
            row.ShowAsList = !preference.ThumbnailView;
        }
        return row;
    }
}
