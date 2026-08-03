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

        var (prepResult, sessionId, tokens, usedHttps, prepErr) = await LocalSendClientHelper.PrepareUploadAsync(_httpClient, JsonOptions, targetIp, targetPort, https, dto, pin, token).ConfigureAwait(false);
        if (prepResult != LocalSendSendResult.Success || string.IsNullOrEmpty(sessionId) || tokens == null || !tokens.TryGetValue(fileId, out var fileToken))
        {
            LastError = prepErr;
            return prepResult;
        }

        var scheme = usedHttps ? "https" : "http";
        var cleanIp = LocalSendServerHelper.CleanIpAddress(targetIp);
        var uploadUrl = $"{scheme}://{cleanIp}:{targetPort}/api/localsend/v2/upload?sessionId={sessionId}&fileId={fileId}&token={fileToken}&fileName=text.txt";

        try
        {
            var textBytes = Encoding.UTF8.GetBytes(text);
            using var ms = new MemoryStream(textBytes);
            using var content = new StreamContent(ms);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            var resp = await _httpClient.PostAsync(uploadUrl, content, token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                await CancelSessionAsync(cleanIp, targetPort, usedHttps, sessionId, CancellationToken.None).ConfigureAwait(false);
                return resp.StatusCode == System.Net.HttpStatusCode.Forbidden ? LocalSendSendResult.Declined : LocalSendSendResult.Error;
            }
            return LocalSendSendResult.Success;
        }
        catch (OperationCanceledException)
        {
            _ = CancelSessionAsync(cleanIp, targetPort, usedHttps, sessionId, CancellationToken.None);
            return LocalSendSendResult.Canceled;
        }
        catch (Exception ex)
        {
            _ = CancelSessionAsync(cleanIp, targetPort, usedHttps, sessionId, CancellationToken.None);
            Logger.Log($"[LocalSendClient] SendText upload error: {ex.GetType().Name} - {ex.Message}");
            return LocalSendSendResult.Canceled;
        }
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
                FileType = LocalSendClientHelper.GetFileType(fi.Extension)
            };
            pathMap[id] = path;
        }

        var prepareDto = new PrepareUploadRequestDto { Info = senderInfo, Files = filesDict };
        var (prepResult, sessionId, tokens, usedHttps, prepErr) = await LocalSendClientHelper.PrepareUploadAsync(_httpClient, JsonOptions, targetIp, targetPort, https, prepareDto, pin, token).ConfigureAwait(false);
        if (prepResult != LocalSendSendResult.Success || string.IsNullOrEmpty(sessionId) || tokens == null)
        {
            LastError = prepErr;
            return prepResult;
        }

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
                    Logger.Log($"[LocalSendClient] Upload failed for {fileDto.FileName}: HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}", LogLevel.Error);
                    await CancelSessionAsync(cleanIp, targetPort, usedHttps, sessionId, CancellationToken.None).ConfigureAwait(false);
                    if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        LastError = "403 Forbidden (Declined by receiver)";
                        return LocalSendSendResult.Declined;
                    }
                    if (resp.StatusCode == System.Net.HttpStatusCode.Conflict || (int)resp.StatusCode == 409)
                    {
                        LastError = "409 Conflict (Receiver busy)";
                        return LocalSendSendResult.Busy;
                    }
                    LastError = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}";
                    return LocalSendSendResult.Declined;
                }
            }
            catch (OperationCanceledException ocex)
            {
                if (token.IsCancellationRequested)
                {
                    await CancelSessionAsync(cleanIp, targetPort, usedHttps, sessionId, CancellationToken.None).ConfigureAwait(false);
                    Logger.Log($"[LocalSendClient] Transfer canceled by sender for {fileDto.FileName}", LogLevel.Info);
                    return LocalSendSendResult.Canceled;
                }
                Logger.Log($"[LocalSendClient] Transfer declined/canceled by receiver for {fileDto.FileName}: {ocex.Message}", LogLevel.Warn);
                LastError = "Declined by receiver";
                return LocalSendSendResult.Declined;
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested)
                {
                    await CancelSessionAsync(cleanIp, targetPort, usedHttps, sessionId, CancellationToken.None).ConfigureAwait(false);
                    return LocalSendSendResult.Canceled;
                }
                Logger.Log($"[LocalSendClient] Transfer interrupted by receiver for {fileDto.FileName}: {ex.GetType().Name} - {ex.Message}", LogLevel.Warn);
                LastError = "Declined by receiver";
                return LocalSendSendResult.Declined;
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
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var json = JsonSerializer.Serialize(new { sessionId }, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            await _httpClient.PostAsync(url, content, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Log($"[LocalSendClient] Failed to send /cancel POST: {ex.Message}", LogLevel.Warn);
        }
    }

    public string? LastError { get; private set; }

    public void Dispose() => _httpClient.Dispose();
}
