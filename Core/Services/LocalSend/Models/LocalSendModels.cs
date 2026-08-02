using System.Text.Json.Serialization;

namespace SwiftList.Core.Services.LocalSend.Models;

/// <summary>
/// Device information exchanged during LocalSend v2 discovery and registration.
/// </summary>
public sealed class LocalSendDeviceInfo
{
    [JsonPropertyName("alias")]
    public string Alias { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = "2.1";

    [JsonPropertyName("deviceModel")]
    public string? DeviceModel { get; set; } = "Windows";

    [JsonPropertyName("deviceType")]
    public string? DeviceType { get; set; } = "desktop";

    [JsonPropertyName("fingerprint")]
    public string Fingerprint { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("port")]
    public int Port { get; set; } = 53317;

    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = "http";

    [JsonPropertyName("download")]
    public bool Download { get; set; } = false;

    [JsonPropertyName("announcement")]
    public bool? Announcement { get; set; } = true;

    [JsonPropertyName("announce")]
    public bool? Announce { get; set; } = true;

    [JsonIgnore]
    public string IpAddress { get; set; } = string.Empty;

    [JsonIgnore]
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// File metadata entry in a prepare-upload request.
/// </summary>
public sealed class LocalSendFileDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("fileType")]
    public string FileType { get; set; } = "other";

    [JsonPropertyName("hash")]
    public string? Hash { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("preview")]
    public string? Preview { get; set; }
}

/// <summary>
/// Request payload for POST /api/localsend/v2/prepare-upload.
/// </summary>
public sealed class PrepareUploadRequestDto
{
    [JsonPropertyName("info")]
    public LocalSendDeviceInfo Info { get; set; } = new();

    [JsonPropertyName("files")]
    public Dictionary<string, LocalSendFileDto> Files { get; set; } = new();
}

/// <summary>
/// Response payload for POST /api/localsend/v2/prepare-upload.
/// </summary>
public sealed class PrepareUploadResponseDto
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("files")]
    public Dictionary<string, string> Files { get; set; } = new();
}

public sealed class LocalSendUploadRequestArgs : EventArgs
{
    public PrepareUploadRequestDto Dto { get; }
    public string? CustomDownloadDirectory { get; set; }
    private readonly Action<bool> _respond;

    public LocalSendUploadRequestArgs(PrepareUploadRequestDto dto, Action<bool> respond)
    {
        Dto = dto;
        _respond = respond;
    }

    public void Respond(bool accept) => _respond(accept);
}
