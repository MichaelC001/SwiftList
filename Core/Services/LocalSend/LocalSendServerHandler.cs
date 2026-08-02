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
                await LocalSendServer.WriteResponseAsync(stream, 412).ConfigureAwait(false);
                return;
            }

            await LocalSendServer.WriteResponseAsync(
                stream, 200, System.Text.Json.JsonSerializer.Serialize(server.DeviceInfo))
                .ConfigureAwait(false);
            return;
        }

        if (method != "POST")
        {
            await LocalSendServer.WriteResponseAsync(stream, 404).ConfigureAwait(false);
            return;
        }

        // Read body for POST
        var bodyText = string.Empty;
        if (headers.TryGetValue("Content-Length", out var lenStr) &&
            int.TryParse(lenStr, out var contentLength) && contentLength > 0)
        {
            if (IsUpload(path))
            {
                // For uploads, stream body directly; don't buffer it in memory.
                await RouteUploadAsync(server, stream, path, query, contentLength, token)
                    .ConfigureAwait(false);
                return;
            }

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
            await server.HandleRegisterAsync(stream, body, remoteEp).ConfigureAwait(false);
        }
        else if (IsPrepareUpload(path))
        {
            await server.HandlePrepareUploadAsync(stream, body, remoteEp).ConfigureAwait(false);
        }
        else if (IsCancel(path))
        {
            await LocalSendServer.WriteResponseAsync(stream, 200).ConfigureAwait(false);
        }
        else
        {
            await LocalSendServer.WriteResponseAsync(stream, 404).ConfigureAwait(false);
        }
    }

    private static async Task RouteUploadAsync(
        LocalSendServer server, Stream stream, string path,
        Dictionary<string, string> query, int contentLength, CancellationToken token)
    {
        if (!IsUpload(path))
        {
            await LocalSendServer.WriteResponseAsync(stream, 404).ConfigureAwait(false);
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
        p.Equals("/api/localsend/v2/cancel", StringComparison.OrdinalIgnoreCase);

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

/// <summary>
/// Wraps an inner stream and limits the number of bytes that can be read.
/// Used to read exactly the file body bytes declared in Content-Length.
/// </summary>
internal sealed class LengthLimitedStream(Stream inner, long limit) : Stream
{
    private long _remaining = limit;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_remaining <= 0) return 0;
        var toRead = (int)Math.Min(count, _remaining);
        var read = inner.Read(buffer, offset, toRead);
        _remaining -= read;
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_remaining <= 0) return 0;
        var toRead = (int)Math.Min(count, _remaining);
        var read = await inner.ReadAsync(buffer.AsMemory(offset, toRead), cancellationToken).ConfigureAwait(false);
        _remaining -= read;
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_remaining <= 0) return 0;
        var toRead = (int)Math.Min(buffer.Length, _remaining);
        var read = await inner.ReadAsync(buffer[..toRead], cancellationToken).ConfigureAwait(false);
        _remaining -= read;
        return read;
    }
}
