using System.Net;
using System.Text;
using System.Text.Json;
using SwiftList.Core.Services.LocalSend.Models;

namespace SwiftList.Core.Services.LocalSend;

public sealed class LocalSendServer : IDisposable
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public LocalSendDeviceInfo DeviceInfo { get; set; } = new()
    {
        Alias = Environment.MachineName,
        DeviceModel = "Windows",
        DeviceType = "desktop",
        Port = 53317,
        Protocol = "http"
    };

    public string DownloadDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
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
                var listener = new HttpListener();
                try
                {
                    listener.Prefixes.Add($"http://+:{p}/api/localsend/");
                    listener.Start();
                }
                catch
                {
                    listener = new HttpListener();
                    listener.Prefixes.Add($"http://*:{p}/api/localsend/");
                    listener.Start();
                }

                _listener = listener;
                ActualPort = p;
                DeviceInfo.Port = p;
                Logger.Log($"[LocalSendServer] Started HTTP server on port {p}");
                break;
            }
            catch (Exception ex)
            {
                Logger.Log($"[LocalSendServer] Port {p} unavailable: {ex.Message}", LogLevel.Warn);
            }
        }

        if (_listener != null)
        {
            _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
        }
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => ProcessRequestAsync(context), token);
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(500, token).ConfigureAwait(false);
            }
        }
    }

    private async Task ProcessRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        var path = request.Url?.AbsolutePath ?? string.Empty;

        try
        {
            if ((path.Equals("/api/localsend/v2/info", StringComparison.OrdinalIgnoreCase) || path.Equals("/api/localsend/v1/info", StringComparison.OrdinalIgnoreCase)) && request.HttpMethod == "GET")
            {
                var senderFp = request.QueryString["fingerprint"];
                if (!string.IsNullOrEmpty(senderFp) && senderFp == DeviceInfo.Fingerprint)
                {
                    response.StatusCode = 412;
                    response.Close();
                    return;
                }
                await WriteJsonAsync(response, DeviceInfo).ConfigureAwait(false);
            }
            else if ((path.Equals("/api/localsend/v2/register", StringComparison.OrdinalIgnoreCase) || path.Equals("/api/localsend/v1/register", StringComparison.OrdinalIgnoreCase)) && request.HttpMethod == "POST")
            {
                await HandleRegisterAsync(request, response).ConfigureAwait(false);
            }
            else if ((path.Equals("/api/localsend/v2/prepare-upload", StringComparison.OrdinalIgnoreCase) || path.Equals("/api/localsend/v1/send-request", StringComparison.OrdinalIgnoreCase)) && request.HttpMethod == "POST")
            {
                await HandlePrepareUploadAsync(request, response).ConfigureAwait(false);
            }
            else if ((path.Equals("/api/localsend/v2/upload", StringComparison.OrdinalIgnoreCase) || path.Equals("/api/localsend/v1/send", StringComparison.OrdinalIgnoreCase)) && request.HttpMethod == "POST")
            {
                await HandleUploadAsync(request, response).ConfigureAwait(false);
            }
            else if (path.Equals("/api/localsend/v2/cancel", StringComparison.OrdinalIgnoreCase) && request.HttpMethod == "POST")
            {
                response.StatusCode = 200;
                response.Close();
            }
            else
            {
                response.StatusCode = 404;
                response.Close();
            }
        }
        catch
        {
            try
            {
                response.StatusCode = 500;
                response.Close();
            }
            catch { }
        }
    }

    private async Task HandleRegisterAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        try
        {
            using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync().ConfigureAwait(false);
            var dto = JsonSerializer.Deserialize<LocalSendDeviceInfo>(body);
            if (dto != null && dto.Fingerprint == DeviceInfo.Fingerprint)
            {
                response.StatusCode = 412;
                response.Close();
                return;
            }

            if (dto != null && !string.IsNullOrEmpty(dto.Alias))
            {
                var rawIp = request.RemoteEndPoint.Address.ToString();
                dto.IpAddress = request.RemoteEndPoint.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                    ? $"[{rawIp}]"
                    : rawIp;
                DeviceRegistered?.Invoke(this, dto);
            }
        }
        catch { }

        await WriteJsonAsync(response, DeviceInfo).ConfigureAwait(false);
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PrepareUploadRequestDto> _activeSessions = new();

    private async Task HandlePrepareUploadAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
        var body = await reader.ReadToEndAsync().ConfigureAwait(false);
        var dto = JsonSerializer.Deserialize<PrepareUploadRequestDto>(body);

        if (dto == null)
        {
            response.StatusCode = 400;
            response.Close();
            return;
        }

        UploadRequested?.Invoke(this, dto);

        var sessionId = Guid.NewGuid().ToString("N");
        _activeSessions[sessionId] = dto;

        var fileTokens = new Dictionary<string, string>();
        foreach (var fileKvp in dto.Files)
        {
            fileTokens[fileKvp.Key] = Guid.NewGuid().ToString("N");
        }

        var responseDto = new PrepareUploadResponseDto
        {
            SessionId = sessionId,
            Files = fileTokens
        };

        response.StatusCode = 200;
        await WriteJsonAsync(response, responseDto).ConfigureAwait(false);
    }

    private async Task HandleUploadAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        var sessionId = request.QueryString["sessionId"] ?? string.Empty;
        var fileId = request.QueryString["fileId"] ?? string.Empty;

        var fileName = $"{fileId}.bin";
        if (_activeSessions.TryGetValue(sessionId, out var prepareDto) && prepareDto.Files.TryGetValue(fileId, out var fileDto))
        {
            fileName = fileDto.FileName;
        }
        else if (!string.IsNullOrEmpty(request.QueryString["fileName"]))
        {
            fileName = request.QueryString["fileName"]!;
        }

        if (!Directory.Exists(DownloadDirectory))
            Directory.CreateDirectory(DownloadDirectory);

        var targetPath = Path.Combine(DownloadDirectory, Path.GetFileName(fileName));

        using (var destStream = File.Create(targetPath))
        {
            await request.InputStream.CopyToAsync(destStream).ConfigureAwait(false);
        }

        Logger.Log($"[LocalSendServer] Received file: {fileName} -> {targetPath}");
        FileReceived?.Invoke(this, (fileId, targetPath));

        response.StatusCode = 200;
        response.Close();
    }

    private static async Task WriteJsonAsync<T>(HttpListenerResponse response, T data)
    {
        var json = JsonSerializer.Serialize(data);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentType = "application/json";
        response.ContentLength64 = bytes.Length;
        response.StatusCode = 200;

        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.OutputStream.Close();
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _listener?.Stop();
            _listener?.Close();
        }
        catch { }
        _listener = null;
    }

    public void Dispose() => Stop();
}
