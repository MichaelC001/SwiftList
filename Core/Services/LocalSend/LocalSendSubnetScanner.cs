using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using SwiftList.Core.Services.LocalSend.Models;

namespace SwiftList.Core.Services.LocalSend;

/// <summary>
/// Split out purely to keep LocalSendDiscoveryService.cs under the repo's per-file 300 line limit;
/// provides HTTP TCP subnet scanning fallback when UDP multicast is blocked by routers/firewalls.
/// </summary>
internal static class LocalSendSubnetScanner
{
    public static async Task ScanSubnetAsync(LocalSendDiscoveryService discovery, LocalSendDeviceInfo localInfo, int timeoutMs = 2000)
    {
        var localIps = GetLocalIPv4Addresses();
        var tasks = new List<Task>();

        foreach (var localIp in localIps)
        {
            var bytes = localIp.GetAddressBytes();
            for (var i = 1; i <= 254; i++)
            {
                if (i == bytes[3]) continue;
                var targetIp = $"{bytes[0]}.{bytes[1]}.{bytes[2]}.{i}";
                tasks.Add(ProbeHostAsync(discovery, localInfo, targetIp, localInfo.Port, timeoutMs));
            }
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task ProbeHostAsync(LocalSendDiscoveryService discovery, LocalSendDeviceInfo localInfo, string ip, int port, int timeoutMs)
    {
        try
        {
            using var handler = new HttpClientHandler { UseProxy = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
            var resp = await client.GetAsync($"http://{ip}:{port}/api/localsend/v2/info").ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var device = JsonSerializer.Deserialize<LocalSendDeviceInfo>(json);
                if (device != null && !string.IsNullOrEmpty(device.Alias) && device.Fingerprint != localInfo.Fingerprint)
                {
                    device.IpAddress = LocalSendServerHelper.CleanIpAddress(ip);
                    discovery.AddDiscoveredDevice(device);
                    discovery.RegisterWithAnnouncingDevice(ip, port);
                }
            }
        }
        catch { }
    }

    public static List<IPAddress> GetLocalIPv4Addresses()
    {
        var list = new List<IPAddress>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        list.Add(ip.Address);
                }
            }
        }
        catch { }
        return list;
    }
}
