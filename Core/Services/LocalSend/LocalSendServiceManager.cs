namespace SwiftList.Core.Services.LocalSend;

public sealed class LocalSendServiceManager : IDisposable
{
    private LocalSendDiscoveryService? _discoveryService;
    private LocalSendServer? _server;

    public static LocalSendServiceManager Instance { get; } = new();

    public bool IsRunning => _server != null || _discoveryService != null;

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
        _server.DeviceInfo.Port = settings.Port;
        _server.DownloadDirectory = downloadDir;
        _server.QuickSave = settings.QuickSave;
        _server.Start(settings.Port);

        _discoveryService = new LocalSendDiscoveryService();
        _discoveryService.LocalInfo.Alias = alias;
        _discoveryService.LocalInfo.Port = _server.ActualPort > 0 ? _server.ActualPort : settings.Port;
        _server.DeviceRegistered += (s, device) => _discoveryService?.AddDiscoveredDevice(device);
        _discoveryService.Start(settings.Port);
    }

    public void Stop()
    {
        _discoveryService?.Stop();
        _discoveryService?.Dispose();
        _discoveryService = null;

        _server?.Stop();
        _server?.Dispose();
        _server = null;
    }

    public void Dispose() => Stop();
}
