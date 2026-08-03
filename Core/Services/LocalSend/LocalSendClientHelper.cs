using System.Text;
using System.Text.Json;
using SwiftList.Core.Services.LocalSend.Models;

namespace SwiftList.Core.Services.LocalSend;

/// <summary>
/// Helper methods extracted from LocalSendClient to keep LocalSendClient.cs under the repo's 300-line limit.
/// ponytail: purely helper operations on HttpContent and prepare-upload DTO serialization.
/// </summary>
internal static class LocalSendClientHelper
{
    public static string GetFileType(string extension) => extension.ToLowerInvariant() switch
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

    public static async Task<(LocalSendSendResult Result, string? SessionId, Dictionary<string, string>? Tokens, bool UsedHttps, string? LastError)> PrepareUploadAsync(
        HttpClient httpClient, JsonSerializerOptions jsonOptions, string targetIp, int targetPort, bool https, PrepareUploadRequestDto dto, string? pin, CancellationToken token)
    {
        var cleanIp = LocalSendServerHelper.CleanIpAddress(targetIp);
        var schemesToTry = new[] { https, !https };
        string? lastError = null;

        foreach (var tryHttps in schemesToTry)
        {
            try
            {
                var scheme = tryHttps ? "https" : "http";
                var pinQuery = string.IsNullOrEmpty(pin) ? string.Empty : $"?pin={Uri.EscapeDataString(pin)}";
                var prepareUrl = $"{scheme}://{cleanIp}:{targetPort}/api/localsend/v2/prepare-upload{pinQuery}";
                var json = JsonSerializer.Serialize(dto, jsonOptions);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                var resp = await httpClient.PostAsync(prepareUrl, content, token).ConfigureAwait(false);
                if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    return (LocalSendSendResult.Declined, null, null, tryHttps, "403 Forbidden (Declined)");
                }
                if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    return (LocalSendSendResult.InvalidPin, null, null, tryHttps, "401 Unauthorized (Invalid PIN)");
                }
                if ((int)resp.StatusCode == 429)
                {
                    return (LocalSendSendResult.TooManyAttempts, null, null, tryHttps, "429 Too Many Attempts");
                }
                if (resp.StatusCode == System.Net.HttpStatusCode.Conflict || (int)resp.StatusCode == 409)
                {
                    return (LocalSendSendResult.Busy, null, null, tryHttps, "409 Conflict (Busy: Blocked by another session)");
                }
                if (!resp.IsSuccessStatusCode)
                {
                    lastError = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}";
                    continue;
                }

                var respJson = await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                var respDto = JsonSerializer.Deserialize<PrepareUploadResponseDto>(respJson, jsonOptions);
                if (respDto == null || string.IsNullOrEmpty(respDto.SessionId))
                {
                    lastError = "Invalid prepare-upload response payload";
                    continue;
                }

                return (LocalSendSendResult.Success, respDto.SessionId, respDto.Files, tryHttps, null);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return (LocalSendSendResult.Canceled, null, null, https, "Canceled by user");
            }
            catch (Exception ex)
            {
                lastError = $"{ex.GetType().Name}: {ex.Message}";
                Logger.Log($"[LocalSendClient] PrepareUpload scheme {(tryHttps ? "https" : "http")} failed: {ex.Message}", LogLevel.Debug);
            }
        }

        return (LocalSendSendResult.Error, null, null, https, lastError);
    }
}
