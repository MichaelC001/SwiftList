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
    private CancellationTokenSource? _cts;

    public event EventHandler? SendSuccessCompleted;

    public LocalSendSendViewModel(IEnumerable<string>? initialFiles = null, string? initialText = null)
    {
        TargetFiles = new ObservableCollection<string>(initialFiles ?? Array.Empty<string>());
        _textToSend = initialText ?? string.Empty;
        DiscoveredDevices = new ReadOnlyObservableCollection<LocalSendSendDeviceItem>(_discoveredDevices);

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

    public ObservableCollection<string> TargetFiles { get; }
    public ReadOnlyObservableCollection<LocalSendSendDeviceItem> DiscoveredDevices { get; }

    public string TextToSend { get => _textToSend; set => SetProperty(ref _textToSend, value); }
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
        StatusText = prefix + TranslationManager.Instance["Settings_LocalSend_Waiting"];
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        long lastBytes = 0;
        double currentSpeed = 0;

        try
        {
            LocalSendSendResult result;
            string? errDetails;

            if (TargetFiles.Count > 0)
            {
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
            else
            {
                (result, errDetails) = await LocalSendServiceManager.Instance.SendTextAsync(
                    item.Device, TextToSend, item.Pin, _cts?.Token ?? CancellationToken.None);
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
