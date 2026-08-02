namespace SwiftList.Core.Services.LocalSend;
using SwiftList.Core.Services.LocalSend.Models;

public sealed class LocalSendServiceManager : IDisposable
{
    private LocalSendDiscoveryService? _discoveryService;
    private LocalSendServer? _server;

    public static LocalSendServiceManager Instance { get; } = new();

    public bool IsRunning => _server != null || _discoveryService != null;
    public LocalSendDiscoveryService? DiscoveryService => _discoveryService;

    /// <summary>Raised (on a thread-pool thread) when a file has been fully received and saved to disk.</summary>
    public event EventHandler<(string FileId, string Path)>? FileReceived;

    /// <summary>Raised (on a thread-pool thread) when file transfer progress updates.</summary>
    public event EventHandler<LocalSendProgressArgs>? ProgressChanged;

    /// <summary>Raised (on a thread-pool thread) when a session is canceled.</summary>
    public event EventHandler<string>? SessionCanceled;

    /// <summary>Raised (on a thread-pool thread) when text or a link is received.</summary>
    public event EventHandler<(string SenderAlias, string Text, bool IsLink)>? TextReceived;

    /// <summary>Raised (on a thread-pool thread) when an upload request requires user confirmation.</summary>
    public event EventHandler<LocalSendUploadRequestArgs>? UploadRequested;

    /// <summary>Raised when a request to open the LocalSend send UI is initiated.</summary>
    public event EventHandler<(IReadOnlyList<string>? Files, string? Text)>? SendRequested;

    public void OpenSendWindow(IReadOnlyList<string>? files, string? text) => SendRequested?.Invoke(this, (files, text));

    public void ApplySettings(UserSettings userSettings)
    {
        var settings = userSettings.LocalSend;
        if (settings.Enabled)
        {
            Start(settings);
        }
        else
        {
            Stop();
        }
    }

    public void Start(LocalSendSettingsModel settings)
    {
        Stop();

        var alias = string.IsNullOrWhiteSpace(settings.DeviceAlias) ? Environment.MachineName : settings.DeviceAlias;
        var downloadDir = string.IsNullOrWhiteSpace(settings.DownloadDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            : settings.DownloadDirectory;

        _server = new LocalSendServer();
        _server.DeviceInfo.Alias = alias;
        _server.DeviceInfo.Port = 53317;
        _server.DownloadDirectory = downloadDir;
        _server.QuickSave = settings.QuickSave;
        _server.ReceivePin = settings.ReceivePin;
        _server.Start(53317);

        _discoveryService = new LocalSendDiscoveryService();
        _discoveryService.DiscoveryTimeout = settings.DiscoveryTimeout > 0 ? settings.DiscoveryTimeout : 2000;
        _discoveryService.LocalInfo.Alias = alias;
        _discoveryService.LocalInfo.Port = _server.ActualPort > 0 ? _server.ActualPort : 53317;
        _server.DeviceRegistered += (s, device) => _discoveryService?.AddDiscoveredDevice(device);
        _server.FileReceived += (s, e) => FileReceived?.Invoke(this, e);
        _server.ProgressChanged += (s, e) => ProgressChanged?.Invoke(this, e);
        _server.SessionCanceled += (s, e) => SessionCanceled?.Invoke(this, e);
        _server.TextReceived += (s, e) => TextReceived?.Invoke(this, e);
        _server.UploadRequested += (s, e) => UploadRequested?.Invoke(this, e);
        _discoveryService.Start(53317);
    }

    public void CancelSession(string sessionId) => _server?.CancelSession(sessionId);
    public bool IsSessionCanceled(string sessionId) => _server?.IsSessionCanceled(sessionId) == true;

    public void Stop()
    {
        _discoveryService?.Stop();
        _discoveryService?.Dispose();
        _discoveryService = null;

        _server?.Stop();
        _server?.Dispose();
        _server = null;
    }

    public async Task<(LocalSendSendResult Result, string? ErrorDetails)> SendFilesAsync(
        LocalSendDeviceInfo targetDevice, IReadOnlyList<string> filePaths, string? pin = null,
        Action<LocalSendSendProgressArgs>? onProgress = null, CancellationToken token = default)
    {
        using var client = new LocalSendClient();
        var senderInfo = _server?.DeviceInfo ?? new LocalSendDeviceInfo { Alias = Environment.MachineName };
        var res = await client.SendFilesAsync(targetDevice.IpAddress, targetDevice.Port, targetDevice.Https, senderInfo, filePaths, pin, onProgress, token).ConfigureAwait(false);
        return (res, client.LastError);
    }

    public async Task<(LocalSendSendResult Result, string? ErrorDetails)> SendTextAsync(
        LocalSendDeviceInfo targetDevice, string text, string? pin = null, CancellationToken token = default)
    {
        using var client = new LocalSendClient();
        var senderInfo = _server?.DeviceInfo ?? new LocalSendDeviceInfo { Alias = Environment.MachineName };
        var res = await client.SendTextAsync(targetDevice.IpAddress, targetDevice.Port, targetDevice.Https, senderInfo, text, pin, token).ConfigureAwait(false);
        return (res, client.LastError);
    }

    public void Dispose() => Stop();
}
