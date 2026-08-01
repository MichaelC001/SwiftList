using SwiftList.App.Services;
using SwiftList.Core;
namespace SwiftList.App.ViewModels.Settings.QuickPanel;

/// <summary>One entry of the kind dropdown: the value that is stored, and the label that is shown.</summary>
public sealed record QuickPanelSourceKindOption(QuickPanelSourceKind Value, string Label);

/// <summary>
/// One row of a tab's source list, which is also one group in the panel. Folder sources and the
/// built-in ones share this type on purpose: everything the list offers -- rename, show/hide, reorder --
/// applies to both, and the settings store them one way too. Only the fields a built-in source has no
/// answer for (path, kind, and the rest of the advanced block) are hidden for it, via
/// <see cref="IsFolderSource"/>.
/// </summary>
public class QuickPanelSourceRowViewModel : ViewModelBase
{
    private readonly QuickPanelFolderSource? _folder;

    private QuickPanelSourceRowViewModel(string id, string defaultName, QuickPanelFolderSource? folder)
    {
        Id = id;
        DefaultName = defaultName;
        _folder = folder;
    }

    public static QuickPanelSourceRowViewModel ForFolder(QuickPanelFolderSource folder)
        => new(folder.Id, DefaultNameForPath(folder.Path), folder)
        {
            _path = folder.Path,
            _kind = folder.Kind,
            _recursive = folder.Recursive,
            _filterPattern = folder.FilterPattern,
            _maxItems = folder.MaxItems,
            _maxAgeMinutes = folder.MaxAgeMinutes,
        };

    public static QuickPanelSourceRowViewModel ForBuiltIn(string id, string translationKey)
        => new(id, TranslationManager.Instance[translationKey], null);

    public string Id { get; }

    /// <summary>False for the built-in sources: no path to edit, no kind to choose, nothing to delete.</summary>
    public bool IsFolderSource => _folder != null;

    /// <summary>What the group is called when the user has not renamed it.</summary>
    public string DefaultName { get; }

    private string _displayName = string.Empty;

    /// <summary>The user's own name for this group. Empty means fall back to <see cref="DefaultName"/>.</summary>
    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (SetProperty(ref _displayName, value))
                OnPropertyChanged(nameof(EffectiveName));
        }
    }

    /// <summary>What the row heading shows, and what the panel will show.</summary>
    public string EffectiveName => string.IsNullOrWhiteSpace(DisplayName) ? DefaultName : DisplayName.Trim();

    private bool _isVisible = true;

    /// <summary>Unchecked hides the group from the panel without deleting the source.</summary>
    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    private bool _isExpanded;

    /// <summary>Whether this row's advanced block is open. Purely a settings-page state, never stored.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>
    /// Opens and closes the block above. A command driving a plain Button rather than a ToggleButton
    /// bound to <see cref="IsExpanded"/>: a ToggleButton's checked state paints the system accent
    /// colour straight over whatever style it is given, which in this app's dark settings window is a
    /// bright blue block where an icon should be.
    /// </summary>
    public System.Windows.Input.ICommand ToggleOptionsCommand => _toggleOptions ??= new Helpers.RelayCommand(() => IsExpanded = !IsExpanded);

    private System.Windows.Input.ICommand? _toggleOptions;

    private string _path = string.Empty;
    public string Path
    {
        get => _path;
        set
        {
            if (SetProperty(ref _path, value))
                OnPropertyChanged(nameof(EffectiveName));
        }
    }

    /// <summary>
    /// What the kind dropdown offers, as value+label pairs rather than items with translated Content
    /// and the enum in Tag. That is how every other dropdown in Settings is built, for two reasons this
    /// one hit both of: the label is only ever a label, so switching language rebuilds the list without
    /// touching the selection, and the selection matches on the value rather than on identity.
    /// </summary>
    public IReadOnlyList<QuickPanelSourceKindOption> KindOptions => _kindOptions ??= BuildKindOptions();

    private IReadOnlyList<QuickPanelSourceKindOption>? _kindOptions;

    private static IReadOnlyList<QuickPanelSourceKindOption> BuildKindOptions() => new[]
    {
        new QuickPanelSourceKindOption(QuickPanelSourceKind.RecentFiles, TranslationManager.Instance["QuickPanel_KindRecentFiles"]),
        new QuickPanelSourceKindOption(QuickPanelSourceKind.AllByModified, TranslationManager.Instance["QuickPanel_KindAllByModified"]),
        new QuickPanelSourceKindOption(QuickPanelSourceKind.Launcher, TranslationManager.Instance["QuickPanel_KindLauncher"]),
    };

    /// <summary>Rebuilds the labels after a language switch, keeping the selected value.</summary>
    public void RefreshTranslations()
    {
        _kindOptions = null;
        OnPropertyChanged(nameof(KindOptions));
        OnPropertyChanged(nameof(Kind));
    }

    private QuickPanelSourceKind _kind = QuickPanelSourceKind.RecentFiles;
    public QuickPanelSourceKind Kind
    {
        get => _kind;
        set
        {
            if (SetProperty(ref _kind, value))
                OnPropertyChanged(nameof(IsRecentFiles));
        }
    }

    /// <summary>The age limit only means anything for a recent-files source, so the field hides otherwise.</summary>
    public bool IsRecentFiles => Kind == QuickPanelSourceKind.RecentFiles;

    private bool _recursive;
    public bool Recursive
    {
        get => _recursive;
        set => SetProperty(ref _recursive, value);
    }

    private string _filterPattern = "*";
    public string FilterPattern
    {
        get => _filterPattern;
        set => SetProperty(ref _filterPattern, value);
    }

    private int _maxItems = 20;

    /// <summary>0 means everything the source has; the upper bound keeps one group from filling the panel.</summary>
    public int MaxItems
    {
        get => _maxItems;
        set
        {
            if (value < 0 || value > 200)
                throw new ArgumentOutOfRangeException(nameof(value), "Count must be between 0 and 200.");
            SetProperty(ref _maxItems, value);
        }
    }

    private int _maxAgeMinutes;

    /// <summary>0 means no age limit. Same 30-day ceiling the Startup Panel's own field uses.</summary>
    public int MaxAgeMinutes
    {
        get => _maxAgeMinutes;
        set
        {
            if (value < 0 || value > 43200)
                throw new ArgumentOutOfRangeException(nameof(value), "Time range must be between 0 and 43200 minutes.");
            SetProperty(ref _maxAgeMinutes, value);
        }
    }

    /// <summary>Writes this row's folder fields back into the source it came from. Built-ins have none.</summary>
    public void SaveFolderFields()
    {
        if (_folder == null)
            return;
        _folder.Path = Path.Trim().Trim('"');
        _folder.Kind = Kind;
        _folder.Recursive = Recursive;
        _folder.FilterPattern = string.IsNullOrWhiteSpace(FilterPattern) ? "*" : FilterPattern.Trim();
        _folder.MaxItems = MaxItems;
        _folder.MaxAgeMinutes = MaxAgeMinutes;
    }

    /// <summary>The folder's own name, or the path itself for a drive root, which has no last segment.</summary>
    internal static string DefaultNameForPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        var trimmed = path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        var leaf = System.IO.Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(leaf) ? path : leaf;
    }
}
