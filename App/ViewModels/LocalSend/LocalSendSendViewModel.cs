using System.Collections.ObjectModel;
using System.Windows.Input;
using SwiftList.App.Helpers;
using SwiftList.App.Services;
using SwiftList.Core;
using SwiftList.Core.Services.LocalSend;
using SwiftList.Core.Services.LocalSend.Models;

namespace SwiftList.App.ViewModels.LocalSend;

public sealed class LocalSendSendViewModel : ViewModelBase, IDisposable
{
    private readonly ObservableCollection<LocalSendDeviceInfo> _discoveredDevices = new();
    private LocalSendDeviceInfo? _selectedDevice;
    private string _pin = string.Empty;
    private string _textToSend = string.Empty;
    private bool _isSending;
    private double _progressPercentage;
    private string? _statusKey;
    private string? _customStatusText;
    private CancellationTokenSource? _cts;

    public LocalSendSendViewModel(IEnumerable<string>? initialFiles = null, string? initialText = null)
    {
        TargetFiles = new ObservableCollection<string>(initialFiles ?? Array.Empty<string>());
        _textToSend = initialText ?? string.Empty;

        DiscoveredDevices = new ReadOnlyObservableCollection<LocalSendDeviceInfo>(_discoveredDevices);

        SendCommand = new RelayCommand(ExecuteSendAsync, () => !IsSending && SelectedDevice != null && (TargetFiles.Count > 0 || !string.IsNullOrWhiteSpace(TextToSend)));
        CancelCommand = new RelayCommand(ExecuteCancel);

        TranslationManager.Instance.PropertyChanged += OnLanguageChanged;
        LocalSendServiceManager.Instance.SessionCanceled += OnSessionCanceled;

        var discovery = LocalSendServiceManager.Instance.DiscoveryService;
        if (discovery != null)
        {
            discovery.DeviceListChanged += OnDiscoveredDevicesChanged;
            OnDiscoveredDevicesChanged(this, EventArgs.Empty);
        }
    }

    private void OnSessionCanceled(object? sender, string sessionId) => System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                                                                             {
                                                                                 if (IsSending)
                                                                                 {
                                                                                     _cts?.Cancel();
                                                                                     HandleResult(LocalSendSendResult.Declined, null);
                                                                                 }
                                                                             }));

    private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => OnPropertyChanged(nameof(StatusText));

    public ObservableCollection<string> TargetFiles { get; }
    public ReadOnlyObservableCollection<LocalSendDeviceInfo> DiscoveredDevices { get; }

    public LocalSendDeviceInfo? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                OnPropertyChanged(nameof(HasSelectedDevice));
            }
        }
    }

    public bool HasSelectedDevice => SelectedDevice != null;

    public string Pin
    {
        get => _pin;
        set => SetProperty(ref _pin, value);
    }

    public string TextToSend
    {
        get => _textToSend;
        set => SetProperty(ref _textToSend, value);
    }

    public bool IsSending
    {
        get => _isSending;
        private set
        {
            if (SetProperty(ref _isSending, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public double ProgressPercentage
    {
        get => _progressPercentage;
        private set => SetProperty(ref _progressPercentage, value);
    }

    public string StatusText
    {
        get
        {
            if (!string.IsNullOrEmpty(_customStatusText))
                return _customStatusText;
            if (!string.IsNullOrEmpty(_statusKey))
                return TranslationManager.Instance[_statusKey];
            return string.Empty;
        }
        private set
        {
            _statusKey = null;
            _customStatusText = value;
            OnPropertyChanged();
        }
    }

    public void SetStatusKey(string key)
    {
        _customStatusText = null;
        _statusKey = key;
        OnPropertyChanged(nameof(StatusText));
    }

    public ICommand SendCommand { get; }
    public ICommand CancelCommand { get; }

    private void OnDiscoveredDevicesChanged(object? sender, EventArgs e) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                                                                                 {
                                                                                     _discoveredDevices.Clear();
                                                                                     var discovery = LocalSendServiceManager.Instance.DiscoveryService;
                                                                                     if (discovery != null)
                                                                                     {
                                                                                         foreach (var dev in discovery.DiscoveredDevices)
                                                                                         {
                                                                                             _discoveredDevices.Add(dev);
                                                                                         }
                                                                                     }
                                                                                     if (SelectedDevice == null && _discoveredDevices.Count > 0)
                                                                                     {
                                                                                         SelectedDevice = _discoveredDevices[0];
                                                                                     }
                                                                                 }));

    private string _currentFileName = string.Empty;
    private string _counterText = string.Empty;
    private string _speedText = string.Empty;
    private bool _hasSentOrCanceled;

    public string CurrentFileName { get => _currentFileName; private set => SetProperty(ref _currentFileName, value); }
    public string CounterText { get => _counterText; private set => SetProperty(ref _counterText, value); }
    public string SpeedText { get => _speedText; private set => SetProperty(ref _speedText, value); }
    public bool HasSentOrCanceled { get => _hasSentOrCanceled; private set => SetProperty(ref _hasSentOrCanceled, value); }

    private async void ExecuteSendAsync()
    {
        if (SelectedDevice == null) return;

        IsSending = true;
        HasSentOrCanceled = true;
        _cts = new CancellationTokenSource();
        ProgressPercentage = 0;
        SetStatusKey("Settings_LocalSend_Sending");

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
                var fileSizes = filesList.Select(f => { try { return new System.IO.FileInfo(f).Length; } catch { return 0L; } }).ToList();
                var totalSessionBytes = fileSizes.Sum();
                var completedFilesBytes = new long[filesList.Count];

                (result, errDetails) = await LocalSendServiceManager.Instance.SendFilesAsync(
                    SelectedDevice, filesList, Pin,
                    args => System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            var idx = Math.Clamp(args.FileIndex - 1, 0, filesList.Count - 1);
                            completedFilesBytes[idx] = Math.Min(args.BytesSent, fileSizes[idx]);
                            var currentSessionSent = completedFilesBytes.Sum();

                            var pct = totalSessionBytes > 0 ? (double)currentSessionSent / totalSessionBytes * 100.0 : 0;
                            ProgressPercentage = Math.Min(100.0, pct);

                            var elapsedSec = stopwatch.Elapsed.TotalSeconds;
                            if (elapsedSec >= 0.3 || lastBytes == 0)
                            {
                                var bytesDelta = currentSessionSent - lastBytes;
                                currentSpeed = elapsedSec > 0 && bytesDelta > 0 ? bytesDelta / elapsedSec : currentSpeed;
                                lastBytes = currentSessionSent;
                                stopwatch.Restart();
                            }

                            CurrentFileName = args.FileName;
                            var completedCount = (args.TotalBytes > 0 && args.BytesSent >= args.TotalBytes)
                                ? Math.Min(args.FileIndex, args.TotalFiles)
                                : Math.Max(0, args.FileIndex - 1);
                            CounterText = $"({completedCount}/{args.TotalFiles})";
                            SpeedText = currentSpeed > 0 ? $"{FormatBytes((long)currentSpeed)}/s" : string.Empty;
                            StatusText = $"{args.FileName} ({completedCount}/{args.TotalFiles})";
                        })),
                    _cts.Token);
            }
            else
            {
                (result, errDetails) = await LocalSendServiceManager.Instance.SendTextAsync(
                    SelectedDevice, TextToSend, Pin, _cts.Token);
            }

            HandleResult(result, errDetails);
        }
        catch (OperationCanceledException) when (_cts?.IsCancellationRequested == true)
        {
            SetStatusKey("Settings_LocalSend_Canceled");
        }
        catch (Exception ex)
        {
            Logger.Log($"[LocalSendSendViewModel] Send error: {ex.Message}", LogLevel.Error);
            StatusText = $"{TranslationManager.Instance["Settings_LocalSend_ConnectionError"]} ({ex.Message})";
        }
        finally
        {
            IsSending = false;
        }
    }

    private void HandleResult(LocalSendSendResult result, string? errDetails)
    {
        switch (result)
        {
            case LocalSendSendResult.Success:
                SetStatusKey("Settings_LocalSend_Completed");
                break;
            case LocalSendSendResult.Declined:
                SetStatusKey("Settings_LocalSend_Declined");
                break;
            case LocalSendSendResult.Busy:
                SetStatusKey("Settings_LocalSend_Busy");
                break;
            case LocalSendSendResult.InvalidPin:
                SetStatusKey("Settings_LocalSend_InvalidPin");
                break;
            case LocalSendSendResult.Canceled:
                SetStatusKey("Settings_LocalSend_Canceled");
                break;
            default:
                var suffix = string.IsNullOrEmpty(errDetails) ? result.ToString() : errDetails;
                StatusText = $"{TranslationManager.Instance["Settings_LocalSend_ConnectionError"]} ({suffix})";
                break;
        }
    }

    private void ExecuteCancel()
    {
        _cts?.Cancel();
        IsSending = false;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{(double)bytes / 1024:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{(double)bytes / (1024 * 1024):F1} MB";
        return $"{(double)bytes / (1024 * 1024 * 1024):F2} GB";
    }

    public void Dispose()
    {
        TranslationManager.Instance.PropertyChanged -= OnLanguageChanged;
        LocalSendServiceManager.Instance.SessionCanceled -= OnSessionCanceled;
        var discovery = LocalSendServiceManager.Instance.DiscoveryService;
        discovery?.DeviceListChanged -= OnDiscoveredDevicesChanged;
        _cts?.Dispose();
    }
}
