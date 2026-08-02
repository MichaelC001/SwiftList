using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SwiftList.Core.Services.LocalSend;

/// <summary>
/// Helper methods for LocalSendServer to keep the main server class under 300 lines.
/// Split out purely to adhere to the repository's per-file line limit; has no internal state of its own.
/// </summary>
public static class LocalSendServerHelper
{
    /// <summary>
    /// Tries to create a dual-stack TcpListener (IPv6Any + DualMode=true) that accepts
    /// both IPv4 and IPv6 connections on a single socket. Returns null if IPv6 is
    /// unavailable on this host (DualMode not supported).
    /// </summary>
    internal static TcpListener? TryCreateDualStackListener(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.IPv6Any, port);
            listener.Server.DualMode = true;
            return listener;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Formats an IPAddress cleanly, unmapping IPv4-mapped IPv6 addresses (e.g. ::ffff:192.168.1.1) to standard IPv4.
    /// </summary>
    internal static string FormatIpAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4().ToString();
        }

        return address.ToString();
    }

    /// <summary>
    /// Writes an HTTP response line, headers, and optional JSON body to the network stream.
    /// </summary>
    internal static async Task WriteResponseAsync(Stream stream, int status, string? json = null)
    {
        var statusText = status switch
        {
            200 => "OK",
            400 => "Bad Request",
            403 => "Forbidden",
            409 => "Conflict",
            412 => "Precondition Failed",
            _ => "Internal Server Error"
        };

        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {status} {statusText}\r\n");
        sb.Append("Connection: close\r\n");

        if (json != null)
        {
            var body = Encoding.UTF8.GetBytes(json);
            sb.Append($"Content-Type: application/json\r\nContent-Length: {body.Length}\r\n\r\n");
            var header = Encoding.UTF8.GetBytes(sb.ToString());
            await stream.WriteAsync(header).ConfigureAwait(false);
            await stream.WriteAsync(body).ConfigureAwait(false);
        }
        else
        {
            sb.Append("Content-Length: 0\r\n\r\n");
            await stream.WriteAsync(Encoding.UTF8.GetBytes(sb.ToString())).ConfigureAwait(false);
        }

        await stream.FlushAsync().ConfigureAwait(false);
    }

    private static readonly HttpClient SharedClient = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    })
    { Timeout = TimeSpan.FromSeconds(3) };

    /// <summary>
    /// Sends an HTTP POST notification to the sender device informing them that the receiver has canceled the session.
    /// Tries v2 cancel with sessionId first, then falls back to v1 cancel for maximum compatibility.
    /// </summary>
    internal static async Task<bool> NotifySenderCanceledAsync(Models.LocalSendDeviceInfo senderInfo, string sessionId)
    {
        if (string.IsNullOrEmpty(senderInfo.IpAddress) || senderInfo.Port <= 0 || string.IsNullOrEmpty(sessionId))
            return false;

        var cleanIp = senderInfo.IpAddress.Trim('[', ']');
        var scheme = string.Equals(senderInfo.Protocol, "https", StringComparison.OrdinalIgnoreCase) ? "https" : "http";

        // 1. Try v2 cancel endpoint with sessionId first
        try
        {
            var urlV2 = $"{scheme}://{cleanIp}:{senderInfo.Port}/api/localsend/v2/cancel?sessionId={sessionId}";
            var respV2 = await SharedClient.PostAsync(urlV2, null).ConfigureAwait(false);
            Logger.Log($"[LocalSendServer] Notified sender v2 cancellation: {urlV2} -> {respV2.StatusCode}", LogLevel.Debug);
            if (respV2.IsSuccessStatusCode) return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"[LocalSendServer] v2 cancel notification error: {ex.Message}", LogLevel.Warn);
        }

        // 2. Fallback to v1 cancel endpoint for maximum compatibility
        try
        {
            var urlV1 = $"{scheme}://{cleanIp}:{senderInfo.Port}/api/localsend/v1/cancel";
            var respV1 = await SharedClient.PostAsync(urlV1, null).ConfigureAwait(false);
            Logger.Log($"[LocalSendServer] Notified sender v1 cancellation: {urlV1} -> {respV1.StatusCode}", LogLevel.Debug);
            return respV1.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Logger.Log($"[LocalSendServer] v1 cancel notification error: {ex.Message}", LogLevel.Warn);
            return false;
        }
    }

    internal static async Task HandleRegisterAsync(LocalSendServer server, Stream stream, string body, EndPoint? remoteEp)
    {
        var dto = System.Text.Json.JsonSerializer.Deserialize<Models.LocalSendDeviceInfo>(body);
        if (dto?.Fingerprint == server.DeviceInfo.Fingerprint)
        {
            await WriteResponseAsync(stream, 412).ConfigureAwait(false);
            return;
        }

        if (dto != null && !string.IsNullOrEmpty(dto.Alias) && remoteEp is IPEndPoint ep)
        {
            dto.IpAddress = ep.Address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{ep.Address}]" : ep.Address.ToString();
            server.InvokeDeviceRegistered(dto);
        }

        await WriteResponseAsync(stream, 200, System.Text.Json.JsonSerializer.Serialize(server.DeviceInfo)).ConfigureAwait(false);
    }

    internal static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                Logger.Log($"[LocalSendServer] Cleaned up partial/canceled file: {path}", LogLevel.Debug);
            }
        }
        catch (Exception deleteEx)
        {
            Logger.Log($"[LocalSendServer] Failed to delete partial file {path}: {deleteEx.Message}", LogLevel.Warn);
        }
    }

    public static string GetLocalDeviceHashtag()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                {
                    var ipStr = ip.ToString();
                    var lastDot = ipStr.LastIndexOf('.');
                    if (lastDot > 0 && lastDot < ipStr.Length - 1)
                    {
                        return $"#{ipStr[(lastDot + 1)..]}";
                    }
                }
            }
        }
        catch { }

        return "#42";
    }

    internal static bool CheckAndNotifyTextReceived(LocalSendServer server, Models.PrepareUploadRequestDto? dto, string fileId, string targetPath, string senderAlias)
    {
        try
        {
            var isText = false;
            string? textContent = null;

            if (dto?.Files.TryGetValue(fileId, out var fileDto) == true)
            {
                if (fileDto.FileType?.Equals("text", StringComparison.OrdinalIgnoreCase) == true ||
                    !string.IsNullOrEmpty(fileDto.Preview))
                {
                    isText = true;
                    textContent = fileDto.Preview;
                }
            }

            if (!isText && targetPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                isText = true;
            }

            if (isText && string.IsNullOrEmpty(textContent) && File.Exists(targetPath))
            {
                var fi = new FileInfo(targetPath);
                if (fi.Length <= 512 * 1024)
                {
                    textContent = File.ReadAllText(targetPath);
                }
            }

            if (!string.IsNullOrEmpty(textContent))
            {
                var trimmed = textContent.Trim();
                var isLink = Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
                             (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
                server.InvokeTextReceived(senderAlias, trimmed, isLink);
                return true;
            }
        }
        catch { }

        return false;
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
    }

    internal static string? ResolveTargetPath(string downloadDir, string rawFileName)
    {
        if (!Directory.Exists(downloadDir)) Directory.CreateDirectory(downloadDir);
        var normalizedRelativePath = rawFileName.Replace('\\', '/').TrimStart('/');
        var fullPathCandidate = Path.GetFullPath(Path.Combine(downloadDir, normalizedRelativePath));

        var fullDownloadDir = Path.GetFullPath(downloadDir);
        if (!fullPathCandidate.StartsWith(fullDownloadDir, StringComparison.OrdinalIgnoreCase)) return null;

        var targetDir = Path.GetDirectoryName(fullPathCandidate) ?? fullDownloadDir;
        if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

        var safeFileName = Path.GetFileName(fullPathCandidate);
        var targetPath = Path.Combine(targetDir, safeFileName);
        if (File.Exists(targetPath))
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(safeFileName);
            var ext = Path.GetExtension(safeFileName);
            var counter = 1;
            do
            {
                targetPath = Path.Combine(targetDir, $"{nameWithoutExt} ({counter}){ext}");
                counter++;
            } while (File.Exists(targetPath));
        }

        return targetPath;
    }
}
