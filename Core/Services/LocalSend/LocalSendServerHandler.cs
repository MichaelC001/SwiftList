using System.Net;
using System.Text;

namespace SwiftList.Core.Services.LocalSend;

/// <summary>
/// Minimal HTTP request parser and router for the LocalSend TCP server.
/// Split out purely to keep LocalSendServer.cs under the repo's per-file line limit.
/// Has no state of its own; all routing delegates back to the LocalSendServer that owns it.
/// </summary>
internal static class LocalSendServerHandler
{
    internal static async Task ProcessAsync(
        LocalSendServer server, Stream stream, EndPoint? remoteEp, CancellationToken token)
    {
        // Read request line
        var requestLine = await ReadLineAsync(stream, token).ConfigureAwait(false);
        if (string.IsNullOrEmpty(requestLine))
            return;

        var parts = requestLine.Split(' ');
        if (parts.Length < 2)
            return;

        var method = parts[0];
        var fullPath = parts[1];

        // Split path and query string
        var qIdx = fullPath.IndexOf('?');
        var path = qIdx >= 0 ? fullPath[..qIdx] : fullPath;
        var queryRaw = qIdx >= 0 ? fullPath[(qIdx + 1)..] : string.Empty;
        var query = ParseQuery(queryRaw);

        // Read headers
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string line;
        while (!string.IsNullOrEmpty(line = await ReadLineAsync(stream, token).ConfigureAwait(false)))
        {
            var colon = line.IndexOf(':');
            if (colon > 0)
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        // Fingerprint self-check for GET /info
        if (method == "GET" && IsInfo(path))
        {
            var fp = query.GetValueOrDefault("fingerprint");
            if (!string.IsNullOrEmpty(fp) && fp == server.DeviceInfo.Fingerprint)
            {
                await LocalSendServerHelper.WriteResponseAsync(stream, 412).ConfigureAwait(false);
                return;
            }

            await LocalSendServerHelper.WriteResponseAsync(
                stream, 200, System.Text.Json.JsonSerializer.Serialize(server.DeviceInfo))
                .ConfigureAwait(false);
            return;
        }

        if (method != "POST")
        {
            await LocalSendServerHelper.WriteResponseAsync(stream, 404).ConfigureAwait(false);
            return;
        }

        headers.TryGetValue("Content-Length", out var lenStr);
        int.TryParse(lenStr ?? "0", out var contentLength);

        if (IsUpload(path))
        {
            // For uploads (including 0-byte empty files), stream body directly.
            await RouteUploadAsync(server, stream, path, query, contentLength, token).ConfigureAwait(false);
            return;
        }

        // Read body for POST
        var bodyText = string.Empty;
        if (contentLength > 0)
        {
            var buf = new byte[Math.Min(contentLength, 4 * 1024 * 1024)];
            var totalRead = 0;
            while (totalRead < buf.Length)
            {
                var read = await stream.ReadAsync(buf.AsMemory(totalRead, buf.Length - totalRead), token)
                    .ConfigureAwait(false);
                if (read == 0) break;
                totalRead += read;
            }

            bodyText = Encoding.UTF8.GetString(buf, 0, totalRead);
        }

        await RoutePostAsync(server, stream, path, query, bodyText, remoteEp).ConfigureAwait(false);
    }

    private static async Task RoutePostAsync(
        LocalSendServer server, Stream stream, string path,
        Dictionary<string, string> query, string body, EndPoint? remoteEp)
    {
        if (IsRegister(path))
        {
            await LocalSendServerHelper.HandleRegisterAsync(server, stream, body, remoteEp).ConfigureAwait(false);
        }
        else if (IsPrepareUpload(path))
        {
            await HandlePrepareUploadAsync(server, stream, query, body, remoteEp).ConfigureAwait(false);
        }
        else if (IsCancel(path))
        {
            query.TryGetValue("sessionId", out var sessionId);
            if (string.IsNullOrEmpty(sessionId) && !string.IsNullOrEmpty(body))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("sessionId", out var prop))
                    {
                        sessionId = prop.GetString();
                    }
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(sessionId))
            {
                server.CancelSession(sessionId);
            }
            else
            {
                server.CancelAllSessions();
            }
            await LocalSendServerHelper.WriteResponseAsync(stream, 200).ConfigureAwait(false);
        }
        else
        {
            await LocalSendServerHelper.WriteResponseAsync(stream, 404).ConfigureAwait(false);
        }
    }

    private static async Task HandlePrepareUploadAsync(
        LocalSendServer server, Stream stream, Dictionary<string, string> query, string body, EndPoint? remoteEp)
    {
        var clientIp = remoteEp is IPEndPoint epIp ? LocalSendServerHelper.FormatIpAddress(epIp.Address) : string.Empty;
        query.TryGetValue("pin", out var requestPin);
        if (!server.CheckPin(clientIp, requestPin, out var pinStatus, out var pinErrBody))
        {
            await LocalSendServerHelper.WriteResponseAsync(stream, pinStatus, pinErrBody).ConfigureAwait(false);
            return;
        }

        if (server.IsBusy)
        {
            await LocalSendServerHelper.WriteResponseAsync(stream, 409, "{\"message\":\"Blocked by another session\"}").ConfigureAwait(false);
            return;
        }

        var dto = System.Text.Json.JsonSerializer.Deserialize<Models.PrepareUploadRequestDto>(body);
        if (dto == null || dto.Files.Count == 0)
        {
            await LocalSendServerHelper.WriteResponseAsync(stream, 400).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrEmpty(dto.Info.IpAddress) && remoteEp is IPEndPoint ep)
        {
            dto.Info.IpAddress = LocalSendServerHelper.FormatIpAddress(ep.Address);
        }

        var sessionId = Guid.NewGuid().ToString();
        server.RegisterActiveSession(sessionId, dto);

        var isTextMessage = dto.Files.Count == 1 && dto.Files.Values.Any(f =>
            string.Equals(f.FileType, "text", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(f.Preview) ||
            f.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));

        var isAccepted = server.QuickSave || isTextMessage;
        string? customDir = null;
        HashSet<string>? selectedFileIds = null;
        if (!isAccepted)
        {
            var res = await server.RequestUserAcceptanceAsync(sessionId, dto).ConfigureAwait(false);
            isAccepted = res.Accepted;
            customDir = res.CustomDir;
            selectedFileIds = res.SelectedFileIds;
        }

        if (!isAccepted || server.IsSessionCanceled(sessionId) || (selectedFileIds != null && selectedFileIds.Count == 0))
        {
            server.UnregisterSession(sessionId);
            await LocalSendServerHelper.WriteResponseAsync(stream, 403).ConfigureAwait(false);
            return;
        }

        server.RegisterCustomDirectory(sessionId, customDir);
        server.RegisterSelectedFileIds(sessionId, selectedFileIds);

        var fileTokens = new Dictionary<string, string>();
        foreach (var kv in dto.Files)
        {
            if (selectedFileIds == null || selectedFileIds.Contains(kv.Key))
            {
                fileTokens[kv.Key] = Guid.NewGuid().ToString("N");
            }
        }

        var resp = new Models.PrepareUploadResponseDto { SessionId = sessionId, Files = fileTokens };
        try
        {
            await LocalSendServerHelper.WriteResponseAsync(stream, 200, System.Text.Json.JsonSerializer.Serialize(resp)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Log($"[LocalSendServer] Failed to send prepare-upload response to sender: {ex.Message}");
            server.CancelSession(sessionId);
        }
    }

    private static async Task RouteUploadAsync(
        LocalSendServer server, Stream stream, string path,
        Dictionary<string, string> query, int contentLength, CancellationToken token)
    {
        if (!IsUpload(path))
        {
            await LocalSendServerHelper.WriteResponseAsync(stream, 404).ConfigureAwait(false);
            return;
        }

        query.TryGetValue("sessionId", out var sessionId);
        query.TryGetValue("fileId", out var fileId);
        query.TryGetValue("token", out var tok);

        // Wrap stream to limit reads to contentLength so we don't over-read
        using var limited = new LengthLimitedStream(stream, contentLength);
        await server.HandleUploadAsync(stream, limited, sessionId ?? string.Empty, fileId ?? string.Empty, tok ?? string.Empty)
            .ConfigureAwait(false);
    }

    // ---- helpers ----

    private static bool IsInfo(string p) =>
        p.Equals("/api/localsend/v2/info", StringComparison.OrdinalIgnoreCase) ||
        p.Equals("/api/localsend/v1/info", StringComparison.OrdinalIgnoreCase);

    private static bool IsRegister(string p) =>
        p.Equals("/api/localsend/v2/register", StringComparison.OrdinalIgnoreCase) ||
        p.Equals("/api/localsend/v1/register", StringComparison.OrdinalIgnoreCase);

    private static bool IsPrepareUpload(string p) =>
        p.Equals("/api/localsend/v2/prepare-upload", StringComparison.OrdinalIgnoreCase) ||
        p.Equals("/api/localsend/v1/send-request", StringComparison.OrdinalIgnoreCase);

    private static bool IsUpload(string p) =>
        p.Equals("/api/localsend/v2/upload", StringComparison.OrdinalIgnoreCase) ||
        p.Equals("/api/localsend/v1/send", StringComparison.OrdinalIgnoreCase);

    private static bool IsCancel(string p) =>
        p.Equals("/api/localsend/v2/cancel", StringComparison.OrdinalIgnoreCase) ||
        p.Equals("/api/localsend/v1/cancel", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> ParseQuery(string raw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(raw)) return result;
        foreach (var pair in raw.Split('&'))
        {
            var eq = pair.IndexOf('=');
            if (eq > 0)
                result[Uri.UnescapeDataString(pair[..eq])] = Uri.UnescapeDataString(pair[(eq + 1)..]);
        }

        return result;
    }

    private static async Task<string> ReadLineAsync(Stream stream, CancellationToken token)
    {
        var sb = new StringBuilder();
        var buf = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(buf.AsMemory(0, 1), token).ConfigureAwait(false);
            if (read == 0) break;
            var ch = (char)buf[0];
            if (ch == '\n') break;
            if (ch != '\r') sb.Append(ch);
        }

        return sb.ToString();
    }
}
