using System.Collections.ObjectModel;
using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.Core;

namespace SwiftList.App.ViewModels.Settings;

public class BlacklistSettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;
    private string _newProcessName = string.Empty;
    private string _blacklistText = string.Empty;

    public BlacklistSettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;
        foreach (var proc in _userSettings.BlacklistedProcesses.Where(x => !string.IsNullOrWhiteSpace(x)))
            BlacklistedProcesses.Add(new BlacklistProcessItem(proc));
        _quickPanelBlacklistText = string.Join(Environment.NewLine, _userSettings.QuickPanel.BlacklistedProcesses);

        RefreshBulkText();

        AddProcessCommand = new RelayCommand(AddProcess, CanAddProcess);
        ApplyTextCommand = new RelayCommand(ApplyBulkText);
        ExportTextCommand = new RelayCommand(() => BlacklistText = JoinLines());
        RemoveProcessCommand = new RelayCommand<BlacklistProcessItem>(RemoveProcess);
        EditProcessCommand = new RelayCommand<BlacklistProcessItem>(EditProcess);
    }

    public ObservableCollection<BlacklistProcessItem> BlacklistedProcesses { get; } = new();

    public ICommand AddProcessCommand { get; }
    public ICommand ApplyTextCommand { get; }
    public ICommand ExportTextCommand { get; }
    public ICommand RemoveProcessCommand { get; }
    public ICommand EditProcessCommand { get; }

    public string NewProcessName
    {
        get => _newProcessName;
        set
        {
            if (SetProperty(ref _newProcessName, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public string BlacklistText
    {
        get => _blacklistText;
        set => SetProperty(ref _blacklistText, value);
    }

    private string _quickPanelBlacklistText = string.Empty;

    /// <summary>
    /// The quick panel's own additions to the list above, one process name per line. It lives on this
    /// page rather than the panel's because this is where a user goes to say "not in this app" -- and
    /// the two are read together anyway: the panel refuses to open over anything on either list.
    /// </summary>
    public string QuickPanelBlacklistText
    {
        get => _quickPanelBlacklistText;
        set => SetProperty(ref _quickPanelBlacklistText, value);
    }

    public void Save()
    {
        ApplyBulkText();
        _userSettings.BlacklistedProcesses = NormalizeItems();
        _userSettings.QuickPanel.BlacklistedProcesses = ParseLines(QuickPanelBlacklistText);
        RefreshBulkText();
    }

    private static List<string> ParseLines(string? text) => (text ?? string.Empty)
        .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
        .Select(line => line.Trim().Trim('"'))
        .Where(line => line.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private bool CanAddProcess() => !string.IsNullOrWhiteSpace(NewProcessName);

    private void AddProcess()
    {
        var normalized = NewProcessName.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        if (!BlacklistedProcesses.Any(x => x.Value.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
        {
            BlacklistedProcesses.Add(new BlacklistProcessItem(normalized));
        }
        NewProcessName = string.Empty;
        RefreshBulkText();
    }

    private void RemoveProcess(BlacklistProcessItem item)
    {
        if (item != null)
        {
            BlacklistedProcesses.Remove(item);
            RefreshBulkText();
        }
    }

    private void EditProcess(BlacklistProcessItem item)
    {
        if (item == null)
            return;

        NewProcessName = item.Value;
        BlacklistedProcesses.Remove(item);
        RefreshBulkText();
    }

    private void RefreshBulkText() => BlacklistText = JoinLines();

    private string JoinLines() => string.Join(Environment.NewLine, BlacklistedProcesses.Select(x => x.Value));

    private List<string> NormalizeItems() => BlacklistedProcesses
            .Select(x => x.Value.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private void ApplyBulkText()
    {
        var parsed = (BlacklistText ?? string.Empty)
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
            .Select(x => x.Trim().Trim('"'))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        BlacklistedProcesses.Clear();
        foreach (var val in parsed)
        {
            BlacklistedProcesses.Add(new BlacklistProcessItem(val));
        }
        RefreshBulkText();
    }
}

public sealed class BlacklistProcessItem
{
    public BlacklistProcessItem(string value) => Value = value;

    public string Value { get; }
}
