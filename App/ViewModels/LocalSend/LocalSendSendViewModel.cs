using System.Collections.ObjectModel;
using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.Core.Services.LocalSend;
using SwiftList.Core.Services.LocalSend.Models;

namespace SwiftList.App.ViewModels.LocalSend;

public sealed class LocalSendSendViewModel : ViewModelBase, IDisposable
{
    private readonly ObservableCollection<LocalSendSendDeviceItem> _discoveredDevices = new();
    private string _textToSend = string.Empty;
    private bool _isSending;
    private double _progressPercentage;
    private string? _customStatusText;
    private int _currentStep;
    private bool _isFromAction;
    private CancellationTokenSource? _cts;

    public event EventHandler? SendSuccessCompleted;

    public LocalSendSendViewModel(IEnumerable<string>? initialFiles = null, string? initialText = null)
    {
        var hasFiles = initialFiles != null && initialFiles.Any();
        var hasText = !string.IsNullOrEmpty(initialText);
        _isFromAction = hasFiles || hasText;
        _currentStep = _isFromAction ? 1 : 0;
        _selectedMode = hasText && !hasFiles ? 1 : 0;

        TargetFiles = new ObservableCollection<string>(initialFiles ?? Array.Empty<string>());
        _textToSend = initialText ?? string.Empty;
        DiscoveredDevices = new ReadOnlyObservableCollection<LocalSendSendDeviceItem>(_discoveredDevices);

        if (hasFiles)
        {
            foreach (var f in initialFiles!)
            {
                var isDir = System.IO.Directory.Exists(f);
                CollectedItems.Add(new LocalSendCollectedItem(f, isDir));
            }
        }

        CancelCommand = new RelayCommand(ExecuteCancel);
        TranslationManager.Instance.PropertyChanged += OnLanguageChanged;

        var discovery = LocalSendServiceManager.Instance.DiscoveryService;
        if (discovery != null)
        {
            discovery.DeviceListChanged += OnDiscoveredDevicesChanged;
            OnDiscoveredDevicesChanged(this, EventArgs.Empty);
        }
    }

    private void OnDiscoveredDevicesChanged(object? sender, EventArgs e) => System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
    {
        var discovery = LocalSendServiceManager.Instance.DiscoveryService;
        if (discovery == null) return;
        var currentIps = discovery.DiscoveredDevices.Select(d => d.IpAddress).ToHashSet();

        for (var i = _discoveredDevices.Count - 1; i >= 0; i--)
        {
            if (!currentIps.Contains(_discoveredDevices[i].IpAddress)) _discoveredDevices.RemoveAt(i);
        }
        foreach (var dev in discovery.DiscoveredDevices)
        {
            var existing = _discoveredDevices.FirstOrDefault(item => item.IpAddress == dev.IpAddress);
            if (existing == null)
            {
                _discoveredDevices.Add(new LocalSendSendDeviceItem(dev));
            }
            else
            {
                existing.UpdateDevice(dev);
            }
        }
    }));

    public event EventHandler? SendingStarted;
    private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => OnPropertyChanged(nameof(StatusText));

    private int _selectedMode; // 0 = Files, 1 = Text
    public int SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (SetProperty(ref _selectedMode, value))
            {
                if (_selectedMode == 1)
                {
                    TargetFiles.Clear();
                }
                OnPropertyChanged(nameof(IsFilesMode));
                OnPropertyChanged(nameof(IsTextMode));
                OnPropertyChanged(nameof(CanGoNextStep));
            }
        }
    }

    public bool IsFilesMode => _selectedMode == 0;
    public bool IsTextMode => _selectedMode == 1;

    public int CurrentStep { get => _currentStep; set => SetProperty(ref _currentStep, value); }
    public bool IsFromAction => _isFromAction;

    public ObservableCollection<LocalSendCollectedItem> CollectedItems { get; } = new();
    public ObservableCollection<string> TargetFiles { get; }
    public ReadOnlyObservableCollection<LocalSendSendDeviceItem> DiscoveredDevices { get; }

    public string TextToSend
    {
        get => _textToSend;
        set { if (SetProperty(ref _textToSend, value)) OnPropertyChanged(nameof(CanGoNextStep)); }
    }

    public bool CanGoNextStep => IsFilesMode ? CollectedItems.Count > 0 : !string.IsNullOrWhiteSpace(_textToSend);

    public void AddPaths(IEnumerable<string> paths)
    {
        SelectedMode = 0; // Auto switch to Files mode when dropping/adding files
        foreach (var p in paths)
        {
            if (string.IsNullOrEmpty(p)) continue;
            if (CollectedItems.Any(i => string.Equals(i.Path, p, StringComparison.OrdinalIgnoreCase))) continue;
            var isDir = System.IO.Directory.Exists(p);
            if (isDir || System.IO.File.Exists(p))
            {
                CollectedItems.Add(new LocalSendCollectedItem(p, isDir));
            }
        }
        OnPropertyChanged(nameof(CanGoNextStep));
    }

    public void RemoveCollectedItem(LocalSendCollectedItem item)
    {
        CollectedItems.Remove(item);
        OnPropertyChanged(nameof(CanGoNextStep));
    }

    public void ProceedToStep1()
    {
        TargetFiles.Clear();
        if (IsFilesMode)
        {
            _textToSend = string.Empty;
            foreach (var item in CollectedItems)
            {
                TargetFiles.Add(item.Path);
            }
        }
        CurrentStep = 1;
    }

    public void ReturnToStep0() => CurrentStep = 0;

    public bool IsSending { get => _isSending; private set => SetProperty(ref _isSending, value); }
    public double ProgressPercentage { get => _progressPercentage; set => SetProperty(ref _progressPercentage, value); }

    private string _currentFileName = string.Empty;
    private string _counterText = string.Empty;
    private string _speedText = string.Empty;
    public string CurrentFileName { get => _currentFileName; private set => SetProperty(ref _currentFileName, value); }
    public string CounterText { get => _counterText; private set => SetProperty(ref _counterText, value); }
    public string SpeedText { get => _speedText; private set => SetProperty(ref _speedText, value); }

    public string StatusText
    {
        get => _customStatusText ?? string.Empty;
        set { _customStatusText = value; OnPropertyChanged(); }
    }

    public async Task StartSendBatchAsync(List<LocalSendSendDeviceItem> selectedDevices)
    {
        if (selectedDevices == null || selectedDevices.Count == 0) return;

        IsSending = true;
        _cts = new CancellationTokenSource();
        ProgressPercentage = 0;
        StatusText = TranslationManager.Instance["Settings_LocalSend_Sending"];

        var allSuccess = true;
        for (var dIdx = 0; dIdx < selectedDevices.Count; dIdx++)
        {
            if (_cts.IsCancellationRequested) break;
            var deviceItem = selectedDevices[dIdx];
            var devHeader = selectedDevices.Count > 1 ? $"[{dIdx + 1}/{selectedDevices.Count}] {deviceItem.Alias}: " : string.Empty;

            var res = await SendToSingleDeviceAsync(deviceItem, devHeader);
            if (res != LocalSendSendResult.Success) allSuccess = false;
        }

        IsSending = false;
        SpeedText = string.Empty;
        if (allSuccess && !_cts.IsCancellationRequested)
        {
            StatusText = TranslationManager.Instance["Settings_LocalSend_Completed"];
            SendSuccessCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<LocalSendSendResult> SendToSingleDeviceAsync(LocalSendSendDeviceItem item, string prefix)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        long lastBytes = 0;
        double currentSpeed = 0;

        try
        {
            LocalSendSendResult result;
            string? errDetails;

            var isSendingText = IsTextMode || (TargetFiles.Count == 0 && !string.IsNullOrWhiteSpace(TextToSend));

            if (isSendingText)
            {
                StatusText = prefix + TranslationManager.Instance["Settings_LocalSend_Waiting"];
                CurrentFileName = "Text";
                CounterText = "(1/1)";

                (result, errDetails) = await LocalSendServiceManager.Instance.SendTextAsync(
                    item.Device, TextToSend, item.Pin, _cts?.Token ?? CancellationToken.None);

                if (result == LocalSendSendResult.Success)
                {
                    ProgressPercentage = 100;
                    StatusText = prefix + TranslationManager.Instance["Settings_LocalSend_Completed"];
                }
                else
                {
                    HandleResult(result, errDetails, prefix);
                }
                return result;
            }
            else
            {
                StatusText = prefix + TranslationManager.Instance["Settings_LocalSend_Waiting"];
                var filesList = TargetFiles.ToList();
                var firstFile = filesList.FirstOrDefault();
                CurrentFileName = string.IsNullOrEmpty(firstFile) ? string.Empty : System.IO.Path.GetFileName(firstFile);
                CounterText = filesList.Count > 1 ? $"(0/{filesList.Count})" : string.Empty;
                var fileSizes = filesList.Select(f => { try { return new System.IO.FileInfo(f).Length; } catch { return 0L; } }).ToList();
                var totalSessionBytes = fileSizes.Sum();
                var completedFilesBytes = new long[filesList.Count];

                (result, errDetails) = await LocalSendServiceManager.Instance.SendFilesAsync(
                    item.Device, filesList, item.Pin,
                    args => System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var currentFileRatio = args.TotalBytes > 0 ? (double)args.BytesSent / args.TotalBytes : 0.0;
                        var fileProgress = Math.Clamp(args.FileIndex - 1 + currentFileRatio, 0.0, args.TotalFiles);
                        var pct = args.TotalFiles > 0 ? (fileProgress / args.TotalFiles) * 100.0 : 0.0;
                        ProgressPercentage = Math.Clamp(pct, 0.0, 100.0);

                        var elapsedSec = stopwatch.Elapsed.TotalSeconds;
                        if (elapsedSec >= 0.3 || lastBytes == 0)
                        {
                            var bytesDelta = args.BytesSent - lastBytes;
                            currentSpeed = elapsedSec > 0 && bytesDelta > 0 ? bytesDelta / elapsedSec : currentSpeed;
                            lastBytes = args.BytesSent;
                            stopwatch.Restart();
                        }

                        CurrentFileName = args.FileName;
                        var completedCount = (args.TotalBytes > 0 && args.BytesSent >= args.TotalBytes)
                            ? Math.Min(args.FileIndex, args.TotalFiles)
                            : Math.Max(0, args.FileIndex - 1);
                        CounterText = $"({completedCount}/{args.TotalFiles})";
                        SpeedText = currentSpeed > 0 ? $"{FormatBytes((long)currentSpeed)}/s" : string.Empty;
                        StatusText = $"{prefix}{args.FileName} ({completedCount}/{args.TotalFiles})";
                        SendingStarted?.Invoke(this, EventArgs.Empty);
                    })),
                    _cts?.Token ?? CancellationToken.None);
            }

            HandleResult(result, errDetails, prefix);
            return result;
        }
        catch (OperationCanceledException)
        {
            if (_cts != null && _cts.IsCancellationRequested)
            {
                StatusText = prefix + TranslationManager.Instance["Settings_LocalSend_Canceled"];
                return LocalSendSendResult.Canceled;
            }
            StatusText = prefix + TranslationManager.Instance["Settings_LocalSend_Declined"];
            return LocalSendSendResult.Declined;
        }
        catch (ObjectDisposedException)
        {
            if (_cts != null && _cts.IsCancellationRequested)
            {
                StatusText = prefix + TranslationManager.Instance["Settings_LocalSend_Canceled"];
                return LocalSendSendResult.Canceled;
            }
            StatusText = prefix + TranslationManager.Instance["Settings_LocalSend_Declined"];
            return LocalSendSendResult.Declined;
        }
        catch (Exception ex)
        {
            StatusText = prefix + $"{TranslationManager.Instance["Settings_LocalSend_ConnectionError"]} ({ex.Message})";
            return LocalSendSendResult.Error;
        }
    }

    private void HandleResult(LocalSendSendResult result, string? errDetails, string prefix)
    {
        switch (result)
        {
            case LocalSendSendResult.Success:
                StatusText = prefix + TranslationManager.Instance["Settings_LocalSend_Completed"];
                break;
            case LocalSendSendResult.Declined:
                StatusText = prefix + TranslationManager.Instance["Settings_LocalSend_Declined"];
                break;
            case LocalSendSendResult.Busy:
                StatusText = prefix + TranslationManager.Instance["Settings_LocalSend_Busy"];
                break;
            case LocalSendSendResult.InvalidPin:
                StatusText = prefix + TranslationManager.Instance["Settings_LocalSend_InvalidPin"];
                break;
            case LocalSendSendResult.Canceled:
                StatusText = prefix + TranslationManager.Instance["Settings_LocalSend_Canceled"];
                break;
            default:
                var suffix = string.IsNullOrEmpty(errDetails) ? result.ToString() : errDetails;
                StatusText = prefix + $"{TranslationManager.Instance["Settings_LocalSend_ConnectionError"]} ({suffix})";
                break;
        }
    }

    public ICommand CancelCommand { get; }
    private void ExecuteCancel() { try { _cts?.Cancel(); } catch (ObjectDisposedException) { } }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{(double)bytes / 1024:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{(double)bytes / (1024.0 * 1024.0):F1} MB";
        return $"{(double)bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
    }

    public void Dispose() { try { _cts?.Cancel(); } catch (ObjectDisposedException) { } }
}
