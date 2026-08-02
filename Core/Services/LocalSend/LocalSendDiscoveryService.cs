using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using SwiftList.Core.Services.LocalSend.Models;

namespace SwiftList.Core.Services.LocalSend;

public sealed class LocalSendDiscoveryService : IDisposable
{
    public const string MulticastGroupIp = "224.0.0.167";
    public const string MulticastGroupIpV6 = "ff12::fd3a:e420";
    public const int DefaultPort = 53317;

    private readonly ConcurrentDictionary<string, LocalSendDeviceInfo> _discoveredDevices = new(StringComparer.OrdinalIgnoreCase);
    private UdpClient? _udpListener;
    private UdpClient? _udpListenerV6;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private Task? _listenTaskV6;
    private Task? _announceTask;

    public event EventHandler<LocalSendDeviceInfo>? DeviceDiscovered;
    public event EventHandler? DeviceListChanged;

    public LocalSendDeviceInfo LocalInfo { get; set; } = new()
    {
        Alias = Environment.MachineName,
        DeviceModel = "Windows",
        DeviceType = "desktop",
        Port = DefaultPort,
        Protocol = "http"
    };

    public IReadOnlyCollection<LocalSendDeviceInfo> DiscoveredDevices => _discoveredDevices.Values.ToList().AsReadOnly();

    public void Start(int port = DefaultPort)
    {
        if (_udpListener != null)
            return;

        _cts = new CancellationTokenSource();
        LocalInfo.Port = port;

        try
        {
            _udpListener = new UdpClient();
            _udpListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpListener.Client.Bind(new IPEndPoint(IPAddress.Any, port));

            var multicastIp = IPAddress.Parse(MulticastGroupIp);
            foreach (var ip in LocalSendSubnetScanner.GetLocalIPv4Addresses())
            {
                try { _udpListener.JoinMulticastGroup(multicastIp, ip); } catch { }
            }

            _udpListener.EnableBroadcast = true;
            _udpListener.MulticastLoopback = true;

            Logger.Log($"[LocalSendDiscovery] Started discovery service on port {port}. Alias={LocalInfo.Alias}, Fingerprint={LocalInfo.Fingerprint}");

            _listenTask = Task.Run(() => ListenLoopAsync(_udpListener, _cts.Token));

            try
            {
                _udpListenerV6 = new UdpClient(AddressFamily.InterNetworkV6);
                _udpListenerV6.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpListenerV6.Client.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
                _udpListenerV6.JoinMulticastGroup(IPAddress.Parse(MulticastGroupIpV6));
                _udpListenerV6.MulticastLoopback = true;
                _listenTaskV6 = Task.Run(() => ListenLoopAsync(_udpListenerV6, _cts.Token));
            }
            catch { }

            _announceTask = Task.Run(() => AnnounceLoopAsync(_cts.Token));

            _ = Task.Run(async () =>
            {
                foreach (var delay in new[] { 100, 500, 2000 })
                {
                    await Task.Delay(delay).ConfigureAwait(false);
                    await AnnounceAsync().ConfigureAwait(false);
                }
            });
        }
        catch
        {
            Stop();
        }
    }

    public async Task AnnounceAsync()
    {
        try
        {
            LocalInfo.Announcement = true;
            LocalInfo.Announce = true;
            var json = JsonSerializer.Serialize(LocalInfo);
            var bytes = Encoding.UTF8.GetBytes(json);

            var multicastEp = new IPEndPoint(IPAddress.Parse(MulticastGroupIp), LocalInfo.Port);
            var multicastEpV6 = new IPEndPoint(IPAddress.Parse(MulticastGroupIpV6), LocalInfo.Port);
            var broadcastEp = new IPEndPoint(IPAddress.Broadcast, LocalInfo.Port);

            if (_udpListener != null)
            {
                try { await _udpListener.SendAsync(bytes, bytes.Length, multicastEp).ConfigureAwait(false); } catch { }
                try { await _udpListener.SendAsync(bytes, bytes.Length, broadcastEp).ConfigureAwait(false); } catch { }
            }

            if (_udpListenerV6 != null)
            {
                try { await _udpListenerV6.SendAsync(bytes, bytes.Length, multicastEpV6).ConfigureAwait(false); } catch { }
            }

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        try
                        {
                            using var client = new UdpClient(new IPEndPoint(ip.Address, 0));
                            client.EnableBroadcast = true;
                            client.MulticastLoopback = true;
                            await client.SendAsync(bytes, bytes.Length, multicastEp).ConfigureAwait(false);
                            await client.SendAsync(bytes, bytes.Length, broadcastEp).ConfigureAwait(false);

                            var mask = ip.IPv4Mask;
                            if (mask != null)
                            {
                                var ipBytes = ip.Address.GetAddressBytes();
                                var maskBytes = mask.GetAddressBytes();
                                var bBytes = new byte[4];
                                for (var i = 0; i < 4; i++) bBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
                                await client.SendAsync(bytes, bytes.Length, new IPEndPoint(new IPAddress(bBytes), LocalInfo.Port)).ConfigureAwait(false);
                            }
                        }
                        catch { }
                    }
                    else if (ip.Address.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        try
                        {
                            using var clientV6 = new UdpClient(AddressFamily.InterNetworkV6);
                            clientV6.Client.Bind(new IPEndPoint(ip.Address, 0));
                            await clientV6.SendAsync(bytes, bytes.Length, multicastEpV6).ConfigureAwait(false);
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }
    }

    private async Task ListenLoopAsync(UdpClient listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await listener.ReceiveAsync(token).ConfigureAwait(false);
                var json = Encoding.UTF8.GetString(result.Buffer);
                var device = JsonSerializer.Deserialize<LocalSendDeviceInfo>(json);

                if (device != null && !string.IsNullOrEmpty(device.Alias) && device.Fingerprint != LocalInfo.Fingerprint)
                {
                    var rawIp = result.RemoteEndPoint.Address.ToString();
                    device.IpAddress = result.RemoteEndPoint.Address.AddressFamily == AddressFamily.InterNetworkV6
                        ? $"[{rawIp}]"
                        : rawIp;
                    device.LastSeen = DateTime.UtcNow;

                    AddDiscoveredDevice(device);

                    if (device.Announcement == true || device.Announce == true)
                    {
                        RegisterWithAnnouncingDevice(device.IpAddress, device.Port);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(1000, token).ConfigureAwait(false);
            }
        }
    }

    public void AddDiscoveredDevice(LocalSendDeviceInfo device)
    {
        if (device == null || string.IsNullOrEmpty(device.Alias) || device.Fingerprint == LocalInfo.Fingerprint)
            return;

        var key = $"{device.IpAddress}:{device.Port}";
        var isNew = !_discoveredDevices.ContainsKey(key);
        device.LastSeen = DateTime.UtcNow;
        _discoveredDevices[key] = device;

        if (isNew)
        {
            Logger.Log($"[LocalSendDiscovery] Discovered device: {device.Alias} ({device.IpAddress}:{device.Port}, model: {device.DeviceModel})", LogLevel.Debug);
            DeviceDiscovered?.Invoke(this, device);
            DeviceListChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal void RegisterWithAnnouncingDevice(string ip, int port) => _ = Task.Run(async () =>
                                                                            {
                                                                                try
                                                                                {
                                                                                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                                                                                    var regInfo = new LocalSendDeviceInfo
                                                                                    {
                                                                                        Alias = LocalInfo.Alias,
                                                                                        Version = LocalInfo.Version,
                                                                                        DeviceModel = LocalInfo.DeviceModel,
                                                                                        DeviceType = LocalInfo.DeviceType,
                                                                                        Fingerprint = LocalInfo.Fingerprint,
                                                                                        Port = LocalInfo.Port,
                                                                                        Protocol = LocalInfo.Protocol,
                                                                                        Download = LocalInfo.Download,
                                                                                        Announcement = false,
                                                                                        Announce = false
                                                                                    };

                                                                                    var json = JsonSerializer.Serialize(regInfo);
                                                                                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                                                                                    var targetHost = ip.StartsWith('[') ? ip : ip;
                                                                                    await client.PostAsync($"http://{targetHost}:{port}/api/localsend/v2/register", content).ConfigureAwait(false);
                                                                                }
                                                                                catch { }
                                                                            });

    private async Task AnnounceLoopAsync(CancellationToken token)
    {
        var loopCount = 0;
        while (!token.IsCancellationRequested)
        {
            await AnnounceAsync().ConfigureAwait(false);

            if (loopCount % 4 == 0)
            {
                _ = LocalSendSubnetScanner.ScanSubnetAsync(this, LocalInfo);
            }
            loopCount++;

            PruneStaleDevices();

            try
            {
                await Task.Delay(3000, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void PruneStaleDevices()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-60);
        var removedAny = false;

        foreach (var kvp in _discoveredDevices)
        {
            if (kvp.Value.LastSeen < cutoff)
            {
                if (_discoveredDevices.TryRemove(kvp.Key, out _))
                    removedAny = true;
            }
        }

        if (removedAny)
            DeviceListChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        _cts?.Cancel();

        try
        {
            LocalInfo.Announcement = false;
            LocalInfo.Announce = false;
            var json = JsonSerializer.Serialize(LocalInfo);
            var bytes = Encoding.UTF8.GetBytes(json);
            var multicastEp = new IPEndPoint(IPAddress.Parse(MulticastGroupIp), LocalInfo.Port);
            var multicastEpV6 = new IPEndPoint(IPAddress.Parse(MulticastGroupIpV6), LocalInfo.Port);

            if (_udpListener != null)
            {
                try { _udpListener.Send(bytes, bytes.Length, multicastEp); } catch { }
            }
            if (_udpListenerV6 != null)
            {
                try { _udpListenerV6.Send(bytes, bytes.Length, multicastEpV6); } catch { }
            }
        }
        catch { }

        _udpListener?.Close();
        _udpListener?.Dispose();
        _udpListener = null;

        _udpListenerV6?.Close();
        _udpListenerV6?.Dispose();
        _udpListenerV6 = null;

        _discoveredDevices.Clear();
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
