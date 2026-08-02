using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SwiftList.Core.Services.LocalSend.Models;

namespace SwiftList.Core.Services.LocalSend;

public sealed class LocalSendClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public LocalSendClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            UseProxy = false
        };
        _httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<LocalSendDeviceInfo?> GetDeviceInfoAsync(string ip, int port = 53317, bool https = false, CancellationToken token = default)
    {
        try
        {
            var cleanIp = LocalSendServerHelper.CleanIpAddress(ip);
            var scheme = https ? "https" : "http";
            var url = $"{scheme}://{cleanIp}:{port}/api/localsend/v2/info";
            var response = await _httpClient.GetAsync(url, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            var device = JsonSerializer.Deserialize<LocalSendDeviceInfo>(json);
            device?.IpAddress = cleanIp;
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

        var (result, _, _, _) = await PrepareUploadAsync(targetIp, targetPort, https, dto, pin, token).ConfigureAwait(false);
        return result;
    }

    public async Task<LocalSendSendResult> SendFilesAsync(
        string targetIp, int targetPort, bool https, LocalSendDeviceInfo senderInfo, IReadOnlyList<string> filePaths,
        string? pin = null, Action<LocalSendSendProgressArgs>? onProgress = null, CancellationToken token = default)
    {
        if (filePaths.Count == 0) return LocalSendSendResult.Error;

        var expandedFiles = new List<string>();
        foreach (var p in filePaths)
        {
            if (File.Exists(p))
            {
                expandedFiles.Add(p);
            }
            else if (Directory.Exists(p))
            {
                try
                {
                    expandedFiles.AddRange(Directory.GetFiles(p, "*", SearchOption.AllDirectories));
                }
                catch { }
            }
        }

        if (expandedFiles.Count == 0) return LocalSendSendResult.Error;

        var filesDict = new Dictionary<string, LocalSendFileDto>();
        var pathMap = new Dictionary<string, string>();
        for (var i = 0; i < expandedFiles.Count; i++)
        {
            var path = expandedFiles[i];
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

        var prepareDto = new PrepareUploadRequestDto { Info = senderInfo, Files = filesDict };
        var (prepResult, sessionId, tokens, usedHttps) = await PrepareUploadAsync(targetIp, targetPort, https, prepareDto, pin, token).ConfigureAwait(false);
        if (prepResult != LocalSendSendResult.Success || string.IsNullOrEmpty(sessionId) || tokens == null)
            return prepResult;

        var scheme = usedHttps ? "https" : "http";
        var totalFiles = filesDict.Count;
        var currentIndex = 0;
        var cleanIp = LocalSendServerHelper.CleanIpAddress(targetIp);

        foreach (var kvp in filesDict)
        {
            if (token.IsCancellationRequested)
            {
                await CancelSessionAsync(cleanIp, targetPort, usedHttps, sessionId, CancellationToken.None).ConfigureAwait(false);
                return LocalSendSendResult.Canceled;
            }

            currentIndex++;
            var fileId = kvp.Key;
            var fileDto = kvp.Value;
            if (!tokens.TryGetValue(fileId, out var fileToken) || !pathMap.TryGetValue(fileId, out var filePath))
                continue;

            var uploadUrl = $"{scheme}://{cleanIp}:{targetPort}/api/localsend/v2/upload?sessionId={sessionId}&fileId={fileId}&token={fileToken}&fileName={Uri.EscapeDataString(fileDto.FileName)}";

            try
            {
                using var fs = File.OpenRead(filePath);
                using var content = new ProgressiveStreamContent(fs, (sent, total) => onProgress?.Invoke(new LocalSendSendProgressArgs(fileDto.FileName, sent, total, currentIndex, totalFiles)));
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                var resp = await _httpClient.PostAsync(uploadUrl, content, token).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    await CancelSessionAsync(cleanIp, targetPort, usedHttps, sessionId, CancellationToken.None).ConfigureAwait(false);
                    if (LocalSendServiceManager.Instance.IsSessionCanceled(sessionId))
                    {
                        return LocalSendSendResult.Declined;
                    }
                    LastError = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}";
                    return LocalSendSendResult.Error;
                }
            }
            catch (OperationCanceledException)
            {
                // Use CancellationToken.None: the user token is already cancelled, so we must
                // send the /cancel HTTP POST on a fresh token or it silently throws and never arrives.
                await CancelSessionAsync(cleanIp, targetPort, usedHttps, sessionId, CancellationToken.None).ConfigureAwait(false);
                return LocalSendSendResult.Canceled;
            }
            catch (Exception ex)
            {
                await CancelSessionAsync(cleanIp, targetPort, usedHttps, sessionId, CancellationToken.None).ConfigureAwait(false);
                if (LocalSendServiceManager.Instance.IsSessionCanceled(sessionId))
                {
                    return LocalSendSendResult.Declined;
                }
                LastError = $"{ex.GetType().Name}: {ex.Message}";
                return LocalSendSendResult.Error;
            }
        }

        return LocalSendSendResult.Success;
    }

    public async Task CancelSessionAsync(string targetIp, int targetPort, bool https, string sessionId, CancellationToken token = default)
    {
        try
        {
            var cleanIp = LocalSendServerHelper.CleanIpAddress(targetIp);
            var scheme = https ? "https" : "http";
            var url = $"{scheme}://{cleanIp}:{targetPort}/api/localsend/v2/cancel?sessionId={sessionId}";
            await _httpClient.PostAsync(url, null, token).ConfigureAwait(false);
        }
        catch { }
    }

    public string? LastError { get; private set; }

    private async Task<(LocalSendSendResult Result, string? SessionId, Dictionary<string, string>? Tokens, bool UsedHttps)> PrepareUploadAsync(
        string targetIp, int targetPort, bool https, PrepareUploadRequestDto dto, string? pin, CancellationToken token)
    {
        var cleanIp = LocalSendServerHelper.CleanIpAddress(targetIp);
        var schemesToTry = new[] { https, !https };

        foreach (var tryHttps in schemesToTry)
        {
            try
            {
                var scheme = tryHttps ? "https" : "http";
                var pinQuery = string.IsNullOrEmpty(pin) ? string.Empty : $"?pin={Uri.EscapeDataString(pin)}";
                var prepareUrl = $"{scheme}://{cleanIp}:{targetPort}/api/localsend/v2/prepare-upload{pinQuery}";
                var json = JsonSerializer.Serialize(dto, JsonOptions);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                var resp = await _httpClient.PostAsync(prepareUrl, content, token).ConfigureAwait(false);
                if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    LastError = "403 Forbidden (Declined)";
                    return (LocalSendSendResult.Declined, null, null, tryHttps);
                }
                if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    LastError = "401 Unauthorized (Invalid PIN)";
                    return (LocalSendSendResult.InvalidPin, null, null, tryHttps);
                }
                if ((int)resp.StatusCode == 429)
                {
                    LastError = "429 Too Many Attempts";
                    return (LocalSendSendResult.TooManyAttempts, null, null, tryHttps);
                }
                if (!resp.IsSuccessStatusCode)
                {
                    LastError = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}";
                    continue;
                }

                var respJson = await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                var respDto = JsonSerializer.Deserialize<PrepareUploadResponseDto>(respJson, JsonOptions);
                if (respDto == null || string.IsNullOrEmpty(respDto.SessionId))
                {
                    LastError = "Invalid prepare-upload response payload";
                    continue;
                }

                LastError = null;
                return (LocalSendSendResult.Success, respDto.SessionId, respDto.Files, tryHttps);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                LastError = "Canceled by user";
                return (LocalSendSendResult.Canceled, null, null, https);
            }
            catch (Exception ex)
            {
                LastError = $"{ex.GetType().Name}: {ex.Message}";
                // Timeout, scheme mismatch or proxy error, fallback to alternate scheme
            }
        }

        return (LocalSendSendResult.Error, null, null, https);
    }

    private static string GetFileType(string extension) => extension.ToLowerInvariant() switch
    {
        ".apk" => "apk",
        ".pdf" => "pdf",
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".svg" or ".ico"
            or ".heic" or ".heif" or ".tiff" or ".tif" or ".psd" or ".raw" or ".arw" or ".cr2" or ".nef" or ".dng" => "image",
        ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".flv" or ".wmv" or ".m4v"
            or ".3gp" or ".3g2" or ".ts" or ".mts" or ".m2ts" or ".vob" or ".rm" or ".rmvb" => "video",
        ".txt" or ".md" or ".markdown" or ".json" or ".csv" or ".log" or ".xml" or ".html" or ".htm"
            or ".css" or ".js" or ".ts" or ".py" or ".c" or ".cpp" or ".h" or ".cs" or ".java"
            or ".sh" or ".bat" or ".cmd" or ".ps1" or ".yaml" or ".yml" or ".toml" or ".ini" or ".conf" => "text",
        _ => "other"
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
