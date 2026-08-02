using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SwiftList.Core.Services.LocalSend.Models;

namespace SwiftList.Core.Services.LocalSend;

public sealed class LocalSendClient : IDisposable
{
    private readonly HttpClient _httpClient;

    public LocalSendClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<LocalSendDeviceInfo?> GetDeviceInfoAsync(string ip, int port = 53317, bool https = false, CancellationToken token = default)
    {
        try
        {
            var scheme = https ? "https" : "http";
            var url = $"{scheme}://{ip}:{port}/api/localsend/v2/info";
            var response = await _httpClient.GetAsync(url, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            var device = JsonSerializer.Deserialize<LocalSendDeviceInfo>(json);
            device?.IpAddress = ip;
            return device;
        }
        catch { return null; }
    }

    public async Task<LocalSendSendResult> SendTextAsync(
        string targetIp, int targetPort, bool https, LocalSendDeviceInfo senderInfo, string text, string? pin = null, CancellationToken token = default)
    {
        var fileId = $"text_{Guid.NewGuid():N}";
        var dto = new PrepareUploadRequestDto
        {
            Info = senderInfo,
            Files = new Dictionary<string, LocalSendFileDto>
            {
                [fileId] = new LocalSendFileDto
                {
                    Id = fileId,
                    FileName = "text.txt",
                    Size = Encoding.UTF8.GetByteCount(text),
                    FileType = "text",
                    Preview = text
                }
            }
        };

        var (result, _, _) = await PrepareUploadAsync(targetIp, targetPort, https, dto, pin, token).ConfigureAwait(false);
        return result;
    }

    public async Task<LocalSendSendResult> SendFilesAsync(
        string targetIp, int targetPort, bool https, LocalSendDeviceInfo senderInfo, IReadOnlyList<string> filePaths,
        string? pin = null, Action<LocalSendSendProgressArgs>? onProgress = null, CancellationToken token = default)
    {
        if (filePaths.Count == 0) return LocalSendSendResult.Error;

        var filesDict = new Dictionary<string, LocalSendFileDto>();
        var pathMap = new Dictionary<string, string>();
        for (var i = 0; i < filePaths.Count; i++)
        {
            var path = filePaths[i];
            if (!File.Exists(path)) continue;
            var fi = new FileInfo(path);
            var id = $"file_{i}_{Guid.NewGuid():N}";
            filesDict[id] = new LocalSendFileDto
            {
                Id = id,
                FileName = fi.Name,
                Size = fi.Length,
                FileType = GetFileType(fi.Extension)
            };
            pathMap[id] = path;
        }

        if (filesDict.Count == 0) return LocalSendSendResult.Error;

        var prepareDto = new PrepareUploadRequestDto { Info = senderInfo, Files = filesDict };
        var (prepResult, sessionId, tokens) = await PrepareUploadAsync(targetIp, targetPort, https, prepareDto, pin, token).ConfigureAwait(false);
        if (prepResult != LocalSendSendResult.Success || string.IsNullOrEmpty(sessionId) || tokens == null)
            return prepResult;

        var scheme = https ? "https" : "http";
        var totalFiles = filesDict.Count;
        var currentIndex = 0;

        foreach (var kvp in filesDict)
        {
            if (token.IsCancellationRequested)
            {
                await CancelSessionAsync(targetIp, targetPort, https, sessionId, token).ConfigureAwait(false);
                return LocalSendSendResult.Canceled;
            }

            currentIndex++;
            var fileId = kvp.Key;
            var fileDto = kvp.Value;
            if (!tokens.TryGetValue(fileId, out var fileToken) || !pathMap.TryGetValue(fileId, out var filePath))
                continue;

            var uploadUrl = $"{scheme}://{targetIp}:{targetPort}/api/localsend/v2/upload?sessionId={sessionId}&fileId={fileId}&token={fileToken}&fileName={Uri.EscapeDataString(fileDto.FileName)}";

            try
            {
                using var fs = File.OpenRead(filePath);
                using var content = new ProgressiveStreamContent(fs, (sent, total) => onProgress?.Invoke(new LocalSendSendProgressArgs(fileDto.FileName, sent, total, currentIndex, totalFiles)));
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                var resp = await _httpClient.PostAsync(uploadUrl, content, token).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    await CancelSessionAsync(targetIp, targetPort, https, sessionId, token).ConfigureAwait(false);
                    return LocalSendSendResult.Error;
                }
            }
            catch (OperationCanceledException)
            {
                await CancelSessionAsync(targetIp, targetPort, https, sessionId, token).ConfigureAwait(false);
                return LocalSendSendResult.Canceled;
            }
            catch
            {
                await CancelSessionAsync(targetIp, targetPort, https, sessionId, token).ConfigureAwait(false);
                return LocalSendSendResult.Error;
            }
        }

        return LocalSendSendResult.Success;
    }

    public async Task CancelSessionAsync(string targetIp, int targetPort, bool https, string sessionId, CancellationToken token = default)
    {
        try
        {
            var scheme = https ? "https" : "http";
            var url = $"{scheme}://{targetIp}:{targetPort}/api/localsend/v2/cancel?sessionId={sessionId}";
            await _httpClient.PostAsync(url, null, token).ConfigureAwait(false);
        }
        catch { }
    }

    private async Task<(LocalSendSendResult Result, string? SessionId, Dictionary<string, string>? Tokens)> PrepareUploadAsync(
        string targetIp, int targetPort, bool https, PrepareUploadRequestDto dto, string? pin, CancellationToken token)
    {
        try
        {
            var scheme = https ? "https" : "http";
            var pinQuery = string.IsNullOrEmpty(pin) ? string.Empty : $"?pin={Uri.EscapeDataString(pin)}";
            var prepareUrl = $"{scheme}://{targetIp}:{targetPort}/api/localsend/v2/prepare-upload{pinQuery}";
            var json = JsonSerializer.Serialize(dto);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var resp = await _httpClient.PostAsync(prepareUrl, content, token).ConfigureAwait(false);
            if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden) return (LocalSendSendResult.Declined, null, null);
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized) return (LocalSendSendResult.InvalidPin, null, null);
            if ((int)resp.StatusCode == 429) return (LocalSendSendResult.TooManyAttempts, null, null);
            if (!resp.IsSuccessStatusCode) return (LocalSendSendResult.Error, null, null);

            var respJson = await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            var respDto = JsonSerializer.Deserialize<PrepareUploadResponseDto>(respJson);
            if (respDto == null || string.IsNullOrEmpty(respDto.SessionId)) return (LocalSendSendResult.Error, null, null);

            return (LocalSendSendResult.Success, respDto.SessionId, respDto.Files);
        }
        catch { return (LocalSendSendResult.Error, null, null); }
    }

    private static string GetFileType(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" => "image",
        ".mp4" or ".mkv" or ".avi" or ".mov" => "video",
        ".pdf" => "pdf",
        ".txt" or ".md" or ".json" or ".log" => "text",
        ".apk" => "apk",
        _ => "binary"
    };

    public void Dispose() => _httpClient.Dispose();
}

internal sealed class ProgressiveStreamContent : HttpContent
{
    private readonly Stream _stream;
    private readonly Action<long, long> _onProgress;

    public ProgressiveStreamContent(Stream stream, Action<long, long> onProgress)
    {
        _stream = stream;
        _onProgress = onProgress;
    }

    protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
    {
        var buffer = new byte[1024 * 1024];
        long totalRead = 0;
        int read;
        while ((read = await _stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
        {
            await stream.WriteAsync(buffer, 0, read).ConfigureAwait(false);
            totalRead += read;
            _onProgress(totalRead, _stream.Length);
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _stream.Length;
        return true;
    }
}
