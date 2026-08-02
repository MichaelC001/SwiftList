using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using SwiftList.Core.Services.LocalSend.Models;

namespace SwiftList.Core.Services.LocalSend;

/// <summary>
/// LocalSend HTTP server backed by a raw TcpListener so it works without
/// Windows URL ACL reservations or administrator privileges.
/// Handles only the LocalSend v1/v2 API surface we need.
/// </summary>
public sealed class LocalSendServer : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PrepareUploadRequestDto> _activeSessions = new();

    public LocalSendDeviceInfo DeviceInfo { get; set; } = new()
    {
        Alias = Environment.MachineName,
        DeviceModel = "Windows",
        DeviceType = "desktop",
        Port = 53317,
        Protocol = "http"
    };

    public string DownloadDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    public bool QuickSave { get; set; } = false;

    public event EventHandler<PrepareUploadRequestDto>? UploadRequested;
    public event EventHandler<(string FileId, string Path)>? FileReceived;
    public event EventHandler<LocalSendDeviceInfo>? DeviceRegistered;

    public int ActualPort { get; private set; }

    public void Start(int port = 53317)
    {
        if (_listener != null)
            return;

        _cts = new CancellationTokenSource();

        for (var p = port; p < port + 10; p++)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Any, p);
                listener.Start();
                _listener = listener;
                ActualPort = p;
                DeviceInfo.Port = p;
                Logger.Log($"[LocalSendServer] Started on port {p} (TcpListener)");
                break;
            }
            catch (Exception ex)
            {
                Logger.Log($"[LocalSendServer] Port {p} unavailable: {ex.Message}", LogLevel.Warn);
            }
        }

        if (_listener != null)
            _listenTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(token).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(client, token), token);
            }
            catch (OperationCanceledException) { break; }
            catch { await Task.Delay(200, token).ConfigureAwait(false); }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 10000;
                using var stream = client.GetStream();
                await LocalSendServerHandler.ProcessAsync(this, stream, client.Client.RemoteEndPoint, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Log($"[LocalSendServer] Client handling error: {ex.Message}", LogLevel.Warn);
            }
        }
    }

    internal async Task HandlePrepareUploadAsync(
        Stream stream, string body, EndPoint? remoteEp)
    {
        if (!QuickSave)
        {
            await WriteResponseAsync(stream, 403).ConfigureAwait(false);
            return;
        }

        var dto = JsonSerializer.Deserialize<PrepareUploadRequestDto>(body);
        if (dto == null || dto.Files.Count == 0)
        {
            await WriteResponseAsync(stream, 400).ConfigureAwait(false);
            return;
        }

        UploadRequested?.Invoke(this, dto);

        var sessionId = Guid.NewGuid().ToString("N");
        _activeSessions[sessionId] = dto;

        var fileTokens = new Dictionary<string, string>();
        foreach (var kv in dto.Files)
            fileTokens[kv.Key] = Guid.NewGuid().ToString("N");

        var resp = new PrepareUploadResponseDto { SessionId = sessionId, Files = fileTokens };
        await WriteResponseAsync(stream, 200, JsonSerializer.Serialize(resp)).ConfigureAwait(false);
    }

    internal async Task HandleUploadAsync(
        Stream stream, Stream requestBody, string sessionId, string fileId, string token)
    {
        var fileName = $"{fileId}.bin";
        if (_activeSessions.TryGetValue(sessionId, out var prepareDto) &&
            prepareDto.Files.TryGetValue(fileId, out var fileDto))
        {
            fileName = fileDto.FileName;
        }

        if (!Directory.Exists(DownloadDirectory))
            Directory.CreateDirectory(DownloadDirectory);

        var targetPath = Path.Combine(DownloadDirectory, Path.GetFileName(fileName));

        using (var dest = File.Create(targetPath))
        {
            await requestBody.CopyToAsync(dest).ConfigureAwait(false);
        }

        Logger.Log($"[LocalSendServer] Received: {fileName} -> {targetPath}");
        FileReceived?.Invoke(this, (fileId, targetPath));

        await WriteResponseAsync(stream, 200).ConfigureAwait(false);
    }

    internal async Task HandleRegisterAsync(Stream stream, string body, EndPoint? remoteEp)
    {
        var dto = JsonSerializer.Deserialize<LocalSendDeviceInfo>(body);

        if (dto != null && dto.Fingerprint == DeviceInfo.Fingerprint)
        {
            await WriteResponseAsync(stream, 412).ConfigureAwait(false);
            return;
        }

        if (dto != null && !string.IsNullOrEmpty(dto.Alias) && remoteEp is IPEndPoint ep)
        {
            dto.IpAddress = ep.Address.AddressFamily == AddressFamily.InterNetworkV6
                ? $"[{ep.Address}]" : ep.Address.ToString();
            DeviceRegistered?.Invoke(this, dto);
        }

        await WriteResponseAsync(stream, 200, JsonSerializer.Serialize(DeviceInfo)).ConfigureAwait(false);
    }

    internal static async Task WriteResponseAsync(Stream stream, int status, string? json = null)
    {
        var statusText = status switch { 200 => "OK", 400 => "Bad Request", 403 => "Forbidden", 409 => "Conflict", 412 => "Precondition Failed", _ => "Internal Server Error" };
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

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;
    }

    public void Dispose() => Stop();
}
