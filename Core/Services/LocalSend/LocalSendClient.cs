using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SwiftList.Core.Services.LocalSend.Models;

namespace SwiftList.Core.Services.LocalSend;

public sealed class LocalSendClient : IDisposable
{
    private readonly HttpClient _httpClient;

    public LocalSendClient() => _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<LocalSendDeviceInfo?> GetDeviceInfoAsync(string ip, int port = 53317, CancellationToken token = default)
    {
        try
        {
            var url = $"http://{ip}:{port}/api/localsend/v2/info";
            var response = await _httpClient.GetAsync(url, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            var device = JsonSerializer.Deserialize<LocalSendDeviceInfo>(json);
            device?.IpAddress = ip;
            return device;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> SendFilesAsync(
        string targetIp,
        int targetPort,
        LocalSendDeviceInfo senderInfo,
        IReadOnlyList<string> filePaths,
        Action<string, long, long>? onProgress = null,
        CancellationToken token = default)
    {
        if (filePaths.Count == 0)
            return false;

        var filesDict = new Dictionary<string, LocalSendFileDto>();
        for (var i = 0; i < filePaths.Count; i++)
        {
            var path = filePaths[i];
            if (!File.Exists(path))
                continue;

            var fileInfo = new FileInfo(path);
            var id = $"file_{i}_{Guid.NewGuid():N}";
            filesDict[id] = new LocalSendFileDto
            {
                Id = id,
                FileName = fileInfo.Name,
                Size = fileInfo.Length,
                FileType = GetFileType(fileInfo.Extension)
            };
        }

        if (filesDict.Count == 0)
            return false;

        var prepareDto = new PrepareUploadRequestDto
        {
            Info = senderInfo,
            Files = filesDict
        };

        var prepareUrl = $"http://{targetIp}:{targetPort}/api/localsend/v2/prepare-upload";
        var prepareJson = JsonSerializer.Serialize(prepareDto);
        using var prepareContent = new StringContent(prepareJson, Encoding.UTF8, "application/json");

        var prepareResponse = await _httpClient.PostAsync(prepareUrl, prepareContent, token).ConfigureAwait(false);
        if (!prepareResponse.IsSuccessStatusCode)
            return false;

        var responseJson = await prepareResponse.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        var responseDto = JsonSerializer.Deserialize<PrepareUploadResponseDto>(responseJson);
        if (responseDto == null || string.IsNullOrEmpty(responseDto.SessionId))
            return false;

        foreach (var fileKvp in filesDict)
        {
            var fileId = fileKvp.Key;
            var fileDto = fileKvp.Value;
            if (!responseDto.Files.TryGetValue(fileId, out var fileToken))
                continue;

            var filePath = filePaths.FirstOrDefault(p => Path.GetFileName(p) == fileDto.FileName);
            if (filePath == null || !File.Exists(filePath))
                continue;

            var uploadUrl = $"http://{targetIp}:{targetPort}/api/localsend/v2/upload?sessionId={responseDto.SessionId}&fileId={fileId}&token={fileToken}&fileName={Uri.EscapeDataString(fileDto.FileName)}";

            using var fileStream = File.OpenRead(filePath);
            using var streamContent = new StreamContent(fileStream);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            var uploadResponse = await _httpClient.PostAsync(uploadUrl, streamContent, token).ConfigureAwait(false);
            if (!uploadResponse.IsSuccessStatusCode)
                return false;

            onProgress?.Invoke(fileDto.FileName, fileDto.Size, fileDto.Size);
        }

        return true;
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
