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

    private async void ExecuteSendAsync()
    {
        if (SelectedDevice == null) return;

        IsSending = true;
        _cts = new CancellationTokenSource();
        ProgressPercentage = 0;
        SetStatusKey("Settings_LocalSend_Sending");

        try
        {
            LocalSendSendResult result;
            string? errDetails;
            if (TargetFiles.Count > 0)
            {
                (result, errDetails) = await LocalSendServiceManager.Instance.SendFilesAsync(
                    SelectedDevice, TargetFiles.ToList(), Pin,
                    args => System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            var pct = args.TotalBytes > 0 ? (double)args.BytesSent / args.TotalBytes * 100.0 : 0;
                            ProgressPercentage = Math.Min(100.0, pct);
                            StatusText = $"{args.FileName} ({args.FileIndex}/{args.TotalFiles})";
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

    public void Dispose()
    {
        TranslationManager.Instance.PropertyChanged -= OnLanguageChanged;
        LocalSendServiceManager.Instance.SessionCanceled -= OnSessionCanceled;
        var discovery = LocalSendServiceManager.Instance.DiscoveryService;
        discovery?.DeviceListChanged -= OnDiscoveredDevicesChanged;
        _cts?.Dispose();
    }
}
